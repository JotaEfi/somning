using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>Geometry of the glass drop in device-independent pixels (96 dpi) and helpers to scale it.</summary>
public static class DropLayout
{
    public const float OrbRadius = 44f;
    public const float OrbWindowWidth = 248f;    // room for the voice-stretched pill plus glow
    public const float OrbWindowHeight = 208f;   // room for the contact shadow and the caption pill below
    public const float NoticeHalfHeight = 29f;
    public const float NoticeTextPadding = 30f;
    public const float NoticeWindowMargin = 26f;
    private const float CaptionFontDip = 14f;

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

    /// <summary>Caption text mask for the transcribing pill (label without trailing dots, which are animated).</summary>
    public static TextMask CreateCaption(string? text, float dpiScale) =>
        TextMask.Create(CaptionText.WithoutTrailingDots(text), (int)MathF.Round(CaptionFontDip * dpiScale), 600,
            Math.Max(1, (int)MathF.Round(0.6f * dpiScale)), true);
}
