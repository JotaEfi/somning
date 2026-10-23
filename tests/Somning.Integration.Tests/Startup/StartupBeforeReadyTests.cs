using NAudio.Wave;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Startup;

/// <summary>
/// STT-04 / D-05 / review H9 in the running App: a hotkey pressed while the engine is still loading is hooked,
/// suppressed (no space reaches Notepad), recorded, and transcribed and pasted after the engine becomes ready.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StartupBeforeReadyTests
{
    [Fact]
    public async Task Hotkey_before_ready_is_recorded_and_delivered()
    {
        Requires.Models();
        Requires.Desktop();
        var fixture = Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav");
        var expected = File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));
        TimeSpan fixtureLength;
        using (var reader = new WaveFileReader(fixture)) fixtureLength = reader.TotalTime;
        var env = new Dictionary<string, string> { ["SOMNING_ENGINE_READY_DELAY_MS"] = "20000" };

        using var app = await AppHarnessDriver.StartAsync(fixture, env, waitForEngine: false);
        await app.WaitEventAsync("hook_installed", 0, null, TimeSpan.FromSeconds(20));
        Assert.Equal("Starting", (await app.StatusAsync()).Engine);
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "Notepad could not be brought to the foreground.");
        var seq0 = await app.LastSeqAsync();

        HotkeyInjector.Chord(80);
        await Task.Delay(fixtureLength + TimeSpan.FromSeconds(0.8), TestContext.Current.CancellationToken);
        HotkeyInjector.Chord(80);
        var sinceStop = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal("Starting", (await app.StatusAsync()).Engine); // the whole dictation happened during model load
        Assert.Equal("", notepad.GetText()); // the chord was suppressed: no leaked space
        Assert.Null(await app.TryWaitEventAsync("engine_ready", 0, null, TimeSpan.Zero));

        // Lower bound of the wait for the engine: the last moment after the stop that the engine was still Starting.
        long stillStartingMs = 0;
        while (sinceStop.Elapsed < TimeSpan.FromSeconds(60) && (await app.StatusAsync()).Engine == "Starting")
        {
            stillStartingMs = sinceStop.ElapsedMilliseconds;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var outcome = await app.WaitOutcomeAsync(seq0, TimeSpan.FromSeconds(90));
        Assert.Equal("pasted", outcome.Outcome);
        Assert.True(stillStartingMs > 1000, $"The engine should still be loading well after the stop (was {stillStartingMs} ms).");
        // queue_ms is the supervisor-internal wait (request issued after ready), so the startup wait shows in stop_to_paste_ms.
        Assert.True(outcome.MetricLong("stop_to_paste_ms") >= stillStartingMs,
            $"stop_to_paste_ms {outcome.MetricLong("stop_to_paste_ms")} should cover the {stillStartingMs} ms the engine was still starting.");
        var engineReady = await app.WaitEventAsync("engine_ready", 0, null, TimeSpan.FromSeconds(1));
        Assert.True(outcome.Seq > engineReady.Seq, "The outcome must follow engine_ready.");
        var text = notepad.WaitForText(ClipboardProbe.ReadText());
        Assert.True(Wer.Compute(expected, text) <= 0.10, $"WER too high. Pasted: '{text}'");
    }
}
