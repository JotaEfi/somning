namespace Somning.Core.Overlay;

/// <summary>Screen rectangle (physical pixels) whose colors should be captured behind the overlay.</summary>
public readonly record struct BackdropRegion(int Left, int Top, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Supplies a heavily reduced picture of what is behind the overlay. Implementations fill
/// <see cref="BackdropTexture.Data"/> (already reduced) and must never keep or log the captured pixels.
/// </summary>
public interface IBackdropSource : IDisposable
{
    /// <summary>Fills <paramref name="target"/> for <paramref name="region"/>; returns false when capture is unavailable.</summary>
    bool TryCapture(in BackdropRegion region, BackdropTexture target);
}
