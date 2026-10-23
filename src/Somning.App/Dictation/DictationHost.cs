using System.Collections.Immutable;
using System.Globalization;
using System.Threading.Channels;
using Serilog;
using Somning.Core.Dictation;
using Somning.Core.Trigger;

namespace Somning.App.Dictation;

/// <summary>
/// Single-writer event loop (review H8). One unbounded channel is read by ONE loop task that owns the
/// <see cref="TriggerInterpreter"/> and the <see cref="DictationState"/>. Every other thread (hook pump, 50 ms
/// timer, adapters) only posts events; OnKey/OnTick and Reduce run exclusively on the loop.
/// </summary>
public sealed class DictationHost : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private readonly Channel<DictationEvent> channel =
        Channel.CreateUnbounded<DictationEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly TriggerInterpreter interpreter;
    private readonly Func<bool> modifiersDown;
    private readonly Func<string, bool> idTaken;
    private readonly List<Task> tasks = [];
    private Action<ImmutableArray<DictationEffect>>? runEffects;
    private DictationState state = DictationState.Initial;
    private string? lastIdBase;
    private int lastIdSuffix;
    private int started;

    /// <param name="initialState">Startup state; the app passes RecoveryAdmissionPending=true (D-11, H9).</param>
    public DictationHost(Func<bool> modifiersDown, TriggerOptions? triggerOptions = null, Func<string, bool>? idTaken = null,
        DictationState? initialState = null)
    {
        state = initialState ?? DictationState.Initial;
        this.modifiersDown = modifiersDown ?? throw new ArgumentNullException(nameof(modifiersDown));
        interpreter = new TriggerInterpreter(triggerOptions);
        this.idTaken = idTaken ?? (_ => false);
    }

    /// <summary>Raised on the loop task only.</summary>
    public event Action<DictationNotification>? Notified;

    /// <summary>Snapshot for diagnostics/tests; the loop is the only writer.</summary>
    public DictationState State => Volatile.Read(ref state);

    /// <summary>Thread-safe: enqueues an event for the loop.</summary>
    public bool Post(DictationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return channel.Writer.TryWrite(e);
    }

    public void Notify(string kind, string? id = null) => Post(new NotificationPosted(new DictationNotification(kind, id, null)));

    /// <summary>Starts the loop, the 50 ms tick task and (optionally) the hook pump.</summary>
    public void Start(Action<ImmutableArray<DictationEffect>> effectSink, ChannelReader<KeyEvent>? keys = null)
    {
        ArgumentNullException.ThrowIfNull(effectSink);
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Host already started.");
        runEffects = effectSink;
        var token = lifetime.Token;
        tasks.Add(Task.Run(() => LoopAsync(token), CancellationToken.None));
        tasks.Add(Task.Run(() => TickAsync(token), CancellationToken.None));
        if (keys is not null) tasks.Add(Task.Run(() => PumpKeysAsync(keys, token), CancellationToken.None));
    }

    private async Task PumpKeysAsync(ChannelReader<KeyEvent> keys, CancellationToken ct)
    {
        try
        {
            await foreach (var key in keys.ReadAllAsync(ct).ConfigureAwait(false))
                Post(new KeyReceived(key));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Samples the clock and physical modifier state only; never touches the interpreter (H8).</summary>
    private async Task TickAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                Post(new Tick(Environment.TickCount64, modifiersDown()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try { Handle(e); }
                catch (Exception ex) { Log.Error(ex, "Event loop failed to handle {EventType}", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void Handle(DictationEvent e)
    {
        switch (e)
        {
            case KeyReceived key:
                if (key.Event.Kind == KeyEventKind.Cancel)
                {
                    interpreter.OnKey(key.Event);
                    Reduce(new CancelRequested(key.Event.TimestampMs));
                    break;
                }
                Fire(interpreter.OnKey(key.Event), key.Event.TimestampMs);
                break;
            case Tick tick:
                Fire(interpreter.OnTick(tick.NowMs, tick.ModifiersDown), tick.NowMs);
                break;
            case NotificationPosted posted:
                Notified?.Invoke(posted.Notification);
                break;
            default:
                Reduce(e);
                break;
        }
    }

    private void Fire(TriggerResult result, long nowMs)
    {
        if (result.Command == TriggerCommand.None) return;
        Reduce(new TriggerFired(result, nowMs, NewId()));
    }

    private void Reduce(DictationEvent e)
    {
        var (next, effects) = DictationController.Reduce(state, e);
        Volatile.Write(ref state, next);
        if (!effects.IsDefaultOrEmpty) runEffects!(effects);
    }

    /// <summary>yyyyMMdd-HHmmss-fff from local time, suffixed when the same millisecond repeats or the id is taken.</summary>
    private string NewId()
    {
        var idBase = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        lastIdSuffix = idBase == lastIdBase ? lastIdSuffix + 1 : 0;
        lastIdBase = idBase;
        while (true)
        {
            var id = lastIdSuffix == 0 ? idBase : idBase + "-" + lastIdSuffix.ToString(CultureInfo.InvariantCulture);
            if (!state.Records.ContainsKey(id) && !idTaken(id)) return id;
            lastIdSuffix++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        channel.Writer.TryComplete();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        lifetime.Dispose();
    }
}
