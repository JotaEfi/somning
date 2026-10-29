namespace Somning.Core.Overlay;

/// <summary>Per-frame values consumed by the glass-drop renderer. All fields are plain floats (no allocation).</summary>
/// <param name="Mode">Mode being drawn (kept while fading out after a hide).</param>
/// <param name="Opacity">Whole-drop opacity, 0..1.</param>
/// <param name="Scale">Overall scale with elastic overshoot (about 0.1 at birth, up to ~1.2 while growing).</param>
/// <param name="Time">Flow clock in seconds; advances faster while speaking so the liquid reacts to the voice.</param>
/// <param name="Level">Smoothed, gain-normalized voice level, 0..1.</param>
/// <param name="Radius">Jelly radius multiplier around 1 (swells with the voice).</param>
/// <param name="Stretch">Volume-preserving squash/stretch, positive = wider than tall.</param>
/// <param name="Morph">0 = round drop, 1 = wide capsule (used by the notice state).</param>
/// <param name="TranscribeMix">0..1 weight of the calm cyan/violet palette and swirl.</param>
/// <param name="NoticeMix">0..1 weight of the amber warning palette.</param>
/// <param name="Lobe1X">Complex coefficients of the radial lobes (mode k adds a*cos(k*theta + phase)).</param>
public readonly record struct DropFrame(
    OverlayMode Mode,
    float Opacity,
    float Scale,
    float Time,
    float Level,
    float Radius,
    float Stretch,
    float Morph,
    float TranscribeMix,
    float NoticeMix,
    float Lobe1X, float Lobe1Y,
    float Lobe2X, float Lobe2Y,
    float Lobe3X, float Lobe3Y,
    float Lobe4X, float Lobe4Y)
{
    /// <summary>Neutral frame for the mode: full size, opaque, no voice, no lobes.</summary>
    public static DropFrame Static(OverlayMode mode) => new(
        mode, 1f, 1f, 0f, 0f, 1f, 0f,
        mode == OverlayMode.Notice ? 1f : 0f,
        mode == OverlayMode.Transcribing ? 1f : 0f,
        mode == OverlayMode.Notice ? 1f : 0f,
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
}
