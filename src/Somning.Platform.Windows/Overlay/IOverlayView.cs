using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public interface IOverlayView : IDisposable
{
    IntPtr Hwnd { get; }
    bool IsVisible { get; }
    OverlayViewModel CurrentModel { get; }

    void ShowListening(float[] waveformBars);
    void UpdateWaveform(float[] waveformBars);
    void ShowTranscribing();
    void ShowNotice(OverlayNoticeKind kind);
    void Hide();
}
