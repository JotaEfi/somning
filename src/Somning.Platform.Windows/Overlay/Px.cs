using System.Runtime.CompilerServices;

namespace Somning.Platform.Windows.Overlay;

/// <summary>Premultiplied RGBA color in floats, plus the small compositing helpers the software renderers share.</summary>
internal readonly record struct Px(float R, float G, float B, float A)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Px Over(Px top, Px bottom)
    {
        var k = 1f - top.A;
        return new Px(top.R + bottom.R * k, top.G + bottom.G * k, top.B + bottom.B * k, top.A + bottom.A * k);
    }

    /// <summary>Adds light (premultiplied) and raises alpha to at least <paramref name="a"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Px Add(Px p, float r, float g, float b, float a) =>
        new(p.R + r, p.G + g, p.B + b, Math.Min(1f, Math.Max(p.A, a)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Px Scale(Px p, float k) => new(p.R * k, p.G * k, p.B * k, p.A * k);

    public static uint Pack(Px p, float opacity) => PackBytes(p.R * opacity, p.G * opacity, p.B * opacity, p.A * opacity);

    public static uint PackBytes(float r, float g, float b, float a)
    {
        var ai = (uint)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f);
        var ri = Math.Min((uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f), ai);
        var gi = Math.Min((uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f), ai);
        var bi = Math.Min((uint)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f), ai);
        return (ai << 24) | (ri << 16) | (gi << 8) | bi;
    }

    /// <summary>Composites <paramref name="src"/> (premultiplied) over a premultiplied ARGB destination pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint CompositeOver(uint dst, Px src)
    {
        var inv = 1f - src.A;
        return PackBytes(
            src.R + ((dst >> 16) & 0xFF) / 255f * inv,
            src.G + ((dst >> 8) & 0xFF) / 255f * inv,
            src.B + (dst & 0xFF) / 255f * inv,
            src.A + (dst >> 24) / 255f * inv);
    }
}
