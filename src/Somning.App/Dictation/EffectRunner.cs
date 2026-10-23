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
    private readonly ConcurrentDictionary<string, CaptureSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> transcribing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> createdUtc = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> running = new();
    private readonly CancellationTokenSource lifetime = new();

    public EffectRunner(Action<DictationEvent> post, SidecarStore sidecars, string recordingsDir,
        Func<IAudioSource> audioSourceFactory, ITargetProbe targetProbe, ITranscriber transcriber, ITextOutput output)
    {
        this.post = post ?? throw new ArgumentNullException(nameof(post));
        this.sidecars = sidecars ?? throw new ArgumentNullException(nameof(sidecars));
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingsDir);
        this.recordingsDir = Path.GetFullPath(recordingsDir);
        this.audioSourceFactory = audioSourceFactory ?? throw new ArgumentNullException(nameof(audioSourceFactory));
        this.targetProbe = targetProbe ?? throw new ArgumentNullException(nameof(targetProbe));
        this.transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
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
            case Transcribe transcribe: Track(TranscribeAsync(transcribe.Id, transcribe.WavPath)); break;
            case Deliver deliver: Track(DeliverAsync(deliver.Id, ct => output.DeliverAsync(deliver.Text, deliver.Target, ct))); break;
            case CopyOnly copy: Track(DeliverAsync(copy.Id, ct => output.CopyOnlyAsync(copy.Text, ct))); break;
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

        Task<AudioStartInfo> startTask;
        try { startTask = source.StartAsync(writer, lifetime.Token); }
        catch (Exception ex) { startTask = Task.FromException<AudioStartInfo>(ex); }
        var session = new CaptureSession(id, writer, source, startTask) { WavPath = wavPath };
        sessions[id] = session;
        Track(ReportStartAsync(session));
    }

    private async Task ReportStartAsync(CaptureSession session)
    {
        try
        {
            var info = await session.StartTask.ConfigureAwait(false);
            post(new CaptureStarted(session.Id, info.MicOpenMs));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Capture start failed for {Id}", session.Id);
            sessions.TryRemove(session.Id, out _);
            await session.ReleaseAsync().ConfigureAwait(false);
            post(new CaptureStartFailed(session.Id, StartFailureCode(ex)));
        }
    }

    /// <summary>D-13: the paste target is captured at stop, BEFORE the capture is stopped.</summary>
    private void EndCapture(string id)
    {
        var target = targetProbe.Capture();
        Notify("target_captured", id);
        if (!sessions.TryGetValue(id, out var session)) return;
        Track(StopAsync(session, target));
    }

    private async Task StopAsync(CaptureSession session, PasteTarget target)
    {
        long captureMs = 0;
        try
        {
            await session.Source.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warning(ex, "Capture stop reported an error for {Id}", session.Id); }
        finally
        {
            captureMs = (long)session.Writer.Duration.TotalMilliseconds;
            sessions.TryRemove(session.Id, out _);
            await session.ReleaseAsync().ConfigureAwait(false);
        }
        post(new CaptureStopped(session.Id, target, captureMs, session.WavPath));
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
