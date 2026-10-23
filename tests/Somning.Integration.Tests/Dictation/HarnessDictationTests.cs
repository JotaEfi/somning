using System.Text.RegularExpressions;
using NAudio.Wave;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Dictation;

/// <summary>
/// Tracer E2E (plan 01-10): the real App in harness mode — real hook-driven tap-tap → single-writer loop →
/// FileAudioSource → spool → engine → clipboard → Ctrl+V into Notepad.
/// </summary>
[Trait("Category", "E2E")]
public sealed partial class HarnessDictationTests
{
    [GeneratedRegex(@"\[INF\] dictation \d{8}-\d{6}-\d{3}")]
    private static partial Regex DictationLine();

    [Fact]
    public async Task TapTap_pastes_fixture_into_notepad()
    {
        Requires.Models();
        Requires.Desktop();
        var fixture = Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav");
        var expected = File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));
        TimeSpan fixtureLength;
        using (var reader = new WaveFileReader(fixture)) fixtureLength = reader.TotalTime;

        using var app = await AppHarnessDriver.StartAsync(fixture, waitForEngine: true);
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
            "Notepad edit control did not become the active foreground focus.");

        HotkeyInjector.Chord(80); // tap → start (toggle)
        await Task.Delay(fixtureLength + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
            "Notepad edit control did not retain focus before the stop hotkey.");
        HotkeyInjector.Chord(80); // tap → stop

        var text = "";
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < until)
        {
            text = notepad.GetText();
            if (text.Length > 0 && DictationLine().IsMatch(app.ReadLog())) break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var log = app.ReadLog();
        Assert.False(string.IsNullOrEmpty(text), "Nothing was pasted into Notepad. Log:\n" + log);
        Assert.True(Wer.Compute(expected, text) <= 0.10, $"WER too high. Pasted: '{text}'");
        Assert.False(char.IsWhiteSpace(text[0]), $"Pasted text has a leading space: '{text}'");
        Assert.Equal(text, ClipboardProbe.ReadText());
        Assert.Single(DictationLine().Matches(log));
        Assert.DoesNotContain(text, log, StringComparison.Ordinal);
    }
}
