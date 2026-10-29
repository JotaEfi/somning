namespace Somning.Core.Overlay;

/// <summary>
/// Liquid color sets of the orb: calm blue/pink/orange/white for listening (Siri-like), cyan/violet for
/// transcribing, red/amber for errors. <see cref="Resolve"/> blends them by the animator's mode mixes.
/// </summary>
public static class OrbPalette
{
    /// <summary>Floats per palette: 4 flow colors + glow, RGB each.</summary>
    public const int Floats = 5 * 3;

    private static readonly float[] Listening =
    [
        0.18f, 0.42f, 1.00f,   // blue
        1.00f, 0.28f, 0.62f,   // pink
        1.00f, 0.58f, 0.18f,   // orange
        0.88f, 0.94f, 1.00f,   // white
        0.30f, 0.46f, 1.00f    // glow
    ];

    private static readonly float[] Transcribing =
    [
        0.10f, 0.82f, 1.00f,   // cyan
        0.56f, 0.34f, 1.00f,   // violet
        0.20f, 0.46f, 0.96f,   // blue
        0.82f, 0.96f, 1.00f,   // white
        0.26f, 0.72f, 1.00f    // glow
    ];

    private static readonly float[] Notice =
    [
        1.00f, 0.66f, 0.08f,   // amber
        1.00f, 0.26f, 0.20f,   // red
        1.00f, 0.46f, 0.12f,   // orange
        1.00f, 0.90f, 0.70f,   // warm white
        1.00f, 0.52f, 0.10f    // glow
    ];

    /// <summary>Writes the blended palette (<see cref="Floats"/> values) into <paramref name="destination"/>.</summary>
    public static void Resolve(float transcribeMix, float noticeMix, Span<float> destination)
    {
        var t = OrbMath.Clamp01(transcribeMix);
        var n = OrbMath.Clamp01(noticeMix);
        for (var i = 0; i < Floats; i++)
        {
            var calm = Listening[i] + (Transcribing[i] - Listening[i]) * t;
            destination[i] = calm + (Notice[i] - calm) * n;
        }
    }
}
