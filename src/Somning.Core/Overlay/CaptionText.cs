namespace Somning.Core.Overlay;

/// <summary>Helpers for the transcribing caption: the label is drawn without its trailing dots, which are animated.</summary>
public static class CaptionText
{
    /// <summary>Label without trailing periods or an ellipsis character (the renderer animates dots instead).</summary>
    public static string WithoutTrailingDots(string? text) =>
        (text ?? string.Empty).TrimEnd('.', '…', ' ');

    /// <summary>Opacity 0..1 of animated dot <paramref name="index"/> at <paramref name="seconds"/>: a travelling pulse.</summary>
    public static float DotAlpha(int index, float seconds)
    {
        var phase = (seconds * 1.35f - index * 0.2f) % 1f;
        if (phase < 0f) phase += 1f;
        var pulse = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;   // triangle
        pulse = pulse * pulse * (3f - 2f * pulse);
        return 0.30f + 0.70f * pulse;
    }
}
