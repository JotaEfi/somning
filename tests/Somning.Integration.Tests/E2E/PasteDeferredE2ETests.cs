using System.Globalization;
using Somning.Core.Dictation;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Clipboard;
using Xunit;

namespace Somning.Integration.Tests.E2E;

[Trait("Category", "E2E")]
public sealed class PasteDeferredE2ETests
{
    private const string FixtureName = "tracer/hoje.wav";

    [Fact]
    public async Task Foreground_change_after_capture_defers_paste_and_keeps_transcript_on_clipboard()
    {
        var fixture = E2EFixtureSupport.Fixture(FixtureName);
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        using var other = NotepadDriver.Start();
        Assert.True(ForegroundHelper.EnsureForeground(e2e.Notepad.Window, e2e.Notepad.Edit));
        await e2e.App.ArmDeliveryBarrierAsync();
        var cursor = await e2e.App.LastSeqAsync();
        await BeginAndStopAsync(e2e, fixture);
        var target = await e2e.App.WaitEventAsync("target_captured", cursor, null, TimeSpan.FromSeconds(20));

        Assert.True(ForegroundHelper.EnsureForeground(other.Window, other.Edit));
        await e2e.App.ReleaseDeliveryBarrierAsync();
        var outcome = await e2e.App.WaitEventAsync("outcome", target.Seq - 1, target.Id, TimeSpan.FromMinutes(2));

        Assert.Equal("paste_deferred", outcome.Outcome);
        Assert.Equal("foreground_changed", outcome.Reason);
        Assert.Empty(e2e.Notepad.GetText());
        Assert.Empty(other.GetText());
        Assert.Equal(E2EFixtureSupport.Expected(FixtureName), ClipboardProbe.ReadText());
    }

    [Fact]
    public async Task Focus_change_inside_same_window_after_capture_defers_paste()
    {
        var fixture = E2EFixtureSupport.Fixture(FixtureName);
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        using var targetWindow = TwoEditWindow.Start();
        Assert.True(ForegroundHelper.EnsureForeground(targetWindow.Window, targetWindow.EditHandle(0)));
        await e2e.App.ArmDeliveryBarrierAsync();
        var cursor = await e2e.App.LastSeqAsync();
        await BeginAndStopAsync(e2e, fixture);
        var target = await e2e.App.WaitEventAsync("target_captured", cursor, null, TimeSpan.FromSeconds(20));

        targetWindow.FocusEdit(1);
        await e2e.App.ReleaseDeliveryBarrierAsync();
        var outcome = await e2e.App.WaitEventAsync("outcome", target.Seq - 1, target.Id, TimeSpan.FromMinutes(2));

        Assert.Equal("paste_deferred", outcome.Outcome);
        Assert.Equal("focus_changed", outcome.Reason);
        Assert.Empty(targetWindow.GetText(0));
        Assert.Empty(targetWindow.GetText(1));
        Assert.Equal(E2EFixtureSupport.Expected(FixtureName), ClipboardProbe.ReadText());
    }

    [Fact]
    public async Task Successful_dictation_logs_one_complete_private_metrics_record()
    {
        var fixture = E2EFixtureSupport.Fixture(FixtureName);
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        var expected = E2EFixtureSupport.Expected(FixtureName);
        var outcome = await e2e.DictateAsync(fixture, hold: false);
        Assert.Equal("pasted", outcome.Outcome);
        e2e.AssertPasted(outcome, expected);

        var marker = "dictation " + outcome.Id;
        await e2e.App.WaitForLogAsync(marker, TimeSpan.FromSeconds(10));
        var content = e2e.App.ReadLog();
        var matching = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(marker, StringComparison.Ordinal)).ToArray();
        var line = Assert.Single(matching);
        foreach (var field in DictationMetrics.FieldNames)
            Assert.Contains(field, line, StringComparison.Ordinal);

        foreach (var field in new[] { "capture_ms", "mic_open_ms", "vad_ms", "queue_ms", "infer_ms", "load_ms", "warm_ms", "paste_ms", "stop_to_paste_ms" })
        {
            var value = ReadIntegerField(line, field);
            Assert.True(value >= 0, $"{field} was negative: {value}");
        }
        Assert.Equal(expected.Trim().Length, ReadIntegerField(line, "chars"));
        Assert.Equal("pasted", ReadStringField(line, "outcome"));

        foreach (var privateValue in new[] { expected, "sk-live-123", "tok-9", "Bearer x", "Authorization", "ApiKey", "AccessToken" })
            Assert.DoesNotContain(privateValue, line, StringComparison.OrdinalIgnoreCase);
    }

    // This desktop session does not provision a noninteractive low-IL App/high-IL target pair. The mandatory
    // target_elevated and integrity_unknown cases are exercised by PasteGateTests; this is not empirical UIPI proof.
    [Fact(Skip = "No noninteractive low-integrity App/high-integrity target pair is provisioned in this desktop session; PasteGateTests covers policy only.")]
    public void Elevated_target_defers_paste_when_a_real_integrity_pair_is_available() =>
        throw new NotSupportedException("A real integrity pair is required for this optional desktop acceptance.");

    private static async Task BeginAndStopAsync(E2EFixtureSupport e2e, string fixture)
    {
        await e2e.App.SetFixtureAsync(fixture);
        HotkeyInjector.Chord(70);
        await Task.Delay(E2EFixtureSupport.Duration(fixture) + TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);
        HotkeyInjector.Chord(70);
    }

    private static long ReadIntegerField(string line, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line,
            "\\\"" + System.Text.RegularExpressions.Regex.Escape(field) + "\\\"\\s*:\\s*(-?[0-9]+)");
        Assert.True(match.Success, $"Could not find numeric field {field} in metrics log line: {line}");
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadStringField(string line, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line,
            "\\\"" + System.Text.RegularExpressions.Regex.Escape(field) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"");
        Assert.True(match.Success, $"Could not find string field {field} in metrics log line: {line}");
        return match.Groups[1].Value;
    }
}
