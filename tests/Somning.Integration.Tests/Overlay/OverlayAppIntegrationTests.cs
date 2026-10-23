using Somning.App.Composition;
using Somning.Core.Overlay;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

public sealed class OverlayAppIntegrationTests
{
    private sealed class TestOverlayView : IOverlayView
    {
        public IntPtr Hwnd => (IntPtr)12345;
        public bool IsVisible => CurrentModel.Mode != OverlayMode.Hidden;
        public OverlayViewModel CurrentModel { get; private set; } = OverlayViewModel.CreateHidden();
        public int WaveformUpdateCount { get; private set; }

        public void ShowListening(float[] waveformBars) =>
            CurrentModel = OverlayViewModel.CreateListening(waveformBars);

        public void UpdateWaveform(float[] waveformBars)
        {
            WaveformUpdateCount++;
            CurrentModel = OverlayViewModel.CreateListening(waveformBars);
        }

        public void ShowTranscribing() =>
            CurrentModel = OverlayViewModel.CreateTranscribing();

        public void ShowNotice(OverlayNoticeKind kind) =>
            CurrentModel = OverlayViewModel.CreateNotice(kind);

        public void Hide() =>
            CurrentModel = OverlayViewModel.CreateHidden();

        public void Dispose() { }
    }

    [Fact]
    public async Task AppComposition_WithOverlay_TracksListeningAndTranscribingStates()
    {
        var testOverlay = new TestOverlayView();
        await using var app = new AppComposition(() => new WasapiAudioSource());
        app.EnableOverlay(testOverlay);

        Assert.NotNull(app.Overlay);
        Assert.False(app.Overlay.IsVisible);

        // Verify initial state
        Assert.Equal(OverlayMode.Hidden, testOverlay.CurrentModel.Mode);

        // 1. Simulate start capture
        testOverlay.ShowListening(app.AudioLevelTracker.GenerateWaveformBars(5));
        Assert.True(testOverlay.IsVisible);
        Assert.Equal(OverlayMode.Listening, testOverlay.CurrentModel.Mode);
        Assert.Equal("Ouvindo...", testOverlay.CurrentModel.Text);

        // 2. Simulate audio level updates (AUD-03)
        var pcm = new byte[3200]; // 100ms of audio
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short sample = (short)(Math.Sin(i * 0.1) * 16000);
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)((sample >> 8) & 0xFF);
        }

        app.AudioLevelTracker.Update(pcm);
        Assert.True(app.AudioLevelTracker.Level > 0.1f);
        var bars = app.AudioLevelTracker.GenerateWaveformBars(5);
        testOverlay.UpdateWaveform(bars);

        Assert.Equal(1, testOverlay.WaveformUpdateCount);
        Assert.Equal(5, testOverlay.CurrentModel.Bars.Length);
        Assert.True(testOverlay.CurrentModel.Bars[2] > 0.15f); // Center bar amplified

        // 3. Simulate stop capture / transcribing (OVL-03)
        testOverlay.ShowTranscribing();
        Assert.Equal(OverlayMode.Transcribing, testOverlay.CurrentModel.Mode);
        Assert.Equal("Transcrevendo...", testOverlay.CurrentModel.Text);

        // 4. Simulate delivery / hide
        testOverlay.Hide();
        Assert.False(testOverlay.IsVisible);
        Assert.Equal(OverlayMode.Hidden, testOverlay.CurrentModel.Mode);
    }
}
