namespace Somning.Core.Overlay;

/// <summary>
/// Window geometry of the overlay in logical (96 dpi) pixels: a square-ish orb zone on top and a caption strip below.
/// <see cref="Scaled"/> converts to physical pixels for a DPI scale.
/// </summary>
public readonly record struct OrbLayout(int Width, int Height, float CenterX, float CenterY, float Radius, float CaptionCenterY, float CaptionHeight)
{
    public const int LogicalWidth = 280;
    public const int LogicalHeight = 176;
    public const float LogicalRadius = 35f;
    public const float LogicalCenterY = 78f;
    public const float LogicalCaptionCenterY = 150f;
    public const float LogicalCaptionHeight = 24f;

    public static OrbLayout Scaled(float dpiScale) => new(
        (int)MathF.Round(LogicalWidth * dpiScale),
        (int)MathF.Round(LogicalHeight * dpiScale),
        LogicalWidth * dpiScale / 2f,
        LogicalCenterY * dpiScale,
        LogicalRadius * dpiScale,
        LogicalCaptionCenterY * dpiScale,
        LogicalCaptionHeight * dpiScale);
}
