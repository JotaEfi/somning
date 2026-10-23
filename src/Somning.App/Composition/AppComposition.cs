using System.Threading.Channels;
using Serilog;
using Somning.App.Dictation;
using Somning.App.Engine;
using Somning.App.Recovery;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Ports;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Paste;
using Somning.Platform.Windows.Recording;
#if SOMNING_TEST_HARNESS
using Somning.App.TestHarness;
#endif

namespace Somning.App.Composition;

/// <summary>
/// Wires the running app: engine supervisor, clipboard/paste, target probe, keyboard hook, audio source factory,
/// sidecar store, the single-writer DictationHost and its EffectRunner. Startup ORDER is owned by Program (H9).
/// </summary>
public sealed class AppComposition : IAsyncDisposable
{
    private readonly Channel<KeyEvent> keys = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ClipboardWriter clipboard;
    private readonly CancellationTokenSource lifetime = new();
    private Task engineStart = Task.CompletedTask;
    private Task recovery = Task.CompletedTask;

    public AppComposition(Func<IAudioSource> audioSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(audioSourceFactory);
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        Directory.CreateDirectory(SomningPaths.LogsDir);
        Engine = new EngineSupervisor(options: SupervisorOptionsForHost());
        clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        var probe = new ForegroundProbe(integrity);
        var paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());
        Sidecars = new SidecarStore(SomningPaths.RecordingsDir);
        Hook = new KeyboardHook(keys.Writer);
        // Pre-hook snapshot (H9): paths/IDs only; repair, model wait and inference all happen later in RecoveryService.
        Snapshot = RecoverySnapshot.Take(SomningPaths.RecordingsDir);
        // Live captures run normally while admission is pending; only transcription/copy dispatch waits (D-05, D-11).
        Host = new DictationHost(PhysicalKeyState.AreChordModifiersDown, idTaken: IdTaken,
            initialState: DictationState.Initial with { RecoveryAdmissionPending = true });
        Runner = new EffectRunner(e => Host.Post(e), Sidecars, SomningPaths.RecordingsDir, audioSourceFactory, probe, Engine, paster);
        Engine.RequestWritten += Runner.OnRequestWritten;
        Engine.StatusChanged += status => Host.Post(new EngineStatusChanged(status));
        Host.Notified += OnNotified;
    }

    public EngineSupervisor Engine { get; }
    public KeyboardHook Hook { get; }
    public DictationHost Host { get; }
    public EffectRunner Runner { get; }
    public SidecarStore Sidecars { get; }
    public RecoverySnapshot Snapshot { get; }

#if SOMNING_TEST_HARNESS
    public HarnessJournal? Journal { get; private set; }
    public HarnessPipeServer? Pipe { get; private set; }

    /// <summary>Gate that delays startup recovery processing (armed only when a test asks for it).</summary>
    public HarnessGate RecoveryGate { get; } = new();

    /// <summary>
    /// Debug-only (D-20): the journal subscribes to Host.Notified BEFORE the loop starts so no milestone is missed,
    /// the delivery barrier is wired into the EffectRunner, and the pipe server starts listening. Call before StartLoop.
    /// </summary>
    public void EnableHarness(HarnessFixture fixture, Action requestShutdown, bool holdRecovery)
    {
        if (Journal is not null) throw new InvalidOperationException("Harness already enabled.");
        Journal = new HarnessJournal();
        Journal.Attach(Host);
        var deliveryBarrier = new HarnessGate();
        Runner.BeforeDeliver = deliveryBarrier.WaitIfArmedAsync;
        if (holdRecovery) RecoveryGate.Arm();
        Pipe = new HarnessPipeServer(Journal, new HarnessStatusSource(() => Engine.Status.ToString(), () => Host.State),
            fixture, deliveryBarrier, RecoveryGate, requestShutdown);
        Pipe.Start();
    }
#endif

    /// <summary>Starts the single-writer loop, its 50 ms tick and the hook pump (the hook itself is started by Program).</summary>
    public void StartLoop() => Host.Start(Runner.Run, keys.Reader);

    /// <summary>
    /// Starts the engine in the background and returns immediately: nothing on the startup path awaits readiness,
    /// so the hook and loop work during model load; dictations captured meanwhile wait in the reducer FIFO (D-05).
    /// </summary>
    public Task StartEngineInBackground()
    {
        engineStart = Task.Run(RunEngineStartAsync);
        return engineStart;
    }

    /// <summary>
    /// Starts startup recovery in the background right after the hook is installed; it does not await engine readiness
    /// and always completes recovery admission (RecoveryAdmissionCompleted), even on failure.
    /// </summary>
    public Task StartRecoveryInBackground()
    {
        Func<CancellationToken, Task>? beforeProcessing = null;
#if SOMNING_TEST_HARNESS
        beforeProcessing = RecoveryGate.WaitIfArmedAsync;
#endif
        var service = new RecoveryService(Snapshot, Sidecars, SomningPaths.RecordingsDir, Host, beforeProcessing);
        recovery = Task.Run(() => service.RunAsync(lifetime.Token));
        return recovery;
    }

    private async Task RunEngineStartAsync()
    {
        try
        {
            await Engine.StartAsync(EngineDefaults.Backend, lifetime.Token).ConfigureAwait(false);
            if (Engine.Status == EngineStatus.Ready && Engine.Session is { } session)
            {
                Log.Information("engine ready backend={Backend} load_ms={LoadMs} warm_ms={WarmMs}",
                    session.Backend, session.LoadMs, session.WarmMs);
                Host.Notify("engine_ready");
            }
            else
            {
                Log.Error("engine failed to start status={Status}", Engine.Status);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error(ex, "engine failed to start");
        }
    }

    /// <summary>
    /// The supervisor strips fault-injection variables from the inherited environment, so a harness run forwards
    /// SOMNING_ENGINE_READY_DELAY_MS explicitly to the first worker (Debug only; Release workers never see it).
    /// </summary>
    private static SupervisorOptions SupervisorOptionsForHost()
    {
#if SOMNING_TEST_HARNESS
        var delay = Environment.GetEnvironmentVariable("SOMNING_ENGINE_READY_DELAY_MS");
        if (!string.IsNullOrEmpty(delay))
            return new SupervisorOptions(FirstWorkerEnvironment: new Dictionary<string, string> { ["SOMNING_ENGINE_READY_DELAY_MS"] = delay });
#endif
        return new SupervisorOptions();
    }

    private static void OnNotified(DictationNotification notification) =>
        Log.Debug("notification {Kind} {Id}", notification.Kind, notification.Id);

    private static bool IdTaken(string id) =>
        File.Exists(Path.Combine(SomningPaths.RecordingsDir, id + ".wav"))
        || File.Exists(Path.Combine(SomningPaths.RecordingsDir, id + ".json"));

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
#if SOMNING_TEST_HARNESS
        if (Pipe is not null) await Pipe.DisposeAsync().ConfigureAwait(false);
#endif
        Hook.Dispose();
        try { await recovery.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug(ex, "Recovery ended during shutdown"); }
        await Host.DisposeAsync().ConfigureAwait(false);
        await Runner.DisposeAsync().ConfigureAwait(false);
        try { await engineStart.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug(ex, "Engine start ended during shutdown"); }
        await Engine.DisposeAsync().ConfigureAwait(false);
        clipboard.Dispose();
        lifetime.Dispose();
    }
}
