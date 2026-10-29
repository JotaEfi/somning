namespace Somning.Core.Overlay;

/// <summary>
/// Samples an <see cref="IBackdropSource"/> at a low rate (~15 fps), keeps a small reduced texture and eases the
/// displayed texture toward each new capture so the reflection never pops. Degrades to "unavailable" (Availability
/// fades to 0) when capture fails repeatedly. Holds only the reduced texture; never persists anything. Allocates
/// only when the capture region changes size.
/// </summary>
public sealed class BackdropCache
{
    public const float CaptureIntervalSeconds = 1f / 15f;
    public const float MarginDip = 40f;
    public const float TexelDip = 1.5f;
    public const int MaxTexelsPerSide = 224;
    public const int MaxConsecutiveFailures = 3;
    private const float SmoothSeconds = 0.045f;
    private const float AvailabilitySeconds = 0.25f;

    private readonly IBackdropSource? source;
    private BackdropTexture? target;
    private BackdropTexture? shown;
    private BackdropRegion region;
    private float sinceCapture = float.MaxValue;
    private int failures;
    private bool hasData;
    private bool snapNext = true;

    public BackdropCache(IBackdropSource? source) => this.source = source;

    public bool IsEnabled => source is not null && failures < MaxConsecutiveFailures;

    /// <summary>0..1 weight of the backdrop in the shading; fades in after the first capture and out on failure.</summary>
    public float Availability { get; private set; }

    /// <summary>Texture to shade with, or null when none exists yet.</summary>
    public BackdropTexture? Texture => hasData && (IsEnabled || Availability > 0f) ? shown : null;

    public BackdropRegion Region => region;

    /// <summary>Capture region for a window: the window rect inflated by the reflection margin, clipped to <paramref name="bounds"/>.</summary>
    public static BackdropRegion RegionFor(int windowLeft, int windowTop, int windowWidth, int windowHeight, float dpiScale, BackdropRegion bounds)
    {
        var m = (int)MathF.Round(MarginDip * dpiScale);
        var l = windowLeft - m;
        var t = windowTop - m;
        var r = windowLeft + windowWidth + m;
        var b = windowTop + windowHeight + m;
        if (!bounds.IsEmpty)
        {
            l = Math.Max(l, bounds.Left);
            t = Math.Max(t, bounds.Top);
            r = Math.Min(r, bounds.Left + bounds.Width);
            b = Math.Min(b, bounds.Top + bounds.Height);
        }

        return new BackdropRegion(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    /// <summary>Texture size for a region at a DPI: about <see cref="TexelDip"/> device-independent pixels per texel.</summary>
    public static (int W, int H) TextureSizeFor(in BackdropRegion r, float dpiScale)
    {
        var texel = TexelDip * dpiScale;
        return (Math.Clamp((int)MathF.Ceiling(r.Width / texel), 2, MaxTexelsPerSide),
                Math.Clamp((int)MathF.Ceiling(r.Height / texel), 2, MaxTexelsPerSide));
    }

    /// <summary>Sets the window placement; a changed region forces an immediate recapture.</summary>
    public void Configure(int windowLeft, int windowTop, int windowWidth, int windowHeight, float dpiScale, BackdropRegion bounds)
    {
        if (!IsEnabled) return;
        var next = RegionFor(windowLeft, windowTop, windowWidth, windowHeight, dpiScale, bounds);
        if (next.IsEmpty) return;
        var (tw, th) = TextureSizeFor(next, dpiScale);
        if (target is null || target.Width != tw || target.Height != th)
        {
            target = new BackdropTexture(tw, th);
            shown = new BackdropTexture(tw, th);
            hasData = false;
            snapNext = true;
        }

        if (!next.Equals(region)) sinceCapture = float.MaxValue;
        region = next;
        target.OriginX = region.Left - windowLeft;
        target.OriginY = region.Top - windowTop;
        target.PixelsPerTexelX = (float)region.Width / target.Width;
        target.PixelsPerTexelY = (float)region.Height / target.Height;
    }

    /// <summary>Drops the shown texture so the next show starts from a fresh capture (called when the overlay hides).</summary>
    public void Reset()
    {
        hasData = false;
        snapNext = true;
        sinceCapture = float.MaxValue;
        Availability = 0f;
        target?.Fill(0f, 0f, 0f);
        shown?.Fill(0f, 0f, 0f);
    }

    /// <summary>Advances time: captures when due, eases the shown texture and the availability weight.</summary>
    public void Advance(float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        sinceCapture += dt;
        if (IsEnabled && target is not null && shown is not null && sinceCapture >= CaptureIntervalSeconds)
        {
            sinceCapture = 0f;
            if (Capture(target))
            {
                failures = 0;
                hasData = true;
            }
            else
            {
                failures++;
            }
        }

        if (hasData && shown is not null && target is not null)
        {
            if (snapNext)
            {
                shown.CopyFrom(target);
                snapNext = false;
            }
            else
            {
                shown.Approach(target, 1f - MathF.Exp(-dt / SmoothSeconds));
            }
        }

        var goal = hasData && IsEnabled ? 1f : 0f;
        Availability += (goal - Availability) * (1f - MathF.Exp(-dt / AvailabilitySeconds));
        if (goal == 0f && Availability < 0.002f) Availability = 0f;
    }

    private bool Capture(BackdropTexture destination)
    {
        try
        {
            return source!.TryCapture(in region, destination);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }
}
