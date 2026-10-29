namespace Somning.Core.Overlay;

/// <summary>Liquid colors (sRGB 0..1) for the blobs flowing inside the glass drop, per state.</summary>
public static class DropPalette
{
    public const int BlobCount = 5;
    public const int FloatsPerBlob = 3;
    public const int Length = BlobCount * FloatsPerBlob;

    // Listening: Siri-like blue, orange, pink, white and a second blue.
    private static readonly float[] Listening =
    [
        0.14f, 0.40f, 1.00f,
        1.00f, 0.50f, 0.10f,
        1.00f, 0.26f, 0.56f,
        0.92f, 0.86f, 0.96f,
        0.30f, 0.62f, 1.00f
    ];

    // Transcribing: calm cyan / violet / indigo.
    private static readonly float[] Transcribing =
    [
        0.18f, 0.86f, 1.00f,
        0.58f, 0.34f, 1.00f,
        0.26f, 0.40f, 1.00f,
        0.86f, 0.97f, 1.00f,
        0.30f, 0.90f, 0.86f
    ];

    // Notice: amber.
    private static readonly float[] Notice =
    [
        1.00f, 0.68f, 0.10f,
        1.00f, 0.46f, 0.08f,
        1.00f, 0.84f, 0.34f,
        1.00f, 0.95f, 0.80f,
        1.00f, 0.58f, 0.14f
    ];

    /// <summary>Writes the blended blob colors (r, g, b per blob) into <paramref name="destination"/> (length at least <see cref="Length"/>).</summary>
    public static void Blend(Span<float> destination, float transcribeMix, float noticeMix)
    {
        var t = Math.Clamp(transcribeMix, 0f, 1f);
        var n = Math.Clamp(noticeMix, 0f, 1f);
        for (var i = 0; i < Length; i++)
        {
            var calm = Listening[i] + (Transcribing[i] - Listening[i]) * t;
            destination[i] = calm + (Notice[i] - calm) * n;
        }
    }
}
