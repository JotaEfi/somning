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
    public void A_red_backdrop_tints_the_glass_and_the_shadow_red()
    {
        var frame = Settled(OverlayMode.Listening, 2f, 0.12f);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var plain = Render(frame, null, 0f);
        var red = Render(frame, Solid(w, h, 1f, 0.05f, 0.05f), 1f);
        var blue = Render(frame, Solid(w, h, 0.05f, 0.1f, 1f), 1f);

        static (double R, double B) Mean(uint[] b, int w, int h)
        {
            double r = 0, bl = 0, n = 0;
            for (var y = h / 2 - 30; y < h / 2 + 30; y++)
                for (var x = w / 2 - 50; x < w / 2 + 50; x++)
                {
                    var p = b[y * w + x];
                    var a = p >> 24;
                    if (a == 0) continue;
                    r += ((p >> 16) & 0xFF) / (double)a;
                    bl += (p & 0xFF) / (double)a;
                    n++;
                }

            return (r / n, bl / n);
        }

        var (pr, pb) = Mean(plain, w, h);
        var (rr, rb) = Mean(red, w, h);
        var (br, bb) = Mean(blue, w, h);
        Assert.True(rr - rb > pr - pb + 0.05, $"red backdrop should push red over blue: plain {pr - pb:F3}, red {rr - rb:F3}");
        Assert.True(bb - br > pb - pr + 0.05, $"blue backdrop should push blue over red: plain {pb - pr:F3}, blue {bb - br:F3}");
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

    [Fact]
    public void Caption_is_a_translucent_frosted_pill_that_stays_legible_on_a_white_desktop()
    {
        var frame = Settled(OverlayMode.Transcribing, 1.5f);
        var caption = DropLayout.CreateCaption(OverlayText.Transcribing, 1f);
        Assert.True(caption.Width > 40);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var white = Solid(w, h, 1f, 1f, 1f);
        var buffer = Render(frame, white, 1f, caption);
        var plain = Render(frame, white, 1f, null);
        Assert.NotEqual(plain, buffer);

        // Panel sample inside the left end of the pill, clear of the text.
        var pillY = (int)(h / 2f + DropLayout.OrbRadius + 14f + 15f);
        var pillLeft = (int)(w / 2f - ((caption.Width + 5f + 17.6f) / 2f + 17f) + 9f);
        var p = buffer[pillY * w + pillLeft];
        var a = (p >> 24) / 255f;
        Assert.InRange(a, 0.5f, 0.95f);                              // translucent glass, not solid
        var lum = (0.2126f * ((p >> 16) & 0xFF) + 0.7152f * ((p >> 8) & 0xFF) + 0.0722f * (p & 0xFF)) / 255f / a;
        Assert.True(lum < 0.55f, $"white text needs a darkened frost, got luminance {lum:F2}");

        // The same pill over a red desktop is red-tinted: the frost really samples what is behind it.
        var redPill = Render(frame, Solid(w, h, 0.9f, 0.1f, 0.1f), 1f, caption)[pillY * w + pillLeft];
        Assert.True(((redPill >> 16) & 0xFF) > (redPill & 0xFF) * 1.5f);
    }

    [Fact]
    public void Caption_fades_in_with_the_transcribing_state_and_not_while_listening()
    {
        var caption = DropLayout.CreateCaption(OverlayText.Transcribing, 1f);
        var listening = Render(Settled(OverlayMode.Listening, 1.5f), null, 0f, caption);
        var transcribing = Render(Settled(OverlayMode.Transcribing, 1.5f), null, 0f, caption);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var pillY = (int)(h / 2f + DropLayout.OrbRadius + 29f);
        Assert.True((listening[pillY * w + w / 2] >> 24) < 12);
        Assert.True((transcribing[pillY * w + w / 2 - caption.Width / 2 - 8] >> 24) > 100);
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
