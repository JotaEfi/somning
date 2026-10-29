namespace Somning.Core.Overlay;

/// <summary>Box-downsamples a full-resolution BGRA capture into a <see cref="BackdropTexture"/> and softens it. Pure; allocation free after the first call.</summary>
public sealed class BackdropReducer
{
    private float[] scratch = [];

    /// <summary>Reduces <paramref name="bgra"/> (width x height, 0xAARRGGBB) into <paramref name="target"/> (box filter + two 3x3 blur passes).</summary>
    public void Reduce(ReadOnlySpan<uint> bgra, int width, int height, BackdropTexture target)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height) throw new ArgumentException("Invalid capture size.");
        var tw = target.Width;
        var th = target.Height;
        var data = target.Data;
        for (var ty = 0; ty < th; ty++)
        {
            var y0 = ty * height / th;
            var y1 = Math.Max(y0 + 1, (ty + 1) * height / th);
            for (var tx = 0; tx < tw; tx++)
            {
                var x0 = Math.Min(tx * width / tw, width - 1);
                var x1 = Math.Min(width, Math.Max(x0 + 1, (tx + 1) * width / tw));
                float r = 0f, g = 0f, b = 0f;
                for (var y = y0; y < y1; y++)
                {
                    var row = bgra.Slice(y * width + x0, x1 - x0);
                    uint sr = 0, sg = 0, sb = 0;
                    foreach (var p in row)
                    {
                        sr += (p >> 16) & 0xFF;
                        sg += (p >> 8) & 0xFF;
                        sb += p & 0xFF;
                    }

                    r += sr;
                    g += sg;
                    b += sb;
                }

                var k = 1f / (255f * (y1 - y0) * (x1 - x0));
                var o = (ty * tw + tx) * 3;
                data[o] = r * k;
                data[o + 1] = g * k;
                data[o + 2] = b * k;
            }
        }

        Blur(data, tw, th);
        Blur(data, tw, th);   // two passes: a smooth, lens-friendly blur without hard window edges
    }

    private void Blur(float[] data, int w, int h)
    {
        if (scratch.Length < data.Length) scratch = new float[data.Length];
        Array.Copy(data, scratch, data.Length);
        for (var y = 0; y < h; y++)
        {
            var ya = Math.Max(0, y - 1);
            var yb = Math.Min(h - 1, y + 1);
            for (var x = 0; x < w; x++)
            {
                var xa = Math.Max(0, x - 1);
                var xb = Math.Min(w - 1, x + 1);
                var n = (yb - ya + 1) * (xb - xa + 1);
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0f;
                    for (var yy = ya; yy <= yb; yy++)
                        for (var xx = xa; xx <= xb; xx++)
                            sum += scratch[(yy * w + xx) * 3 + c];
                    data[(y * w + x) * 3 + c] = sum / n;
                }
            }
        }
    }
}
