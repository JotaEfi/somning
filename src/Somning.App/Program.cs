using Serilog;
using Somning.App.Composition;
using Somning.App.Logging;
using Somning.App.Modes;
using Somning.Core.Engine;
using Somning.Core.Ports;
using Somning.Platform.Windows.Audio;
#if SOMNING_TEST_HARNESS
using Somning.App.TestHarness;
#endif

if (args.Length > 0 && args[0] == "--transcribe-file")
    return await RunTranscribeFileAsync(args);

Func<IAudioSource> audioSourceFactory = () => new WasapiAudioSource();
Action<AppComposition, Action>? configure = null;
#if SOMNING_TEST_HARNESS
if (HarnessOptions.IsRequested(args))
{
    HarnessOptions harness;
    try { harness = HarnessOptions.Parse(args); }
    catch (Exception ex) when (ex is ArgumentException or IOException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
    // D-19: paced file audio instead of the microphone; a fresh source per capture (sources start once).
    // The pipe's set-fixture swaps the path used by the NEXT capture.
    var fixture = new HarnessFixture(harness.Fixture);
    audioSourceFactory = () => new FileAudioSource(fixture.Path, realTime: true);
    var holdRecovery = Environment.GetEnvironmentVariable("SOMNING_TEST_HOLD_RECOVERY") == "1";
    configure = (app, requestShutdown) => app.EnableHarness(fixture, requestShutdown, holdRecovery);
}
else if (args.Length > 0)
{
    Console.Error.WriteLine("Unknown arguments.");
    return 2;
}
#else
if (args.Contains("--test-harness"))
{
    // D-20 / T-01-44: the harness is compiled only into Debug/SOMNING_TEST_HARNESS builds.
    Console.Error.WriteLine("test harness not available in this build");
    return 2;
}
if (args.Length > 0)
{
    Console.Error.WriteLine("Unknown arguments.");
    return 2;
}
#endif

return await RunDictationAsync(audioSourceFactory, configure);

// Startup order (review H9): logging → harness pipe (Debug) → loop + tick → keyboard hook → engine start in the background.
static async Task<int> RunDictationAsync(Func<IAudioSource> audioSourceFactory, Action<AppComposition, Action>? configure)
{
    LoggingSetup.Initialize();
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { shutdown.Cancel(); } catch (ObjectDisposedException) { } };
    try
    {
        await using var app = new AppComposition(audioSourceFactory);
        configure?.Invoke(app, () => { try { shutdown.Cancel(); } catch (ObjectDisposedException) { } });
        app.StartLoop();
        app.Hook.Start(); // KeyboardHook installed BEFORE the engine StartAsync (below) is even called
        Log.Information("hook installed");
        app.Host.Notify("hook_installed");
        _ = app.StartEngineInBackground(); // NOT awaited: the hook must work while the model loads (STT-04)
        try { await Task.Delay(Timeout.Infinite, shutdown.Token); }
        catch (OperationCanceledException) { }
        Log.Information("shutting down");
        return 0;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "App failed");
        return 1;
    }
    finally { await Log.CloseAndFlushAsync(); }
}

static async Task<int> RunTranscribeFileAsync(string[] args)
{
    LoggingSetup.Initialize();
    try
    {
        if (args.Length < 2 || args.Length % 2 != 0)
            throw new ArgumentException("Expected --transcribe-file <wav> [--backend cpu|vulkan] [--engine path].");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--transcribe-file" or "--backend" or "--engine") || !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown or duplicate argument.");
        }
        return await TranscribeFileCommand.RunAsync(options["--transcribe-file"],
            options.GetValueOrDefault("--backend", EngineDefaults.Backend), options.GetValueOrDefault("--engine"));
    }
    catch (Exception)
    {
        Log.Error("App command failed");
        return 1;
    }
    finally { await Log.CloseAndFlushAsync(); }
}
