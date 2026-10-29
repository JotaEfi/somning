using System.Diagnostics;
using System.Threading.Channels;
using Serilog;
using Somning.App.Dictation;
using Somning.App.Engine;
using Somning.App.Recovery;
using Somning.Core.Cloud;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Model;
using Somning.Core.Output;
using Somning.Core.Overlay;
using Somning.Core.Paths;
using Somning.Core.Ports;
using Somning.Core.Settings;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Autostart;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Overlay;
using Somning.Platform.Windows.Paste;
using Somning.Platform.Windows.Power;
using Somning.Platform.Windows.Recording;
using Somning.Platform.Windows.Settings;
using Somning.Platform.Windows.Tray;
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
    private readonly TargetPaster paster;
    private readonly ForegroundProbe probe;
    private Task engineStart = Task.CompletedTask;
    private Task recovery = Task.CompletedTask;
    private Action? shutdownCallback;
    private static readonly TimeSpan DefaultHookWatchdogInterval = TimeSpan.FromSeconds(30);
    private readonly HookLivenessWatchdog hookWatchdog;
    private CancellationTokenSource? watchdogCts;
    private Task watchdogTask = Task.CompletedTask;

    public AppComposition(Func<IAudioSource> audioSourceFactory, ISettingsStore? settingsStore = null, DynamicAudioSourceFactory? dynamicAudio = null, IAutostartManager? autostartManager = null, PowerSessionListener? powerListener = null)
    {
        ArgumentNullException.ThrowIfNull(audioSourceFactory);
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        Directory.CreateDirectory(SomningPaths.LogsDir);
        DynamicAudioFactory = dynamicAudio;
        AutostartManager = autostartManager ?? new RegistryAutostartManager();
        PowerListener = powerListener ?? TryCreatePowerListener();
        SettingsStore = settingsStore ?? new JsonSettingsStore();
        Settings = SettingsStore.LoadAsync().GetAwaiter().GetResult();
        AutoPasteEnabled = Settings.AutoPaste;
        SoundPlayer.Enabled = Settings.PlaySoundCues;
        if (DynamicAudioFactory is not null && !string.IsNullOrEmpty(Settings.MicrophoneDeviceId))
        {
            DynamicAudioFactory.CurrentDeviceId = Settings.MicrophoneDeviceId;
        }

        Engine = new EngineSupervisor(options: SupervisorOptionsForHost());
        CloudClient = new CloudTranscriptionClient();
        Transcriber = new RouterTranscriber(Engine, CloudClient, () => Settings, new DpapiSettingsProtector());
        clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        probe = new ForegroundProbe(integrity);
        paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());
        Sidecars = new SidecarStore(SomningPaths.RecordingsDir);
        Hook = new KeyboardHook(keys.Writer);
        // Pre-hook snapshot (H9): paths/IDs only; repair, model wait and inference all happen later in RecoveryService.
        Snapshot = RecoverySnapshot.Take(SomningPaths.RecordingsDir);
        // Live captures run normally while admission is pending; only transcription/copy dispatch waits (D-05, D-11).
        Host = new DictationHost(PhysicalKeyState.AreChordModifiersDown, idTaken: IdTaken,
            initialState: DictationState.Initial with { RecoveryAdmissionPending = true });
        Hook.IsRecordingActive = () => Host.State.Capture is not null;
        hookWatchdog = new HookLivenessWatchdog(new HookLivenessProbe(Hook), new HookLivenessPolicy(),
            () => ReinstallHookAfterWake("Watchdog: hook probe missed"), () => Host.State.Capture is not null,
            LogWatchdog, interval: ResolveWatchdogInterval());
        Runner = new EffectRunner(e => Host.Post(e), Sidecars, SomningPaths.RecordingsDir, audioSourceFactory, probe, Transcriber, paster, History, () => Overlay, AudioLevelTracker, () => AutoPasteEnabled, SoundPlayer);
        Engine.RequestWritten += Runner.OnRequestWritten;
        Engine.StatusChanged += status => Host.Post(new EngineStatusChanged(status));
        Host.Notified += OnNotified;

        // DIST-03: a sleep/resume or lock/unlock cycle can silently drop the low-level hook.
        if (PowerListener is not null)
        {
            PowerListener.Suspended += OnSystemSuspended;
            PowerListener.Resumed += OnSystemResumed;
            PowerListener.SessionLocked += OnSessionLocked;
            PowerListener.SessionUnlocked += OnSessionUnlocked;
        }
    }

    public EngineSupervisor Engine { get; }
    public HookLivenessWatchdog HookWatchdog => hookWatchdog;
    public CloudTranscriptionClient CloudClient { get; }
    public RouterTranscriber Transcriber { get; }
    public KeyboardHook Hook { get; }
    public DictationHost Host { get; }
    public EffectRunner Runner { get; }
    public SidecarStore Sidecars { get; }
    public RecoverySnapshot Snapshot { get; }
    public ITranscriptHistory History { get; } = new TranscriptHistory();
    public AudioLevelTracker AudioLevelTracker { get; } = new();
    public SoundCuePlayer SoundPlayer { get; } = new();
    public bool AutoPasteEnabled { get; set; } = true;
    public ISettingsStore SettingsStore { get; }
    public IAutostartManager AutostartManager { get; }
    public PowerSessionListener? PowerListener { get; }
    public UserSettings Settings { get; private set; }
    public DynamicAudioSourceFactory? DynamicAudioFactory { get; }
    public IOverlayView? Overlay { get; private set; }
    public ISettingsWindow? SettingsWindow { get; private set; }
    public TrayIconManager? Tray { get; private set; }

    public void EnableOverlay(IOverlayView? customOverlay = null)
    {
        Overlay = customOverlay ?? new OverlayWindowManager();
    }

    public void EnableSettings(ISettingsWindow? customWindow = null)
    {
        SettingsWindow = customWindow ?? new SettingsWindowManager();
    }

    public void OpenSettings()
    {
        if (SettingsWindow is null) EnableSettings();
        SettingsWindow?.Show(Settings, OnSettingsSaved);
    }

    public void OnSettingsSaved(UserSettings updated) => ApplySettings(updated);

    /// <summary>
    /// Persists the settings and applies them to the live components. The autostart registry entry is only
    /// rewritten when its state diverges from the saved setting (no needless HKCU writes on every save).
    /// </summary>
    public void ApplySettings(UserSettings updated)
    {
        Settings = updated;
        _ = SettingsStore.SaveAsync(updated);
        AutoPasteEnabled = updated.AutoPaste;
        SoundPlayer.Enabled = updated.PlaySoundCues;
        if (DynamicAudioFactory is not null)
        {
            DynamicAudioFactory.CurrentDeviceId = updated.MicrophoneDeviceId;
        }
        // DIST-02: keep HKCU\...\Run in sync with the "Iniciar com o Windows" toggle.
        if (AutostartManager.IsEnabled() != updated.StartWithWindows)
        {
            AutostartManager.SetEnabled(updated.StartWithWindows);
        }
        Log.Information("Settings updated. Mic={Mic} AutoPaste={AutoPaste} SoundCues={SoundCues} StartWithWindows={StartWithWindows} Provider={Provider}",
            updated.MicrophoneDeviceId, updated.AutoPaste, updated.PlaySoundCues, updated.StartWithWindows, updated.CloudProvider);
    }

    public void EnableTray(Action requestShutdown)
    {
        shutdownCallback = requestShutdown;
        Tray = new TrayIconManager();
        Tray.CanRepaste = () => History.HasTranscript;
        Tray.OpenSettingsRequested += () => OpenSettings();
        Tray.RepasteLastRequested += () => _ = RepasteLastAsync();
        Tray.OpenLogsRequested += () => OpenLogsDirectory();
        Tray.ExitRequested += () => shutdownCallback?.Invoke();
    }

    public async Task<bool> RepasteLastAsync()
    {
        if (!History.HasTranscript || string.IsNullOrEmpty(History.LastTranscript))
            return false;

        var text = History.LastTranscript;
        var target = probe.Capture();
        var result = await paster.DeliverAsync(text, target, CancellationToken.None).ConfigureAwait(false);
        Log.Information("Repasted last transcript outcome={Outcome}", result.Outcome);
        return result.Outcome is "pasted" or "copied" or "paste_deferred";
    }

    public static void OpenLogsDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SomningPaths.LogsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open logs directory");
        }
    }

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
        var status = new HarnessStatusSource(() => Engine.Status.ToString(), () => Host.State,
            () => Hook.InstallCount, () => Hook.ProbeSeenCount, () => Hook.ReinstallCompletedCount,
            () => hookWatchdog.ReinstallRequests);
        var controls = new HarnessControls(InjectTapAsync, enabled => AutoPasteEnabled = enabled);
        Pipe = new HarnessPipeServer(Journal, status, fixture, deliveryBarrier, RecoveryGate, requestShutdown, controls);
        Pipe.Start();
    }
#endif

    /// <summary>DIST-03: starts the periodic hook liveness probe. Call right after Hook.Start().</summary>
    public void StartHookWatchdog()
    {
        if (watchdogCts is not null) return;
        watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = watchdogCts.Token;
        watchdogTask = Task.Run(() => hookWatchdog.RunAsync(token), CancellationToken.None);
    }

    private static void LogWatchdog(string message)
    {
        if (message.Contains("limit reached", StringComparison.Ordinal)) Log.Error("{Message}", message);
        else Log.Warning("{Message}", message);
    }

    private static TimeSpan ResolveWatchdogInterval()
    {
#if SOMNING_TEST_HARNESS
        // Override exists only in harness-enabled builds (the symbol is compiled out of the default Release package).
        var raw = Environment.GetEnvironmentVariable("SOMNING_HOOK_WATCHDOG_MS");
        if (int.TryParse(raw, out var ms) && ms >= 50) return TimeSpan.FromMilliseconds(ms);
#endif
        return DefaultHookWatchdogInterval;
    }

#if SOMNING_TEST_HARNESS
    /// <summary>
    /// Headless tap: emits the same TriggerDown/TriggerUp pair the hook produces for a quick tap straight into the
    /// key channel, so the single-writer loop sees a normal toggle without any OS input.
    /// </summary>
    public async Task InjectTapAsync()
    {
        keys.Writer.TryWrite(new KeyEvent(KeyEventKind.TriggerDown, Environment.TickCount64, true));
        await Task.Delay(40).ConfigureAwait(false);
        keys.Writer.TryWrite(new KeyEvent(KeyEventKind.TriggerUp, Environment.TickCount64, true));
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

    public ModelDownloadManager ModelDownloader { get; } = new();

    public async Task EnsureModelAvailableAsync(CancellationToken cancellationToken = default)
    {
        var modelPath = Path.Combine(SomningPaths.ModelsDir, EngineDefaults.ModelFile);
        if (File.Exists(modelPath)) return;

        var downloadUrl = Environment.GetEnvironmentVariable("SOMNING_MODEL_DOWNLOAD_URL");
        var sha256 = Environment.GetEnvironmentVariable("SOMNING_MODEL_SHA256");

        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(sha256)) return;

        try
        {
            Overlay?.ShowNotice(OverlayNoticeKind.ModelMissing);
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                Log.Debug("Model download progress: {Percent:F1}%", p.Percent);
            });

            await ModelDownloader.DownloadAsync(new Uri(downloadUrl), modelPath, sha256, progress, cancellationToken)
                .ConfigureAwait(false);
            Overlay?.Hide();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Automatic model download failed");
        }
    }

    private async Task RunEngineStartAsync()
    {
        try
        {
            await EnsureModelAvailableAsync(lifetime.Token).ConfigureAwait(false);
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
    /// SOMNING_ENGINE_READY_DELAY_MS / SOMNING_ENGINE_FAULT explicitly to the first worker (Debug only; Release workers never see it).
    /// </summary>
    private static SupervisorOptions SupervisorOptionsForHost()
    {
#if SOMNING_TEST_HARNESS
        var firstWorkerEnvironment = new Dictionary<string, string>();
        var delay = Environment.GetEnvironmentVariable("SOMNING_ENGINE_READY_DELAY_MS");
        var fault = Environment.GetEnvironmentVariable("SOMNING_ENGINE_FAULT");
        if (!string.IsNullOrEmpty(delay)) firstWorkerEnvironment["SOMNING_ENGINE_READY_DELAY_MS"] = delay;
        if (!string.IsNullOrEmpty(fault)) firstWorkerEnvironment["SOMNING_ENGINE_FAULT"] = fault;
        if (firstWorkerEnvironment.Count > 0)
            return new SupervisorOptions(FirstWorkerEnvironment: firstWorkerEnvironment);
#endif
        return new SupervisorOptions();
    }

    private static void OnNotified(DictationNotification notification) =>
        Log.Debug("notification {Kind} {Id}", notification.Kind, notification.Id);

    private static PowerSessionListener? TryCreatePowerListener()
    {
        try { return new PowerSessionListener(); }
        catch (Exception ex)
        {
            // Power/session notifications are an enhancement: never block startup on them.
            Log.Warning(ex, "Power/session listener unavailable");
            return null;
        }
    }

    private static void OnSystemSuspended() =>
        Log.Information("System suspending; interrupted captures will be recovered");

    private void OnSystemResumed() => ReinstallHookAfterWake("System resumed from sleep");

    private static void OnSessionLocked() => Log.Information("Session locked");

    private void OnSessionUnlocked() => ReinstallHookAfterWake("Session unlocked");

    /// <summary>
    /// DIST-03: Windows may drop WH_KEYBOARD_LL across power transitions; reinstall it on the hook thread.
    /// Events can arrive before Program calls Hook.Start(), so a failed reinstall is logged, not thrown.
    /// </summary>
    private void ReinstallHookAfterWake(string reason)
    {
        Log.Information("{Reason}; reinstalling keyboard hook", reason);
        try { Hook.Reinstall(); }
        catch (Exception ex) { Log.Warning(ex, "Keyboard hook reinstall failed after {Reason}", reason); }
    }

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
        SettingsWindow?.Dispose();
        Overlay?.Dispose();
        Tray?.Dispose();
        PowerListener?.Dispose();
        try { watchdogCts?.Cancel(); await watchdogTask.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug(ex, "Hook watchdog ended during shutdown"); }
        watchdogCts?.Dispose();
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
