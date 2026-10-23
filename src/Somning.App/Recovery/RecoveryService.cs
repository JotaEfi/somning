using System.Globalization;
using System.Text.Json;
using Serilog;
using Somning.App.Dictation;
using Somning.Core.Dictation;
using Somning.Core.Recording;
using Somning.Platform.Windows.Recording;

namespace Somning.App.Recovery;

/// <summary>One recording that existed before the hook was installed (paths only; nothing is read or repaired).</summary>
public sealed record SnapshotEntry(string Id, bool HasWav, bool HasSidecar);

/// <summary>
/// The pre-hook recording snapshot (D-05/D-11, review H9): a fast directory enumeration of existing WAV/sidecar IDs,
/// taken before the keyboard hook is installed. Recovery operates ONLY on these IDs, never on live captures.
/// </summary>
public sealed record RecoverySnapshot(IReadOnlyList<SnapshotEntry> Entries)
{
    public static RecoverySnapshot Take(string recordingsDir)
    {
        if (!Directory.Exists(recordingsDir)) return new RecoverySnapshot([]);
        var wavs = new HashSet<string>(StringComparer.Ordinal);
        var sidecars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(recordingsDir))
        {
            var extension = Path.GetExtension(path);
            var id = Path.GetFileNameWithoutExtension(path);
            if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) wavs.Add(id);
            else if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) sidecars.Add(id);
        }
        var entries = wavs.Union(sidecars).Order(StringComparer.Ordinal)
            .Select(id => new SnapshotEntry(id, wavs.Contains(id), sidecars.Contains(id))).ToArray();
        return new RecoverySnapshot(entries);
    }
}

/// <summary>
/// Startup recovery (AUD-04, D-11, reviews H6/H9/M7). Runs right after the hook is installed, without awaiting engine
/// readiness. Adopts orphan WAVs, fails captured sidecars whose audio is gone, repairs only WAVs that still need
/// inference, then posts oldest-first <see cref="RecoveryRequested"/> events into the SAME FIFO as live dictations and
/// always completes admission with <see cref="RecoveryAdmissionCompleted"/>. Recovered text goes through the reducer's
/// CopyOnly path (clipboard only, never a paste). After every recovery outcome, retention keeps the newest 20
/// recordings and never deletes undelivered or live ones; then a replayable <c>recovery_complete</c> is notified.
/// </summary>
public sealed class RecoveryService
{
    public const int KeepRecordings = 20;
    private const string OrphanMode = "unknown";
    private readonly RecoverySnapshot snapshot;
    private readonly SidecarStore sidecars;
    private readonly string recordingsDir;
    private readonly DictationHost host;
    private readonly Func<CancellationToken, Task>? beforeProcessing;

    public RecoveryService(RecoverySnapshot snapshot, SidecarStore sidecars, string recordingsDir, DictationHost host,
        Func<CancellationToken, Task>? beforeProcessing = null)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.sidecars = sidecars ?? throw new ArgumentNullException(nameof(sidecars));
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingsDir);
        this.recordingsDir = Path.GetFullPath(recordingsDir);
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.beforeProcessing = beforeProcessing;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var tracker = new OutcomeTracker();
        host.Notified += tracker.OnNotified;
        try
        {
            var requested = 0;
            try
            {
                if (beforeProcessing is not null) await beforeProcessing(ct).ConfigureAwait(false);
                foreach (var request in Prepare())
                {
                    tracker.Expect(request.Id);
                    host.Post(request);
                    requested++;
                    Log.Information("recovery requested {Id} saved_text={SavedText} attempts={Attempts}",
                        request.Id, request.SavedText is not null, request.Attempts);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error(ex, "Recovery preparation failed; admission completes anyway");
            }
            finally
            {
                // Never leave dispatch stuck: live dictations queued meanwhile are released after the recovery IDs.
                host.Post(new RecoveryAdmissionCompleted());
            }

            await tracker.WhenAllAsync(ct).ConfigureAwait(false);
            ApplyRetention();
            Log.Information("recovery complete requested={Requested}", requested);
            host.Notify("recovery_complete");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            host.Notified -= tracker.OnNotified;
        }
    }

    /// <summary>Normalizes snapshot sidecars, plans oldest-first, repairs WAVs that need inference.</summary>
    private IReadOnlyList<RecoveryRequested> Prepare()
    {
        var loaded = new List<RecordingSidecar>();
        foreach (var entry in snapshot.Entries)
        {
            var sidecar = Normalize(entry);
            if (sidecar is not null) loaded.Add(sidecar);
        }

        var plan = RecoveryPlanner.Plan(loaded);
        foreach (var id in plan.ExhaustedIds)
            Log.Warning("recovery exhausted {Id}; files kept", id);

        var byId = loaded.ToDictionary(sidecar => sidecar.Id, StringComparer.Ordinal);
        var requests = new List<RecoveryRequested>(plan.Items.Count);
        foreach (var item in plan.Items)
        {
            var wavPath = WavPath(item.Id);
            var savedText = string.IsNullOrWhiteSpace(item.SavedText) ? null : item.SavedText;
            if (savedText is null) Repair(item.Id, wavPath);
            var sidecar = byId[item.Id];
            requests.Add(new RecoveryRequested(item.Id, wavPath, savedText, sidecar.Attempts, sidecar.Mode));
        }
        return requests;
    }

    /// <summary>Orphan WAV → captured sidecar; pending sidecar without usable audio or text → failed missing_wav.</summary>
    private RecordingSidecar? Normalize(SnapshotEntry entry)
    {
        var hasWav = entry.HasWav && File.Exists(WavPath(entry.Id));
        var sidecar = entry.HasSidecar ? TryLoad(entry.Id) : null;
        if (sidecar is null)
        {
            if (!hasWav) return null;
            sidecar = new RecordingSidecar(entry.Id, CreatedUtc(entry.Id), RecordingStage.Captured, OrphanMode,
                null, null, null, 0, null);
            sidecars.Save(sidecar);
            Log.Warning("recovery adopted orphan recording {Id}", entry.Id);
            return sidecar;
        }

        var pending = sidecar.Stage is RecordingStage.Captured or RecordingStage.Transcribed;
        if (pending && !hasWav && string.IsNullOrWhiteSpace(sidecar.Text))
        {
            sidecar = sidecar with
            {
                Stage = RecordingStage.Failed, Outcome = "error", Reason = "missing_wav", LastError = "missing_wav"
            };
            sidecars.Save(sidecar);
            Log.Warning("recovery failed {Id} reason=missing_wav", entry.Id);
        }
        return sidecar;
    }

    private RecordingSidecar? TryLoad(string id)
    {
        try { return sidecars.Load(id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Log.Warning(ex, "recovery skipped unreadable sidecar {Id}", id);
            return null;
        }
    }

    private static void Repair(string id, string wavPath)
    {
        try
        {
            if (WavHeaderRepair.EnsureValid(wavPath)) Log.Information("recovery repaired wav header {Id}", id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "recovery could not repair {Id}", id);
        }
    }

    /// <summary>Newest <see cref="KeepRecordings"/> kept; only terminal recordings deleted; live/pending IDs protected.</summary>
    private void ApplyRetention()
    {
        try
        {
            var state = host.State;
            var protectedIds = new HashSet<string>(state.Records.Keys, StringComparer.Ordinal);
            if (state.Capture is { } capture) protectedIds.Add(capture.Id);
            if (state.NextStart is { } next) protectedIds.Add(next.Id);
            foreach (var id in RetentionPolicy.SelectForDeletion(sidecars.LoadAll(), KeepRecordings))
            {
                if (protectedIds.Contains(id)) continue;
                try
                {
                    sidecars.Delete(id);
                    Log.Information("retention deleted {Id}", id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warning(ex, "retention could not delete {Id}", id);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "retention pass failed");
        }
    }

    private string WavPath(string id) => Path.Combine(recordingsDir, id + ".wav");

    /// <summary>IDs are local yyyyMMdd-HHmmss-fff; fall back to the WAV's creation time.</summary>
    private DateTimeOffset CreatedUtc(string id)
    {
        if (id.Length >= 19 && DateTime.TryParseExact(id[..19], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var local))
            return new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero);
        return new DateTimeOffset(File.GetCreationTimeUtc(WavPath(id)), TimeSpan.Zero);
    }

    /// <summary>Completes when every requested recovery ID has produced its outcome notification.</summary>
    private sealed class OutcomeTracker
    {
        private readonly object sync = new();
        private readonly HashSet<string> waiting = new(StringComparer.Ordinal);
        private readonly HashSet<string> done = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource allDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool sealedForAdditions;

        public void Expect(string id)
        {
            lock (sync)
                if (!done.Contains(id)) waiting.Add(id);
        }

        public void OnNotified(DictationNotification notification)
        {
            if (notification.Kind != "outcome" || notification.Id is null) return;
            lock (sync)
            {
                if (!waiting.Remove(notification.Id)) return;
                done.Add(notification.Id);
                if (sealedForAdditions && waiting.Count == 0) allDone.TrySetResult();
            }
        }

        public Task WhenAllAsync(CancellationToken ct)
        {
            lock (sync)
            {
                sealedForAdditions = true;
                if (waiting.Count == 0) allDone.TrySetResult();
            }
            return allDone.Task.WaitAsync(ct);
        }
    }
}
