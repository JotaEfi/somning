namespace Somning.Core.Overlay;

/// <summary>Backdrop source backed by a pixel function over screen coordinates (headless previews and tests).</summary>
public sealed class FuncBackdropSource(Func<int, int, uint> pixelAt) : IBackdropSource
{
    private readonly BackdropReducer reducer = new();
    private uint[] scratch = [];

    public int Captures { get; private set; }

    public bool TryCapture(in BackdropRegion region, BackdropTexture target)
    {
        if (region.IsEmpty) return false;
        var count = region.Width * region.Height;
        if (scratch.Length < count) scratch = new uint[count];
        for (var y = 0; y < region.Height; y++)
            for (var x = 0; x < region.Width; x++)
                scratch[y * region.Width + x] = pixelAt(region.Left + x, region.Top + y);
        reducer.Reduce(scratch.AsSpan(0, count), region.Width, region.Height, target);
        Captures++;
        return true;
    }

    public void Dispose()
    {
    }
}
