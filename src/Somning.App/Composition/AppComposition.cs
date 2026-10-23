using System.Threading.Channels;
using Serilog;
using Somning.App.Dictation;
using Somning.App.Engine;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Ports;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Paste;
using Somning.Platform.Windows.Recording;

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

    public AppComposition(Func<IAudioSource> audioSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(audioSourceFactory);
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        Directory.CreateDirectory(SomningPaths.LogsDir);
        Engine = new EngineSupervisor();
        clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        var probe = new ForegroundProbe(integrity);
        var paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());
        Sidecars = new SidecarStore(SomningPaths.RecordingsDir);
        Hook = new KeyboardHook(keys.Writer);
        Host = new DictationHost(PhysicalKeyState.AreChordModifiersDown, idTaken: IdTaken);
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

    private static void OnNotified(DictationNotification notification) =>
        Log.Debug("notification {Kind} {Id}", notification.Kind, notification.Id);

    private static bool IdTaken(string id) =>
        File.Exists(Path.Combine(SomningPaths.RecordingsDir, id + ".wav"))
        || File.Exists(Path.Combine(SomningPaths.RecordingsDir, id + ".json"));

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        Hook.Dispose();
        await Host.DisposeAsync().ConfigureAwait(false);
        await Runner.DisposeAsync().ConfigureAwait(false);
        try { await engineStart.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug(ex, "Engine start ended during shutdown"); }
        await Engine.DisposeAsync().ConfigureAwait(false);
        clipboard.Dispose();
        lifetime.Dispose();
    }
}
