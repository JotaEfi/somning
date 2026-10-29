namespace Somning.Core.Overlay;

/// <summary>Per-frame values consumed by the orb renderer. Plain value type, no allocation.</summary>
/// <param name="Mode">Mode being drawn (kept while dissolving after a hide).</param>
/// <param name="Opacity">Whole-orb opacity, 0..1.</param>
/// <param name="Scale">Body scale around the center; springs past 1 on entrance (elastic overshoot).</param>
/// <param name="Level">Smoothed voice level, 0..1 (spring driven and clamped).</param>
/// <param name="Flow">Accumulated phase of the liquid color bands; advances faster with voice.</param>
/// <param name="Swirl">Accumulated phase of the transcribing swirl.</param>
/// <param name="Breath">Slow breathing value in [-1, 1]; strongest in silence.</param>
/// <param name="Soften">Extra edge blur, 0..1; high while appearing or dissolving.</param>
/// <param name="TranscribeMix">Palette weight of the calm cyan/violet transcribing look, 0..1.</param>
/// <param name="NoticeMix">Palette weight of the red/amber error look, 0..1.</param>
public readonly record struct OrbFrame(
    OverlayMode Mode, float Opacity, float Scale, float Level, float Flow, float Swirl, float Breath,
    float Soften, float TranscribeMix, float NoticeMix)
{
    /// <summary>Neutral, fully visible frame for static rendering and tests.</summary>
    public static OrbFrame Static(OverlayMode mode, float level = 0f) =>
        new(mode, 1f, 1f, level, 1.3f, 0f, 0f, 0f,
            mode == OverlayMode.Transcribing ? 1f : 0f, mode == OverlayMode.Notice ? 1f : 0f);
}
