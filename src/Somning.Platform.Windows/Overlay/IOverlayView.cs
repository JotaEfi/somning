using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public interface IOverlayView : IDisposable
{
    IntPtr Hwnd { get; }
    bool IsVisible { get; }
    OverlayViewModel CurrentModel { get; }

    void ShowListening(float[] waveformBars);
    void UpdateWaveform(float[] waveformBars);
    /// <summary>Voice brightness 0..1 for tone-reactive reflections; views that do not use it can ignore it.</summary>
    void UpdateVoiceTone(float brightness) { }
    /// <summary>Selects the drop style (an id from <see cref="DropStyles"/>); views that draw a single style can ignore it.</summary>
    void SetDropStyle(string styleId) { }
    void ShowTranscribing();
    void ShowNotice(OverlayNoticeKind kind);
    void Hide();
}
