using System.Diagnostics;
using Somning.Core.Text;
using Somning.Integration.Tests.E2E;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Soak;

/// <summary>
/// DIST-03 soak: N consecutive dictations through the real App, engine and FileAudioSource, driven by inject-tap in
/// clipboard-only mode (nothing is injected into any window), then ONE dictation through the real WH_KEYBOARD_LL hook.
/// Category=Soak only: default Desktop/E2E filters never run it. Failed iterations are counted, never repeated.
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTests
{
    private const string FixtureName = "tracer/hoje";
    private const int DefaultIterations = 50;
    private const int BaselineIteration = 3;
    private const string Sentinel = "SOMNING-SOAK-SENTINEL-NAO-COLAR";
    private const double TranscriptMaxWer = 0.10;
    private static readonly TimeSpan OutcomeTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Fifty_dictations_then_real_hotkey_dictation_stay_healthy()
    {
        Requires.Models();
        Requires.Desktop();
        var iterations = ReadIterations();
        var fixture = E2EFixtureSupport.Fixture(FixtureName + ".wav");
        var expected = E2EFixtureSupport.Expected(FixtureName);
        var perIteration = E2EFixtureSupport.Duration(fixture) + TimeSpan.FromMilliseconds(800);
        var token = TestContext.Current.CancellationToken;

        using var app = await AppHarnessDriver.StartAsync(fixture);
        await app.WaitEventAsync("recovery_complete", 0, null, TimeSpan.FromSeconds(30));
        await app.SetAutoPasteAsync(false); // clipboard-only: no input reaches any window during the loop
        var statusStart = await app.StatusAsync();
        Assert.True(statusStart.Hook, "hook must be installed before the soak");

        var clock = Stopwatch.StartNew();
        var failures = 0;
        var completed = 0;
        (long Handles, long Bytes)? baseline = null;
        (long Handles, long Bytes) last = default;
        var failureNotes = new List<string>();
        for (var i = 1; i <= iterations; i++)
        {
            var ok = await RunIterationAsync(app, perIteration, expected, failureNotes, i, token);
            if (!ok) failures++;
            completed++;
            last = Sample(app.Process.Id);
            if (i == Math.Min(BaselineIteration, iterations)) baseline = last;
        }
        clock.Stop();

        var statusAfterLoop = await app.StatusAsync();
        Assert.True(statusAfterLoop.Hook, "hook must still be installed after the loop");
        var finalOk = await RealHotkeyDictationAsync(app, fixture, expected, token);
        var statusEnd = await app.StatusAsync();
        var baseSample = baseline ?? last;

        var report = SoakReport.Evaluate(new SoakMeasurements(iterations, completed, failures, clock.Elapsed.TotalSeconds,
            baseSample.Handles, last.Handles, baseSample.Bytes, last.Bytes,
            statusStart.HookInstallCount, statusEnd.HookInstallCount, statusEnd.WatchdogReinstalls, finalOk));
        WriteReport(report);
        Assert.True(report.Passed, "Soak failed: " + string.Join("; ", report.Reasons.Concat(failureNotes)));
    }

    private static int ReadIterations() =>
        int.TryParse(Environment.GetEnvironmentVariable("SOMNING_SOAK_N"), out var n) && n >= 1 ? n : DefaultIterations;

    private static async Task<bool> RunIterationAsync(AppHarnessDriver app, TimeSpan duration, string expected, List<string> notes, int index, CancellationToken token)
    {
        try
        {
            E2EFixtureSupport.SetClipboard(Sentinel); // a stale clipboard from the previous iteration must not pass
            var afterSeq = await app.LastSeqAsync();
            await app.InjectTapAsync();
            await Task.Delay(duration, token);
            await app.InjectTapAsync();
            var target = await app.WaitEventAsync("target_captured", afterSeq, null, TimeSpan.FromSeconds(20));
            var outcome = await app.WaitEventAsync("outcome", target.Seq - 1, target.Id, OutcomeTimeout);
            // Clipboard-only delivery is reported as paste_deferred with reason clipboard_set (DictationController.DeliveryStage).
            if (outcome.Outcome != "paste_deferred" || outcome.Reason != "clipboard_set")
            {
                notes.Add($"iteration {index}: outcome {outcome.Outcome}/{outcome.Reason}");
                return false;
            }
            var text = ClipboardProbe.ReadText();
            if (text == Sentinel || string.IsNullOrWhiteSpace(text))
            {
                notes.Add($"iteration {index}: clipboard was not replaced by the transcript");
                return false;
            }
            if (text != text.Trim() || Wer.Compute(expected, text) > TranscriptMaxWer)
            {
                notes.Add($"iteration {index}: unexpected transcript '{text}' (WER limit {TranscriptMaxWer})");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            notes.Add($"iteration {index}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return false;
        }
    }

    private static async Task<bool> RealHotkeyDictationAsync(AppHarnessDriver app, string fixture, string expected, CancellationToken token)
    {
        using var notepad = NotepadDriver.Start();
        try
        {
            Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "test-owned Notepad could not be brought to foreground");
            notepad.SetText(string.Empty);
            await app.SetAutoPasteAsync(true); // same process: hook and its install count are untouched
            var afterSeq = await app.LastSeqAsync();
            HotkeyInjector.Chord(70);
            await Task.Delay(E2EFixtureSupport.Duration(fixture) + TimeSpan.FromMilliseconds(800), token);
            Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "test-owned Notepad lost foreground before stop");
            HotkeyInjector.Chord(70);
            var target = await app.WaitEventAsync("target_captured", afterSeq, null, TimeSpan.FromSeconds(20));
            var outcome = await app.WaitEventAsync("outcome", target.Seq - 1, target.Id, OutcomeTimeout);
            if (outcome.Outcome != "pasted") return false;
            var clipboardText = ClipboardProbe.ReadText();
            var text = notepad.WaitForText(clipboardText, TimeSpan.FromSeconds(3));
            // Same invariants as E2EFixtureSupport.AssertPasted: what Notepad shows equals the clipboard, with no extra
            // leading/trailing whitespace (a leaked hotkey Space), no line breaks, and an accurate transcript.
            return !string.IsNullOrWhiteSpace(text) && text == clipboardText && text == text.Trim()
                && !text.Contains('\n') && !text.Contains('\r') && Wer.Compute(expected, text) <= TranscriptMaxWer;
        }
        catch (TimeoutException) { return false; }
        finally { HotkeyInjector.ReleaseAll(); }
    }

    /// <summary>Handles and private bytes of the App process plus its Somning.Engine children.</summary>
    private static (long Handles, long Bytes) Sample(int appPid)
    {
        var pids = new List<int> { appPid };
        pids.AddRange(E2EFixtureSupport.EnginePids(appPid).Select(int.Parse));
        long handles = 0, bytes = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Refresh();
                handles += process.HandleCount;
                bytes += process.PrivateMemorySize64;
            }
            catch (ArgumentException) { }
        }
        return (handles, bytes);
    }

    private static void WriteReport(SoakReport report)
    {
        var path = Path.Combine(RepoPaths.Root, ".planning", "phases", "04-packaging-autostart-hardening", "soak-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToJson());
    }
}
