using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>Geometry of the glass drop in device-independent pixels (96 dpi) and helpers to scale it.</summary>
public static class DropLayout
{
    public const float OrbRadius = 44f;
    public const float OrbWindowWidth = 248f;    // room for the voice-stretched pill plus glow
    public const float OrbWindowHeight = 208f;   // room for the contact shadow and the caption below
    public const float NoticeHalfHeight = 29f;
    public const float NoticeTextPadding = 30f;
    public const float NoticeWindowMargin = 26f;
    /// <summary>Distance from the resting drop center down to the bottom screen edge when the real one is unknown.</summary>
    public const float DefaultEdgeDistanceDip = 158f;
    private const float CaptionFontDip = 12.6f;   // 70% of the original 18 dip caption

    /// <summary>Half width of the notice capsule for a text mask of the given pixel width.</summary>
    public static float NoticeHalfWidth(float textWidthPx, float dpiScale) =>
        Math.Max(NoticeHalfHeight * dpiScale, textWidthPx / 2f + NoticeTextPadding * dpiScale);

    /// <summary>Window size in pixels: a fixed box for the drop, wider while the notice capsule is shown.</summary>
    public static (int Width, int Height) WindowSize(float dpiScale, bool isWide, float textWidthPx)
    {
        var width = (int)MathF.Ceiling(OrbWindowWidth * dpiScale);
        var height = (int)MathF.Ceiling(OrbWindowHeight * dpiScale);
        if (!isWide) return (width, height);
        var wide = (int)MathF.Ceiling(2f * (NoticeHalfWidth(textWidthPx, dpiScale) + NoticeWindowMargin * dpiScale));
        return (Math.Max(width, wide), height);
    }

    /// <summary>Caption text mask for the floating transcribing label, without trailing dots.</summary>
    public static TextMask CreateCaption(string? text, float dpiScale) =>
        TextMask.Create(CaptionText.WithoutTrailingDots(text), (int)MathF.Round(CaptionFontDip * dpiScale), 350,
            (int)MathF.Round(0.28f * dpiScale), true);
}
