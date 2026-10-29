using System.Runtime.CompilerServices;

namespace Somning.Core.Overlay;

/// <summary>
/// Small blurred RGB picture (floats 0..1) of the area behind the overlay. It is the only form in which screen
/// content is ever held in memory. Maps to window pixel space through <see cref="OriginX"/>/<see cref="OriginY"/>
/// (top-left, window pixels) and <see cref="PixelsPerTexelX"/>/<see cref="PixelsPerTexelY"/>.
/// </summary>
public sealed class BackdropTexture
{
    public BackdropTexture(int width, int height)
    {
        Width = Math.Max(2, width);
        Height = Math.Max(2, height);
        Data = new float[Width * Height * 3];
    }

    public int Width { get; }
    public int Height { get; }
    public float[] Data { get; }
    public float OriginX { get; set; }
    public float OriginY { get; set; }
    public float PixelsPerTexelX { get; set; } = 1f;
    public float PixelsPerTexelY { get; set; } = 1f;

    public void CopyFrom(BackdropTexture other)
    {
        if (other.Width != Width || other.Height != Height) throw new ArgumentException("Texture size mismatch.", nameof(other));
        Array.Copy(other.Data, Data, Data.Length);
        OriginX = other.OriginX;
        OriginY = other.OriginY;
        PixelsPerTexelX = other.PixelsPerTexelX;
        PixelsPerTexelY = other.PixelsPerTexelY;
    }

    /// <summary>Moves every texel toward <paramref name="target"/> by <paramref name="amount"/> (0..1).</summary>
    public void Approach(BackdropTexture target, float amount)
    {
        var a = Data;
        var b = target.Data;
        for (var i = 0; i < a.Length; i++) a[i] += (b[i] - a[i]) * amount;
        OriginX = target.OriginX;
        OriginY = target.OriginY;
        PixelsPerTexelX = target.PixelsPerTexelX;
        PixelsPerTexelY = target.PixelsPerTexelY;
    }

    /// <summary>Bilinear sample at texel coordinates (texel centers at integer values), clamped to the edge.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SampleTexel(float tx, float ty, out float r, out float g, out float b)
    {
        tx = Math.Clamp(tx, 0f, Width - 1.001f);
        ty = Math.Clamp(ty, 0f, Height - 1.001f);
        var ix = (int)tx;
        var iy = (int)ty;
        var fx = tx - ix;
        var fy = ty - iy;
        var i0 = (iy * Width + ix) * 3;
        var i1 = i0 + Width * 3;
        var d = Data;
        var w00 = (1f - fx) * (1f - fy);
        var w10 = fx * (1f - fy);
        var w01 = (1f - fx) * fy;
        var w11 = fx * fy;
        r = d[i0] * w00 + d[i0 + 3] * w10 + d[i1] * w01 + d[i1 + 3] * w11;
        g = d[i0 + 1] * w00 + d[i0 + 4] * w10 + d[i1 + 1] * w01 + d[i1 + 4] * w11;
        b = d[i0 + 2] * w00 + d[i0 + 5] * w10 + d[i1 + 2] * w01 + d[i1 + 5] * w11;
    }

    /// <summary>Samples at a window pixel position.</summary>
    public void SampleWindow(float x, float y, out float r, out float g, out float b) =>
        SampleTexel((x - OriginX) / PixelsPerTexelX - 0.5f, (y - OriginY) / PixelsPerTexelY - 0.5f, out r, out g, out b);

    public void Fill(float r, float g, float b)
    {
        for (var i = 0; i < Data.Length; i += 3)
        {
            Data[i] = r;
            Data[i + 1] = g;
            Data[i + 2] = b;
        }
    }
}
