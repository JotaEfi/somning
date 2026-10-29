using System.Runtime.CompilerServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Viscous entrance: the drop, a thinning liquid filament and the mound at the screen edge are merged into one
/// smooth signed-distance field (polynomial smooth-min) and shaded with the same glass as the drop, using the field
/// gradient as the surface normal and the local tube radius as the depth. Only runs during the ~0.6 s entrance.
/// </summary>
public sealed partial class DropRenderer
{
    private const float Far = 1e6f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool InGooBox(in Scene s, float dx, float dy) =>
        dy > -s.HalfHeight * 0.1f && MathF.Abs(dx) < s.GooReach;

    private void ShadeGooBlock(Span<uint> pixels, int width, int height, in Scene s, int x, int y, float opacity)
    {
        for (var dy = 0; dy < 2 && y + dy < height; dy++)
        {
            for (var dx = 0; dx < 2 && x + dx < width; dx++)
            {
                var px = ShadeGoo(in s, x + dx + 0.5f - s.CenterX, y + dy + 0.5f - s.CenterY);
                if (px.A <= 0.0005f) continue;
                pixels[(y + dy) * width + x + dx] = Px.Pack(px, opacity);
            }
        }
    }

    private Px ShadeGoo(in Scene s, float dx, float dy)
    {
        if (dy > s.EdgeDy) return default;   // nothing below the screen edge
        var d = GooField(in s, dx, dy, out var dDrop, out var dRest, out var rLocal);
        if (d > s.GlowRange) return default;

        // Pixels the goo does not reach keep the exact drop shading.
        var dropWeight = FastMath.Smooth((dRest - dDrop) / s.GooK);
        if (dropWeight >= 0.999f) return ShadePixel(in s, dx, dy);
        if (d > 0.5f) return Outside(in s, dx, dy, d);

        var gx = GooField(in s, dx + 1f, dy, out _, out _, out _) - GooField(in s, dx - 1f, dy, out _, out _, out _);
        var gy = GooField(in s, dx, dy + 1f, out _, out _, out _) - GooField(in s, dx, dy - 1f, out _, out _, out _);
        var g = MathF.Sqrt(gx * gx + gy * gy);
        var inv = g > 1e-5f ? 1f / g : 0f;
        var u = Math.Clamp(1f + d / MathF.Max(1f, rLocal), 0f, 1f);
        var nx = gx * inv * u;
        var ny = gy * inv * u;
        var dyl = dy;

        if (dropWeight > 0f)
        {
            DropGeometry(in s, dx, dy, out var dnx, out var dny, out var du, out var ddyl);
            nx += (dnx - nx) * dropWeight;
            ny += (dny - ny) * dropWeight;
            u += (du - u) * dropWeight;
            dyl += (ddyl - dyl) * dropWeight;
        }

        var px = ShadeGlass(in s, dx, dy, dyl, nx, ny, u);
        var coverage = Math.Min(1f, -d + 0.5f);
        if (coverage < 1f)
        {
            var outside = Outside(in s, dx, dy, Math.Max(0f, d));
            px = Px.Over(outside, Px.Scale(px, coverage));
        }

        return px;
    }

    /// <summary>Signed distance in pixels (negative inside) of drop + filament + edge mound.</summary>
    private static float GooField(in Scene s, float dx, float dy, out float dDrop, out float dRest, out float rLocal)
    {
        dDrop = DropGeometry(in s, dx, dy, out _, out _, out _, out _);
        var dTop = Neck(dx, dy, s.AttachDy, s.TopR0, s.TopEndDy, s.TopR1, out var rTop);
        var dBot = Neck(dx, dy, s.EdgeDy, s.BotR0, s.BotStartDy, s.BotR1, out var rBot);
        var dPool = Far;
        if (s.InvPoolRx > 0f)
        {
            var ex = dx * s.InvPoolRx;
            var ey = (dy - s.PoolCy) * s.InvPoolRy;
            dPool = (MathF.Sqrt(ex * ex + ey * ey) - 1f) * s.PoolMin;
        }

        var lower = SMin(dPool, dBot, s.GooLowK);
        dRest = Math.Min(dTop, lower);
        var upper = SMin(dDrop, dTop, s.GooK);
        var d = SMin(upper, lower, s.GooK * 0.45f);

        // Local tube radius (depth of the glass) blended from the nearest parts.
        var m = d;
        var wDrop = Weight(dDrop - m, s.GooK);
        var wTop = Weight(dTop - m, s.GooK);
        var wBot = Weight(dBot - m, s.GooK);
        var wPool = Weight(dPool - m, s.GooK);
        var sum = wDrop + wTop + wBot + wPool + 1e-6f;
        rLocal = (wDrop * s.EdgeScale + wTop * MathF.Max(rTop, 1f) + wBot * MathF.Max(rBot, 1f) + wPool * s.PoolMin) / sum;
        return d;
    }

    /// <summary>Drop geometry as in <see cref="ShadePixel"/>: lens normal (scaled by u), u and lean-corrected y. Sets the drop distance.</summary>
    private static float DropGeometry(in Scene s, float dx, float dy, out float nx, out float ny, out float u, out float dyl)
    {
        var along = Math.Clamp(dx * s.InvHalfWidth, -1f, 1f);
        dyl = dy * MathF.ReciprocalEstimate(1f + s.Lean * along);
        var core = Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var qx = (dx - core) * s.InvRx;
        var qy = dyl * s.InvRy;
        var d2 = qx * qx + qy * qy;
        var len = MathF.Sqrt(d2);
        var invLen = len > 1e-4f ? 1f / len : 0f;
        var zx = qx * invLen;
        var zy = qy * invLen;
        var z2x = zx * zx - zy * zy;
        var z2y = 2f * zx * zy;
        var z3x = z2x * zx - z2y * zy;
        var z3y = z2x * zy + z2y * zx;
        var z4x = z2x * z2x - z2y * z2y;
        var z4y = 2f * z2x * z2y;
        var r = 1f + s.L1x * zx - s.L1y * zy + s.L2x * z2x - s.L2y * z2y
                   + s.L3x * z3x - s.L3y * z3y + s.L4x * z4x - s.L4y * z4y;
        var ur = len / r;
        u = Math.Min(1f, ur);
        nx = qx / r;
        ny = qy / r;
        return -(1f - ur) * r * s.EdgeScale;
    }

    /// <summary>
    /// Vertical liquid neck from a thick end (yThick, rThick) to a thin end (yThin, rThin) with a concave
    /// (quadratic) profile, like a stretched viscous filament. Rounded caps at both ends.
    /// </summary>
    private static float Neck(float dx, float dy, float yThick, float rThick, float yThin, float rThin, out float r)
    {
        var span = yThin - yThick;
        if (MathF.Abs(span) < 0.5f || rThick <= 0.05f)
        {
            r = 0f;
            return Far;
        }

        var t = (dy - yThick) / span;   // 0 at the thick end, 1 at the thin end
        if (t <= 0f)
        {
            r = rThick;
            var ey = dy - yThick;
            return MathF.Sqrt(dx * dx + ey * ey) - rThick;
        }

        if (t >= 1f)
        {
            r = rThin;
            var ey = dy - yThin;
            return MathF.Sqrt(dx * dx + ey * ey) - rThin;
        }

        var w = 1f - t;
        r = rThin + (rThick - rThin) * w * w;
        var slope = 2f * (rThick - rThin) * w / span;
        // The slope correction degenerates on very short necks; the widest radius bounds the true distance.
        return MathF.Max((MathF.Abs(dx) - r) / MathF.Sqrt(1f + slope * slope), MathF.Abs(dx) - MathF.Max(rThick, rThin));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SMin(float a, float b, float k)
    {
        var h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0f, 1f);
        return b + (a - b) * h - k * h * (1f - h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Weight(float excess, float k)
    {
        var e = excess / k;
        return 1f / (1f + 8f * e * e);
    }
}
