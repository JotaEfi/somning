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
/// <param name="Elongation">Voice-driven horizontal stretch (0 = circle); width x(1+e), height x1/sqrt(1+e), constant volume.</param>
/// <param name="Lean">Teardrop asymmetry, positive = fat end on the right.</param>
/// <param name="Ripple">0..1 surface ripple energy from phoneme transients.</param>
/// <param name="RipplePhase">Ripple wave phase in radians.</param>
/// <param name="Rise">Entrance: drop center between the screen edge (0) and its resting place (1).</param>
/// <param name="Squash">Entrance: volume-preserving vertical elongation (positive = taller).</param>
/// <param name="Goo">Entrance: 0..1 presence of the edge mound and filament (see <see cref="EmergeShape"/>).</param>
/// <param name="Neck">Entrance: filament radius at the pinch point, in drop radii.</param>
/// <param name="TopTail">Entrance: remaining fraction of the upper filament after the snap.</param>
/// <param name="BottomTail">Entrance: remaining fraction of the lower filament after the snap.</param>
/// <param name="Pool">Entrance: radius of the mound at the screen edge, in drop radii.</param>
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
    float Lobe4X, float Lobe4Y,
    float Elongation = 0f,
    float Lean = 0f,
    float Ripple = 0f,
    float RipplePhase = 0f,
    float Rise = 1f,
    float Squash = 0f,
    float Goo = 0f,
    float Neck = 0f,
    float TopTail = 0f,
    float BottomTail = 0f,
    float Pool = 0f)
{
    /// <summary>True while the viscous entrance (edge mound / filament) must be drawn from the screen edge.</summary>
    public bool IsEmerging => Goo > 0f || Rise < 0.999f || Rise > 1.001f;

    /// <summary>Neutral frame for the mode: full size, opaque, no voice, no lobes.</summary>
    public static DropFrame Static(OverlayMode mode) => new(
        mode, 1f, 1f, 0f, 0f, 1f, 0f,
        mode == OverlayMode.Notice ? 1f : 0f,
        mode == OverlayMode.Transcribing ? 1f : 0f,
        mode == OverlayMode.Notice ? 1f : 0f,
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
}
