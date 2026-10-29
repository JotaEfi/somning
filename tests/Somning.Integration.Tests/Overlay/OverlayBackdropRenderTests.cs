using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>Headless checks for the living drop: voice stretch, desktop reflection (fake backdrops), frosted caption.</summary>
public sealed class OverlayBackdropRenderTests
{
    private const float Step = 1f / 60f;

    private static DropFrame Settled(OverlayMode mode, float seconds, float level = 0f)
    {
        var animator = new OverlayAnimator();
        animator.Show(mode);
        for (var t = 0f; t < seconds; t += Step)
        {
            animator.SetLevel(level);
            animator.Tick(Step);
        }

        return animator.Frame;
    }

    private static BackdropTexture Solid(int width, int height, float r, float g, float b)
    {
        var texture = new BackdropTexture(8, 8) { OriginX = -40f, OriginY = -40f, PixelsPerTexelX = (width + 80f) / 8f, PixelsPerTexelY = (height + 80f) / 8f };
        texture.Fill(r, g, b);
        return texture;
    }

    private static uint[] Render(in DropFrame frame, BackdropTexture? backdrop, float mix, TextMask? caption = null, float dpi = 1f)
    {
        var (w, h) = DropLayout.WindowSize(dpi, false, 0f);
        var buffer = new uint[w * h];
        new DropRenderer().Render(buffer, w, h, dpi, in frame, null, 0f, caption, backdrop, mix);
        return buffer;
    }

    private static (int Left, int Right, int Top, int Bottom) Bounds(uint[] buffer, int w, int h, uint minAlpha)
    {
        int left = w, right = -1, top = h, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if ((buffer[y * w + x] >> 24) < minAlpha) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return (left, right, top, bottom);
    }

    [Fact]
    public void Loud_voice_stretches_the_drop_horizontally_into_a_pill()
    {
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var quiet = Bounds(Render(Settled(OverlayMode.Listening, 2f, 0f), null, 0f), w, h, 150);
        var loud = Bounds(Render(Settled(OverlayMode.Listening, 2f, 0.2f), null, 0f), w, h, 150);

        var quietW = quiet.Right - quiet.Left;
        var quietH = quiet.Bottom - quiet.Top;
        var loudW = loud.Right - loud.Left;
        var loudH = loud.Bottom - loud.Top;
        Assert.InRange((float)quietW / quietH, 0.9f, 1.2f);          // at rest: round
        Assert.True((float)loudW / loudH > 1.45f, $"loud {loudW}x{loudH}");
        Assert.True(loudH < quietH, "the drop gets lower as it gets longer (volume is preserved)");
        Assert.True(loudW < w - 40, "the stretched pill still fits the window with room for the glow");
    }

    [Fact]
    public void Backdrop_with_zero_mix_or_no_texture_renders_the_plain_drop()
    {
        var frame = Settled(OverlayMode.Listening, 1.5f, 0.1f);
        var plain = Render(frame, null, 0f);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        Assert.Equal(plain, Render(frame, Solid(w, h, 1f, 0f, 0f), 0f));
        Assert.Equal(plain, Render(frame, null, 1f));
    }

    [Fact]
    public void Listening_glass_stays_grayscale_even_over_colored_desktops()
    {
        var frame = Settled(OverlayMode.Listening, 2f, 0.12f);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        foreach (var backdrop in new[] { Solid(w, h, 1f, 0.05f, 0.05f), Solid(w, h, 0.05f, 0.1f, 1f) })
        {
            foreach (var p in Render(frame, backdrop, 1f))
            {
                Assert.Equal((p >> 16) & 255, (p >> 8) & 255);
                Assert.Equal((p >> 16) & 255, p & 255);
            }
        }
    }

    [Fact]
    public void Long_utterance_keeps_wave_motion_continuous_across_the_ripple_clock_wrap()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var wraps = 0;
        for (var i = 0; i < 60 * 20; i++)
        {
            var previous = animator.Frame;
            animator.SetLevel(0.12f);
            animator.Tick(Step);
            var current = animator.Frame;
            if (current.RipplePhase >= previous.RipplePhase) continue;
            wraps++;
            var before = Render(previous, null, 0f);
            var after = Render(current, null, 0f);
            var delta = 0f;
            var samples = 0;
            for (var y = h / 2 - 18; y < h / 2 + 18; y++)
                for (var x = w / 2 - 38; x < w / 2 + 38; x++)
                {
                    var p = y * w + x;
                    delta += Math.Abs((int)((before[p] >> 16) & 255) - (int)((after[p] >> 16) & 255));
                    samples++;
                }
            Assert.True(delta / samples < 4f, $"wave jumped by {delta / samples:F2} gray levels per pixel at wrap");
        }
        Assert.True(wraps >= 2, "exercise multiple wraps during a 20-second utterance");
    }

    [Fact]
    public void Lens_preserves_detail_and_folds_the_image_near_the_rim()
    {
        var frame = DropFrame.Static(OverlayMode.Listening);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var gradient = new BackdropTexture(w, h);
        var inverse = new BackdropTexture(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                for (var c = 0; c < 3; c++)
                {
                    var value = Math.Clamp(0.5f + (x - w / 2f) / 80f, 0f, 1f);
                    gradient.Data[(y * w + x) * 3 + c] = value;
                    inverse.Data[(y * w + x) * 3 + c] = 1f - value;
                }
        var forward = Render(frame, gradient, 1f);
        var backward = Render(frame, inverse, 1f);
        var white = Render(frame, Solid(w, h, 1f, 1f, 1f), 1f);
        var black = Render(frame, Solid(w, h, 0f, 0f, 0f), 1f);
        // Opposite ramps cancel additive lighting. White/black normalization cancels transmission
        // changes from the dark band and rim. Sample before the outer reflected highlight begins.
        float Difference(int dx)
        {
            var i = (h / 2 - 10) * w + w / 2 + dx;
            static float Red(uint p) => (p >> 16) & 255;
            return (Red(forward[i]) - Red(backward[i])) / Math.Max(1f, Red(white[i]) - Red(black[i]));
        }
        Assert.True(Difference(28) > Difference(8) + 0.1f, "center retains the gradient direction");
        Assert.True(Difference(28) > Difference(32) + 0.025f,
            $"outer lens reverses local image: 8={Difference(8)}, 28={Difference(28)}, 32={Difference(32)}");
    }

    [Fact]
    public void Shaded_pixels_stay_valid_premultiplied_with_a_backdrop_and_a_stretched_drop()
    {
        var frame = Settled(OverlayMode.Listening, 2f, 0.2f);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        foreach (var texture in new[] { Solid(w, h, 1f, 1f, 1f), Solid(w, h, 0f, 0f, 0f), Solid(w, h, 1f, 0f, 0.4f) })
        {
            foreach (var p in Render(frame, texture, 1f))
            {
                var a = p >> 24;
                Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a);
            }
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void Caption_draws_only_translucent_glyphs_with_no_plate_or_dots(float dpi)
    {
        var frame = Settled(OverlayMode.Transcribing, 1.5f);
        var caption = DropLayout.CreateCaption(OverlayText.Transcribing, dpi);
        Assert.True(caption.Width > 40);
        var (w, h) = DropLayout.WindowSize(dpi, false, 0f);
        var white = Solid(w, h, 1f, 1f, 1f);
        var buffer = Render(frame, white, 1f, caption, dpi);
        var plain = Render(frame, white, 1f, null, dpi);
        var cy = h / 2f + (DropLayout.OrbRadius + 7f + 10.5f) * dpi;   // caption gap 7 + half height 10.5 dip (70% scale)
        var left = (int)MathF.Round(w / 2f - caption.Width / 2f);
        var top = (int)MathF.Round(cy - caption.Height / 2f);
        var bevel = Math.Max(1, (int)MathF.Round(dpi));
        var changed = 0;
        for (var y = top - 3; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (plain[i] == buffer[i]) continue;
                changed++;
                var tx = x - left;
                var ty = y - top;
                bool Covered(int yy) => tx >= 0 && tx < caption.Width && yy >= 0 && yy < caption.Height && caption.Coverage[yy * caption.Width + tx] > 0;
                Assert.True(Covered(ty) || Covered(ty - bevel), "no painted panel, padding or dots outside glyph coverage");
                Assert.InRange(buffer[i] >> 24, 1u, 235u);
            }
        Assert.True(changed > 100);
    }

    [Fact]
    public void Caption_fades_in_with_transcribing_and_fades_out_with_the_drop()
    {
        var caption = DropLayout.CreateCaption(OverlayText.Transcribing, 1f);
        var listening = Settled(OverlayMode.Listening, 1.5f);
        Assert.Equal(Render(listening, null, 0f), Render(listening, null, 0f, caption));
        var frame = Settled(OverlayMode.Transcribing, 1.5f);
        Assert.NotEqual(Render(frame, null, 0f), Render(frame, null, 0f, caption));
        var hidden = frame with { Opacity = 0f };
        Assert.All(Render(hidden, null, 0f, caption), p => Assert.Equal(0u, p));
    }

    [Fact]
    public void Rendering_with_a_backdrop_does_not_allocate_per_frame()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var buffer = new uint[w * h];
        var renderer = new DropRenderer();
        var texture = Solid(w, h, 0.3f, 0.6f, 0.9f);
        for (var i = 0; i < 60; i++)
        {
            animator.SetLevel(0.1f);
            animator.Tick(Step);
            renderer.Render(buffer, w, h, 1f, animator.Frame, null, 0f, null, texture, 1f);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            animator.SetLevel(0.1f);
            animator.Tick(Step);
            renderer.Render(buffer, w, h, 1f, animator.Frame, null, 0f, null, texture, 1f);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 2048, "no per-frame allocation");
    }

    [Fact]
    public void High_dpi_backdrop_path_renders_a_centered_drop()
    {
        var frame = Settled(OverlayMode.Listening, 1.5f, 0.15f);
        var (w, h) = DropLayout.WindowSize(2f, false, 0f);
        var texture = new BackdropTexture(8, 8) { OriginX = -80f, OriginY = -80f, PixelsPerTexelX = (w + 160f) / 8f, PixelsPerTexelY = (h + 160f) / 8f };
        texture.Fill(0.9f, 0.4f, 0.1f);
        var buffer = Render(frame, texture, 1f, null, 2f);
        Assert.Equal(0u, buffer[0]);
        Assert.True((buffer[(h / 2) * w + w / 2] >> 24) > 20);
    }

    [Fact]
    public void Real_screen_capture_reduces_to_a_small_valid_texture_or_reports_unavailable()
    {
        using var source = new ScreenBackdropSource();
        var region = new BackdropRegion(0, 0, 320, 240);
        var (tw, th) = BackdropCache.TextureSizeFor(region, 1f);
        var texture = new BackdropTexture(tw, th);
        if (!source.TryCapture(region, texture)) return;           // no interactive desktop in this session: graceful false is the contract

        Assert.True(texture.Width * texture.Height <= BackdropCache.MaxTexelsPerSide * BackdropCache.MaxTexelsPerSide);
        Assert.All(texture.Data, v => Assert.InRange(v, 0f, 1f));
        Assert.False(source.TryCapture(new BackdropRegion(0, 0, 0, 0), texture));
    }

    [Fact]
    public void Overlay_window_pulls_backdrop_frames_while_visible_and_forgets_them_when_hidden()
    {
        var source = new FuncBackdropSource((x, y) => 0xFF2060C0u);
        using var overlay = new OverlayWindowManager(source);
        overlay.ShowListening([0.2f, 0.4f, 0.3f, 0.5f]);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (source.Captures == 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.True(source.Captures > 0, "the overlay samples its backdrop while visible");

        overlay.Hide();
        Thread.Sleep(600);
        var afterHide = source.Captures;
        Thread.Sleep(300);
        Assert.Equal(afterHide, source.Captures);                      // no capture while hidden
    }

    [Fact]
    public void Overlay_window_without_backdrop_still_shows()
    {
        using var overlay = new OverlayWindowManager(null, useScreenBackdrop: false);
        overlay.ShowListening([0.2f, 0.4f]);
        Assert.True(overlay.IsVisible);
        overlay.ShowTranscribing();
        Thread.Sleep(150);
        overlay.Hide();
        Assert.False(overlay.IsVisible);
    }
}
