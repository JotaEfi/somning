using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed partial class DropRenderer
{
    /// <summary>Visual styles the shader can draw; one per <see cref="DropStyles"/> id.</summary>
    private enum StyleKind { Classic, C, F, G, H }

    private static StyleKind StyleKindOf(string? id) => id switch
    {
        DropStyles.C => StyleKind.C,
        DropStyles.F => StyleKind.F,
        DropStyles.G => StyleKind.G,
        DropStyles.H => StyleKind.H,
        _ => StyleKind.Classic
    };

    /// <summary>
    /// Shades one glass pixel in the active style. EVERY style must draw both the listening drop and its matching
    /// transcribing drop (same body, dark center band and reflections; transcribing only changes palette and flow), so each
    /// style method receives the frame (<c>s.F</c>) and reads <c>s.NeutralMix</c> / <c>s.TranscribeMix</c> to blend the two.
    /// </summary>
    private Px ShadeGlass(in Scene s, float dx, float dy, float dyl, float nx, float ny, float u) => s.Kind switch
    {
        StyleKind.C => ShadeGlassC(in s, dx, dy, dyl, nx, ny, u),
        StyleKind.F => ShadeGlassF(in s, dx, dy, dyl, nx, ny, u),
        StyleKind.G => ShadeGlassG(in s, dx, dy, dyl, nx, ny, u),
        StyleKind.H => ShadeGlassH(in s, dx, dy, dyl, nx, ny, u),
        _ => ShadeGlassClassic(in s, dx, dy, dyl, nx, ny, u)
    };
}
