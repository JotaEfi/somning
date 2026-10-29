namespace Somning.Core.Overlay;

/// <summary>Per-frame animation values consumed by the renderer.</summary>
/// <param name="Mode">Mode being drawn (kept while fading out after a hide).</param>
/// <param name="Opacity">Whole-window opacity, 0..1.</param>
/// <param name="Scale">Pill scale around its center, ~0.92..1.</param>
/// <param name="Pulse">Slow breathing value, 0..1, drives dot halo and border glow.</param>
/// <param name="Energy">Smoothed bar energy above rest, 0..1; brightens the glow while speaking.</param>
public readonly record struct OverlayFrame(OverlayMode Mode, float Opacity, float Scale, float Pulse, float Energy)
{
    /// <summary>Neutral frame: full size, opaque, no glow. Matches the pre-animation look.</summary>
    public static OverlayFrame Static(OverlayMode mode) => new(mode, 1f, 1f, 0f, 0f);
}
