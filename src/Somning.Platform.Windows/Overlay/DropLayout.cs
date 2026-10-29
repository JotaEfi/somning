namespace Somning.Platform.Windows.Overlay;

/// <summary>Geometry of the glass drop in device-independent pixels (96 dpi) and helpers to scale it.</summary>
public static class DropLayout
{
    public const float OrbRadius = 44f;
    public const float OrbWindowSize = 176f;
    public const float NoticeHalfHeight = 29f;
    public const float NoticeTextPadding = 30f;
    public const float NoticeWindowMargin = 26f;

    /// <summary>Half width of the notice capsule for a text mask of the given pixel width.</summary>
    public static float NoticeHalfWidth(float textWidthPx, float dpiScale) =>
        Math.Max(NoticeHalfHeight * dpiScale, textWidthPx / 2f + NoticeTextPadding * dpiScale);

    /// <summary>Window size in pixels: a square for the round drop, wider while the notice capsule is shown.</summary>
    public static (int Width, int Height) WindowSize(float dpiScale, bool isWide, float textWidthPx)
    {
        var square = (int)MathF.Ceiling(OrbWindowSize * dpiScale);
        if (!isWide) return (square, square);
        var wide = (int)MathF.Ceiling(2f * (NoticeHalfWidth(textWidthPx, dpiScale) + NoticeWindowMargin * dpiScale));
        return (Math.Max(square, wide), square);
    }
}
