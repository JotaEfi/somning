using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class BackdropTests
{
    private const float Frame = 1f / 60f;

    private sealed class ScriptedSource(Func<bool> result) : IBackdropSource
    {
        public int Calls { get; private set; }

        public bool TryCapture(in BackdropRegion region, BackdropTexture target)
        {
            Calls++;
            if (result()) target.Fill(0.5f, 0.25f, 0.75f);
            return result();
        }

        public void Dispose()
        {
        }
    }

    private static readonly BackdropRegion Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Reducer_keeps_a_uniform_color_and_separates_halves()
    {
        var texture = new BackdropTexture(16, 8);
        var reducer = new BackdropReducer();
        var pixels = new uint[160 * 80];
        Array.Fill(pixels, 0xFF336699u);
        reducer.Reduce(pixels, 160, 80, texture);
        Assert.Equal(0x33 / 255f, texture.Data[0], 3);
        Assert.Equal(0x99 / 255f, texture.Data[^1], 3);

        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 160; x++)
                pixels[y * 160 + x] = x < 80 ? 0xFFFF0000u : 0xFF0000FFu;
        reducer.Reduce(pixels, 160, 80, texture);
        texture.SampleTexel(1f, 4f, out var lr, out _, out var lb);
        texture.SampleTexel(14f, 4f, out var rr, out _, out var rb);
        Assert.True(lr > 0.8f && lb < 0.2f, $"left {lr},{lb}");
        Assert.True(rb > 0.8f && rr < 0.2f, $"right {rr},{rb}");
    }

    [Fact]
    public void Reducer_rejects_a_buffer_smaller_than_the_declared_size()
    {
        Assert.Throws<ArgumentException>(() => new BackdropReducer().Reduce(new uint[10], 20, 20, new BackdropTexture(4, 4)));
    }

    [Fact]
    public void Sampling_is_bilinear_and_clamped()
    {
        var texture = new BackdropTexture(2, 2);
        texture.Data[0] = 0f; texture.Data[3] = 1f; texture.Data[6] = 0f; texture.Data[9] = 1f;
        texture.SampleTexel(0.5f, 0f, out var mid, out _, out _);
        Assert.Equal(0.5f, mid, 3);
        texture.SampleTexel(-50f, -50f, out var corner, out _, out _);
        Assert.Equal(0f, corner, 3);
        texture.SampleTexel(50f, 50f, out var far, out _, out _);
        Assert.InRange(far, 0.99f, 1f);
    }

    [Fact]
    public void Window_sampling_follows_the_origin_and_texel_size()
    {
        var texture = new BackdropTexture(4, 4) { OriginX = -10f, OriginY = -10f, PixelsPerTexelX = 5f, PixelsPerTexelY = 5f };
        for (var i = 0; i < 16; i++) texture.Data[i * 3] = i % 4;   // red grows with x
        texture.SampleWindow(-7.5f, 0f, out var r0, out _, out _);   // texel 0
        texture.SampleWindow(2.5f, 0f, out var r1, out _, out _);    // texel 2
        Assert.Equal(0f, r0, 2);
        Assert.Equal(2f, r1, 2);
    }

    [Fact]
    public void Region_is_the_window_plus_margin_clipped_to_the_screen()
    {
        var region = BackdropCache.RegionFor(836, 818, 248, 208, 1f, Screen);
        Assert.Equal(836 - 40, region.Left);
        Assert.Equal(818 - 40, region.Top);
        Assert.Equal(248 + 80, region.Width);
        Assert.Equal(1026 + 40 - 778, region.Height);

        var corner = BackdropCache.RegionFor(0, 900, 248, 208, 1f, Screen);
        Assert.Equal(0, corner.Left);
        Assert.Equal(1080, corner.Top + corner.Height);

        var (tw, th) = BackdropCache.TextureSizeFor(new BackdropRegion(0, 0, 4000, 4000), 1f);
        Assert.True(tw <= BackdropCache.MaxTexelsPerSide && th <= BackdropCache.MaxTexelsPerSide);
    }

    [Fact]
    public void Cache_captures_at_a_low_rate_and_fades_the_backdrop_in()
    {
        var source = new FuncBackdropSource((x, y) => 0xFFFF0000u);
        var cache = new BackdropCache(source);
        Assert.Null(cache.Texture);
        cache.Configure(836, 818, 248, 208, 1f, Screen);
        for (var i = 0; i < 60; i++)
        {
            cache.Advance(Frame);
            cache.Configure(836, 818, 248, 208, 1f, Screen);
        }

        Assert.InRange(source.Captures, 13, 17);       // ~15 per second, never per frame
        Assert.NotNull(cache.Texture);
        Assert.True(cache.Availability > 0.9f);
        cache.Texture!.SampleWindow(100f, 100f, out var r, out var g, out _);
        Assert.True(r > 0.95f && g < 0.05f);
    }

    [Fact]
    public void Cache_without_a_source_stays_unavailable()
    {
        var cache = new BackdropCache(null);
        cache.Configure(0, 0, 248, 208, 1f, Screen);
        for (var i = 0; i < 30; i++) cache.Advance(Frame);
        Assert.False(cache.IsEnabled);
        Assert.Null(cache.Texture);
        Assert.Equal(0f, cache.Availability);
    }

    [Fact]
    public void Repeated_capture_failures_disable_the_backdrop_gracefully()
    {
        var healthy = true;
        var source = new ScriptedSource(() => healthy);
        var cache = new BackdropCache(source);
        cache.Configure(0, 0, 248, 208, 1f, Screen);
        for (var i = 0; i < 30; i++) cache.Advance(Frame);
        Assert.True(cache.Availability > 0.5f);

        healthy = false;
        for (var i = 0; i < 240; i++)
        {
            cache.Configure(0, 0, 248, 208, 1f, Screen);
            cache.Advance(Frame);
        }

        Assert.False(cache.IsEnabled);
        Assert.Equal(0f, cache.Availability);
        Assert.Null(cache.Texture);
        var calls = source.Calls;
        for (var i = 0; i < 60; i++) cache.Advance(Frame);
        Assert.Equal(calls, source.Calls);             // stops trying instead of hammering the screen
    }

    [Fact]
    public void A_throwing_source_is_treated_as_unavailable()
    {
        var cache = new BackdropCache(new ThrowingSource());
        cache.Configure(0, 0, 248, 208, 1f, Screen);
        for (var i = 0; i < 120; i++) cache.Advance(Frame);
        Assert.False(cache.IsEnabled);
        Assert.Null(cache.Texture);
    }

    [Fact]
    public void Reset_forgets_the_picture_and_wipes_the_texture()
    {
        var cache = new BackdropCache(new FuncBackdropSource((x, y) => 0xFFFFFFFFu));
        cache.Configure(0, 0, 248, 208, 1f, Screen);
        for (var i = 0; i < 20; i++) cache.Advance(Frame);
        var texture = cache.Texture!;
        cache.Reset();
        Assert.Null(cache.Texture);
        Assert.Equal(0f, cache.Availability);
        Assert.All(texture.Data, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Texture_eases_toward_a_new_capture_instead_of_popping()
    {
        var color = 0xFF000000u;
        var cache = new BackdropCache(new FuncBackdropSource((x, y) => color));
        cache.Configure(0, 0, 248, 208, 1f, Screen);
        for (var i = 0; i < 20; i++) cache.Advance(Frame);
        color = 0xFFFFFFFFu;
        for (var i = 0; i < 5; i++) cache.Advance(Frame);       // one capture lands, display eases
        cache.Texture!.SampleWindow(100f, 100f, out var early, out _, out _);
        for (var i = 0; i < 60; i++) cache.Advance(Frame);
        cache.Texture!.SampleWindow(100f, 100f, out var late, out _, out _);
        Assert.True(early < 0.95f, $"early {early}");
        Assert.True(late > 0.98f, $"late {late}");
    }

    [Fact]
    public void Fast_math_is_close_to_the_exact_functions()
    {
        for (var a = -20f; a < 20f; a += 0.013f) Assert.InRange(FastMath.Sin(a) - MathF.Sin(a), -0.003f, 0.003f);
        for (var x = 0f; x <= 4f; x += 0.01f) Assert.InRange(FastMath.Sqrt(x) - MathF.Sqrt(x), -0.003f, 0.003f);
        Assert.Equal(0f, FastMath.Sqrt(0f), 3);
    }

    [Fact]
    public void Caption_text_drops_trailing_dots_and_dots_pulse_in_sequence()
    {
        Assert.Equal("Transcrevendo", CaptionText.WithoutTrailingDots("Transcrevendo..."));
        Assert.Equal("Transcrevendo", CaptionText.WithoutTrailingDots("Transcrevendo…"));
        Assert.Equal(string.Empty, CaptionText.WithoutTrailingDots(null));

        for (var t = 0f; t < 3f; t += 0.017f)
            for (var i = 0; i < 3; i++)
                Assert.InRange(CaptionText.DotAlpha(i, t), 0.3f, 1f);
        Assert.NotEqual(CaptionText.DotAlpha(0, 0.4f), CaptionText.DotAlpha(2, 0.4f));
    }

    private sealed class ThrowingSource : IBackdropSource
    {
        public bool TryCapture(in BackdropRegion region, BackdropTexture target) => throw new InvalidOperationException("denied");

        public void Dispose()
        {
        }
    }
}
