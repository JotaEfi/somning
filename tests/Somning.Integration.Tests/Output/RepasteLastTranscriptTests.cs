using Somning.App.Composition;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Output;

public sealed class RepasteLastTranscriptTests
{
    [Fact]
    public async Task RepasteLastAsync_WithoutTranscript_ReturnsFalse()
    {
        await using var app = new AppComposition(() => new WasapiAudioSource());
        Assert.False(app.History.HasTranscript);

        var result = await app.RepasteLastAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task RepasteLastAsync_WithRecordedTranscript_PopulatesClipboardAndReturnsTrue()
    {
        await using var app = new AppComposition(() => new WasapiAudioSource());

        const string expectedText = "Transcrição de teste para repaste no tray";
        app.History.Record(expectedText);
        Assert.True(app.History.HasTranscript);
        Assert.Equal(expectedText, app.History.LastTranscript);

        var result = await app.RepasteLastAsync();
        Assert.True(result);

        // Verify clipboard has the expected text
        var clipboardText = ClipboardProbe.ReadText();
        Assert.Equal(expectedText, clipboardText);
    }
}
