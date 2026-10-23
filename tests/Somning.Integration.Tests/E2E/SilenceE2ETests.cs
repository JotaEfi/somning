using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.E2E;

[Trait("Category", "E2E")]
public sealed class SilenceE2ETests
{
    [Theory]
    [InlineData("nonspeech/silence.wav", 3000)]
    [InlineData("nonspeech/noise.wav", 3000)]
    [InlineData("nonspeech/tone.wav", 2000)]
    public async Task Non_speech_preserves_user_clipboard_and_notepad(string fixtureName, int captureMs)
    {
        var fixture = E2EFixtureSupport.Fixture(fixtureName);
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        E2EFixtureSupport.SetClipboard("SENTINELA");
        e2e.Notepad.SetText(string.Empty);
        var sequence = ClipboardProbe.SequenceNumber();
        var cursor = await e2e.App.LastSeqAsync();
        var outcome = await e2e.StartAndStopAsync(fixture, captureMs, cursor);
        Assert.Equal("empty", outcome.Outcome);
        Assert.Equal(string.Empty, e2e.Notepad.GetText());
        Assert.Equal("SENTINELA", ClipboardProbe.ReadText());
        Assert.Equal(sequence, ClipboardProbe.SequenceNumber());
    }

    [Fact]
    public async Task Sub_400ms_tap_preserves_clipboard()
    {
        var fixture = E2EFixtureSupport.Fixture("nonspeech/tap.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        E2EFixtureSupport.SetClipboard("SENTINELA");
        var sequence = ClipboardProbe.SequenceNumber();
        var cursor = await e2e.App.LastSeqAsync();
        var outcome = await e2e.StartAndStopAsync(fixture, 200, cursor);
        Assert.Equal("empty", outcome.Outcome);
        Assert.InRange(outcome.MetricLong("capture_ms"), 0, 399);
        Assert.Empty(e2e.Notepad.GetText());
        Assert.Equal("SENTINELA", ClipboardProbe.ReadText());
        Assert.Equal(sequence, ClipboardProbe.SequenceNumber());
    }

    [Fact]
    public async Task Blocklisted_obrigado_preserves_clipboard()
    {
        var fixture = E2EFixtureSupport.Fixture("speech/obrigado.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        E2EFixtureSupport.SetClipboard("SENTINELA");
        var sequence = ClipboardProbe.SequenceNumber();
        var cursor = await e2e.App.LastSeqAsync();
        var outcome = await e2e.StartAndStopAsync(fixture, (int)E2EFixtureSupport.Duration(fixture).TotalMilliseconds + 300, cursor);
        Assert.Equal("empty", outcome.Outcome);
        Assert.Equal(string.Empty, e2e.Notepad.GetText());
        Assert.Equal("SENTINELA", ClipboardProbe.ReadText());
        Assert.Equal(sequence, ClipboardProbe.SequenceNumber());
    }
}
