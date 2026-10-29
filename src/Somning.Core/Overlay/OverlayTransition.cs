namespace Somning.Core.Overlay;

/// <summary>Fade + slight scale for pill entry/exit. Progress moves linearly in time and is eased on read.</summary>
public sealed class OverlayTransition
{
    public const float EnterSeconds = 0.16f;
    public const float ExitSeconds = 0.14f;
    public const float MinScale = 0.92f;

    private float progress;
    private bool isShowing;

    public bool IsShowing => isShowing;

    /// <summary>True once fully faded out; the window can then be hidden.</summary>
    public bool IsHidden => !isShowing && progress <= 0f;

    public bool IsAnimating => progress != (isShowing ? 1f : 0f);

    public float Opacity => Ease(progress);

    public float Scale => MinScale + (1f - MinScale) * Ease(progress);

    public void Show() => isShowing = true;

    public void Hide() => isShowing = false;

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;
        progress = isShowing
            ? Math.Min(1f, progress + deltaSeconds / EnterSeconds)
            : Math.Max(0f, progress - deltaSeconds / ExitSeconds);
    }

    private static float Ease(float t) => t * t * (3f - 2f * t); // smoothstep
}
