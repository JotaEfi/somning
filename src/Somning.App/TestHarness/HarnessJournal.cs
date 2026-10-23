#if SOMNING_TEST_HARNESS
using Somning.App.Dictation;
using Somning.Core.Dictation;

namespace Somning.App.TestHarness;

/// <summary>One replayable milestone: seq is assigned from process start, 1, 2, 3, ...</summary>
public sealed record HarnessEntry(long Seq, string Kind, string? Id, DictationMetrics? Metrics);

/// <summary>
/// Debug/SOMNING_TEST_HARNESS-only event journal (D-20, review H10). Subscribed to DictationHost.Notified before the
/// loop starts, so no milestone is missed; retains the last <see cref="Capacity"/> entries (T-01-38). A waiter asks for
/// the first entry after a seq cursor, so outcomes that completed before the client connected are replayed.
/// </summary>
public sealed class HarnessJournal
{
    public const int Capacity = 512;
    private readonly object gate = new();
    private readonly Queue<HarnessEntry> entries = new();
    private TaskCompletionSource changed = NewSignal();
    private long lastSeq;

    public long LastSeq
    {
        get { lock (gate) return lastSeq; }
    }

    public void Attach(DictationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Notified += n => Record(n.Kind, n.Id, n.Metrics);
    }

    public HarnessEntry Record(string kind, string? id, DictationMetrics? metrics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        TaskCompletionSource signal;
        HarnessEntry entry;
        lock (gate)
        {
            entry = new HarnessEntry(++lastSeq, kind, id, metrics);
            entries.Enqueue(entry);
            while (entries.Count > Capacity) entries.Dequeue();
            signal = changed;
            changed = NewSignal();
        }
        signal.TrySetResult();
        return entry;
    }

    public bool Contains(string kind)
    {
        lock (gate) return entries.Any(e => e.Kind == kind);
    }

    /// <summary>First retained entry with seq &gt; afterSeq matching kind (and id when given); waits for one if none yet.</summary>
    public async Task<HarnessEntry?> WaitAsync(string kind, long afterSeq, string? id, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (true)
        {
            Task next;
            lock (gate)
            {
                var match = entries.FirstOrDefault(e => e.Seq > afterSeq && e.Kind == kind && (id is null || e.Id == id));
                if (match is not null) return match;
                next = changed.Task;
            }
            try { await next.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
#endif
