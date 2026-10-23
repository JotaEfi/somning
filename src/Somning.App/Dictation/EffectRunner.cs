using System.Collections.Concurrent;
using System.Collections.Immutable;
using Serilog;
using Somning.App.Logging;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Recording;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Recording;

namespace Somning.App.Dictation;

/// <summary>
/// Executes reducer effects. UpdateSidecar is persisted synchronously before later effects of the same batch
/// (review H6); long-running effects run as tasks and report back only by posting events stamped with
/// Environment.TickCount64, the hook's clock (review M9). Adapters never touch reducer state.
/// </summary>
public sealed class EffectRunner : IAsyncDisposable
{
    private readonly Action<DictationEvent> post;
    private readonly SidecarStore sidecars;
    private readonly string recordingsDir;
    private readonly Func<IAudioSource> audioSourceFactory;
    private readonly ITargetProbe targetProbe;
    private readonly ITranscriber transcriber;
    private readonly ITextOutput output;
    private readonly ITranscriptHistory history;
    private readonly ConcurrentDictionary<string, CaptureSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> transcribing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> createdUtc = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> running = new();
    private readonly CancellationTokenSource lifetime = new();

    public EffectRunner(Action<DictationEvent> post, SidecarStore sidecars, string recordingsDir,
        Func<IAudioSource> audioSourceFactory, ITargetProbe targetProbe, ITranscriber transcriber, ITextOutput output,
        ITranscriptHistory? history = null)
    {
        this.post = post ?? throw new ArgumentNullException(nameof(post));
        this.sidecars = sidecars ?? throw new ArgumentNullException(nameof(sidecars));
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingsDir);
        this.recordingsDir = Path.GetFullPath(recordingsDir);
        this.audioSourceFactory = audioSourceFactory ?? throw new ArgumentNullException(nameof(audioSourceFactory));
        this.targetProbe = targetProbe ?? throw new ArgumentNullException(nameof(targetProbe));
        this.transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.history = history ?? new TranscriptHistory();
    }

    /// <summary>Runs one reducer batch in order. Called on the loop task only.</summary>
    public void Run(ImmutableArray<DictationEffect> effects)
    {
        foreach (var effect in effects)
        {
            try { RunOne(effect); }
            catch (Exception ex) { Log.Error(ex, "Effect {EffectType} failed", effect.GetType().Name); }
        }
    }

    /// <summary>Maps the supervisor's RequestWritten (wav path) to a transcribe_started notification.</summary>
    public void OnRequestWritten(string wavPath)
    {
        if (transcribing.TryGetValue(Path.GetFullPath(wavPath), out var id))
            Notify("transcribe_started", id);
    }

    private void RunOne(DictationEffect effect)
    {
        switch (effect)
        {
            case UpdateSidecar update: Persist(update.Sidecar); break;
            case StartCapture start: BeginCapture(start.Id); break;
            case StopCapture stop: EndCapture(stop.Id); break;
            case DiscardCapture discard: Discard(discard.Id); break;
            case Transcribe transcribe: Track(TranscribeAsync(transcribe.Id, transcribe.WavPath)); break;
            case Deliver deliver:
                history.Record(deliver.Text);
                Track(DeliverAsync(deliver.Id, ct => DeliverToTargetAsync(deliver, ct)));
                break;
            case CopyOnly copy:
                history.Record(copy.Text);
                Track(DeliverAsync(copy.Id, ct => output.CopyOnlyAsync(copy.Text, ct)));
                break;
            case EmitMetrics emit:
                MetricsLogger.Write(emit.Metrics);
                lock (createdUtc) createdUtc.Remove(emit.Metrics.Id);
                post(new NotificationPosted(new DictationNotification("outcome", emit.Metrics.Id, emit.Metrics)));
                break;
            default: Log.Warning("Unknown effect {EffectType}", effect.GetType().Name); break;
        }
    }

    // ---- Sidecar ---------------------------------------------------------------------------------------------

    /// <summary>Synchronous save; stamps the real creation time once and preserves it afterwards.</summary>
    private void Persist(RecordingSidecar sidecar)
    {
        DateTimeOffset created;
        lock (createdUtc)
        {
            if (!createdUtc.TryGetValue(sidecar.Id, out created))
            {
                var stored = sidecars.Load(sidecar.Id)?.CreatedUtc;
                created = stored is { } value && value != DateTimeOffset.UnixEpoch ? value : DateTimeOffset.UtcNow;
                createdUtc[sidecar.Id] = created;
            }
        }
        sidecars.Save(sidecar.CreatedUtc == DateTimeOffset.UnixEpoch ? sidecar with { CreatedUtc = created } : sidecar);
    }

    // ---- Capture ---------------------------------------------------------------------------------------------

    private void BeginCapture(string id)
    {
        var wavPath = Path.Combine(recordingsDir, id + ".wav");
        SpoolWriter writer;
        IAudioSource source;
        try
        {
            writer = new SpoolWriter(wavPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Spool could not be created for {Id}", id);
            post(new CaptureStartFailed(id, "spool_failed"));
            return;
        }
        try { source = audioSourceFactory(); }
        catch (Exception ex)
        {
            writer.Dispose();
            Log.Warning(ex, "Audio source could not be created for {Id}", id);
            post(new CaptureStartFailed(id, "no_device"));
            return;
        }

        // The fault handler is attached before StartAsync so a fault at any point reaches this session (M11).
        var started = new TaskCompletionSource<AudioStartInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new CaptureSession(id, writer, source, started.Task) { WavPath = wavPath };
        source.Faulted += fault => OnFaulted(session, fault);
        sessions[id] = session;
        Track(RunStartAsync(session, started));
    }

    private async Task RunStartAsync(CaptureSession session, TaskCompletionSource<AudioStartInfo> started)
    {
        AudioStartInfo info;
        try
        {
            info = await session.Source.StartAsync(session.Writer, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Capture start failed for {Id}", session.Id);
            started.TrySetException(ex);
            _ = started.Task.Exception; // observed: waiters (stop/fault) return without posting
            sessions.TryRemove(session.Id, out _);
            await session.ReleaseAsync().ConfigureAwait(false);
            post(new CaptureStartFailed(session.Id, StartFailureCode(ex)));
            return;
        }
        started.TrySetResult(info);
        post(new CaptureStarted(session.Id, info.MicOpenMs));
    }

    /// <summary>D-13: the paste target is captured at stop, BEFORE the capture is stopped.</summary>
    private void EndCapture(string id)
    {
        var target = targetProbe.Capture();
        Notify("target_captured", id);
        if (!sessions.TryGetValue(id, out var session)) return;
        Track(StopAsync(session, target));
    }

    /// <summary>HOTK-04: Esc cancellation stops capture and discards the spool file without delivery.</summary>
    private void Discard(string id)
    {
        Notify("capture_discarded", id);
        if (!sessions.TryGetValue(id, out var session)) return;
        Track(DiscardAsync(session));
    }

    private async Task DiscardAsync(CaptureSession session)
    {
        if (!session.TryBeginEnd()) return;
        if (!await StartSucceededAsync(session).ConfigureAwait(false)) return;
        await StopAndReleaseAsync(session).ConfigureAwait(false);
        try
        {
            if (File.Exists(session.WavPath))
                File.Delete(session.WavPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete discarded WAV {Path}", session.WavPath);
        }
        post(new CaptureStopped(session.Id, default(PasteTarget), 0, session.WavPath));
    }

    /// <summary>
    /// Serialized per capture (H8): a stop issued while StartAsync is pending waits for it; a failed start already
    /// posted CaptureStartFailed, and a capture that already faulted already posted CaptureFaulted.
    /// </summary>
    private async Task StopAsync(CaptureSession session, PasteTarget target)
    {
        if (!session.TryBeginEnd()) return;
        if (!await StartSucceededAsync(session).ConfigureAwait(false)) return;
        var captureMs = await StopAndReleaseAsync(session).ConfigureAwait(false);
        post(new CaptureStopped(session.Id, target, captureMs, session.WavPath));
    }

    /// <summary>Any thread (M11): marshal to a task that stops the source, finalizes the spool and posts once.</summary>
    private void OnFaulted(CaptureSession session, AudioFault fault)
    {
        if (!session.TryBeginEnd()) return; // a stop already owns this capture; its finalization covers the spool
        Track(Task.Run(() => FinalizeFaultAsync(session, fault), CancellationToken.None));
    }

    private async Task FinalizeFaultAsync(CaptureSession session, AudioFault fault)
    {
        Log.Warning("Capture fault for {Id} code={Code}", session.Id, fault.Code);
        if (!await StartSucceededAsync(session).ConfigureAwait(false)) return;
        await StopAndReleaseAsync(session).ConfigureAwait(false);
        post(new CaptureFaulted(session.Id, fault.Code, fault.Message, Environment.TickCount64));
    }

    private static async Task<bool> StartSucceededAsync(CaptureSession session)
    {
        try
        {
            await session.StartTask.ConfigureAwait(false);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Stops the source, then disposes source and writer (WAV header finalized, file preserved).</summary>
    private async Task<long> StopAndReleaseAsync(CaptureSession session)
    {
        try { await session.Source.StopAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Warning(ex, "Capture stop reported an error for {Id}", session.Id); }
        var captureMs = (long)session.Writer.Duration.TotalMilliseconds;
        sessions.TryRemove(session.Id, out _);
        try { await session.ReleaseAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Warning(ex, "Capture release reported an error for {Id}", session.Id); }
        return captureMs;
    }

    private static string StartFailureCode(Exception ex) => ex switch
    {
        OperationCanceledException => "cancelled",
        AudioDeviceUnavailableException device => device.Code,
        _ => "start_failed"
    };

    // ---- Transcription and delivery ---------------------------------------------------------------------------

    private async Task TranscribeAsync(string id, string wavPath)
    {
        var fullPath = Path.GetFullPath(wavPath);
        transcribing[fullPath] = id;
        EngineTranscription result;
        try
        {
            result = await transcriber.TranscribeAsync(fullPath, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Transcription threw for {Id}", id);
            result = new EngineTranscription("", "error", null, "engine_exception", true, 0, 0, 0, 0, 0, 0, "", "", 0);
        }
        finally { transcribing.TryRemove(fullPath, out _); }
        post(new TranscriptionCompleted(id, result, Environment.TickCount64));
    }

    // Harness-only delivery barrier (D-20): awaited asynchronously before TargetPaster; never in Release.
#if SOMNING_TEST_HARNESS
    public Func<CancellationToken, Task>? BeforeDeliver { get; set; }

    private async Task<DeliveryResult> BarrierThenDeliverAsync(Func<CancellationToken, Task> barrier, Deliver deliver, CancellationToken ct)
    {
        await barrier(ct).ConfigureAwait(false);
        return await output.DeliverAsync(deliver.Text, deliver.Target, ct).ConfigureAwait(false);
    }
#endif

    private Task<DeliveryResult> DeliverToTargetAsync(Deliver deliver, CancellationToken ct)
    {
#if SOMNING_TEST_HARNESS
        if (BeforeDeliver is { } barrier) return BarrierThenDeliverAsync(barrier, deliver, ct);
#endif
        return output.DeliverAsync(deliver.Text, deliver.Target, ct);
    }

    private async Task DeliverAsync(string id, Func<CancellationToken, Task<DeliveryResult>> deliver)
    {
        DeliveryResult result;
        try
        {
            result = await deliver(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Delivery threw for {Id}", id);
            result = new DeliveryResult("clipboard_failed", "delivery_exception", 0, 0, 0);
        }
        post(new DeliveryCompleted(id, result, Environment.TickCount64));
    }

    // ---- Plumbing --------------------------------------------------------------------------------------------

    private void Notify(string kind, string id) =>
        post(new NotificationPosted(new DictationNotification(kind, id, null)));

    private void Track(Task task)
    {
        running[task] = 0;
        _ = task.ContinueWith(done => running.TryRemove(done, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Waits for every effect task started so far (tests and shutdown).</summary>
    public async Task WhenIdleAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!running.IsEmpty && DateTime.UtcNow < until)
            await Task.WhenAny(Task.WhenAll(running.Keys), Task.Delay(50)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        foreach (var session in sessions.Values)
        {
            try { await session.ReleaseAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "Capture release failed during shutdown"); }
        }
        await WhenIdleAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        lifetime.Dispose();
    }
}
