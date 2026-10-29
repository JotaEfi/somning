using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>Headless pixel-buffer checks for the glass drop (no window, no focus, no input).</summary>
public sealed class OverlayAnimatedRenderTests
{
    private const float Frame = 1f / 60f;

    private static uint[] Render(DropFrame frame, float dpi = 1f, TextMask? text = null)
    {
        var (w, h) = DropLayout.WindowSize(dpi, frame.Morph > 0.01f, text?.Width ?? 0);
        var buffer = new uint[w * h];
        new DropRenderer().Render(buffer, w, h, dpi, in frame, text, DropLayout.NoticeHalfWidth(text?.Width ?? 0, dpi));
        return buffer;
    }

    private static DropFrame Settled(OverlayMode mode, float seconds, float level = 0f)
    {
        var animator = new OverlayAnimator();
        animator.Show(mode);
        for (var t = 0f; t < seconds; t += Frame)
        {
            animator.SetLevel(level);
            animator.Tick(Frame);
        }

        return animator.Frame;
    }

    [Fact]
    public void Pixels_are_valid_premultiplied_argb()
    {
        var buffer = Render(Settled(OverlayMode.Listening, 1.5f, 0.15f));

        var painted = 0;
        foreach (var p in buffer)
        {
            var a = p >> 24;
            Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a, "color must not exceed alpha");
            if (a > 0) painted++;
        }

        Assert.True(painted > 5000, $"drop and glow should cover many pixels, got {painted}");
    }

    [Fact]
    public void Drop_is_translucent_inside_and_denser_at_the_rim()
    {
        var frame = Settled(OverlayMode.Listening, 2f);
        var buffer = Render(frame);
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);

        var center = buffer[(h / 2 - 14) * w + w / 2] >> 24;      // above the liquid band
        var nearRim = buffer[(h / 2) * w + w / 2 + (int)(DropLayout.OrbRadius * frame.Radius * frame.Scale) - 3] >> 24;
        Assert.InRange(center, 30u, 200u);
        Assert.True(nearRim > center, $"rim {nearRim} should be denser than center {center}");
    }

    [Fact]
    public void Voice_makes_the_drop_larger_and_changes_the_picture()
    {
        var calm = Render(Settled(OverlayMode.Listening, 2f, 0f));
        var loud = Render(Settled(OverlayMode.Listening, 2f, 0.2f));

        static int Coverage(uint[] b) => b.Count(p => (p >> 24) > 128);
        Assert.True(Coverage(loud) > Coverage(calm), "loud voice swells the drop");
        Assert.NotEqual(calm, loud);
    }

    [Fact]
    public void Entrance_frames_grow_from_a_small_drop()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 6; i++) animator.Tick(Frame);
        var early = Render(animator.Frame);
        for (var i = 0; i < 12; i++) animator.Tick(Frame);
        var later = Render(animator.Frame);

        static int Solid(uint[] b) => b.Count(p => (p >> 24) > 100);
        Assert.True(Solid(early) < Solid(later));
    }

    [Fact]
    public void Entrance_draws_a_filament_from_the_screen_edge_then_floats_free()
    {
        var (w, baseH) = DropLayout.WindowSize(1f, false, 0f);
        const int reach = 60;
        var h = baseH + reach;
        var renderer = new DropRenderer();
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        var buffer = new uint[w * h];

        int EdgeAlpha() => Enumerable.Range(w / 2 - 10, 20).Max(x => (int)(buffer[(h - 2) * w + x] >> 24));
        int NeckRow(int y) => Enumerable.Range(0, w).Count(x => (buffer[y * w + x] >> 24) > 100);

        for (var t = 0f; t < 0.25f; t += Frame) animator.Tick(Frame);
        Assert.True(animator.Frame.IsEmerging);
        renderer.Render(buffer, w, h, 1f, animator.Frame, restCenterY: baseH / 2f, edgeY: h);
        Assert.True(EdgeAlpha() > 40, $"goo attached to the screen edge, alpha {EdgeAlpha()}");
        Assert.All(buffer, p => Assert.True(((p >> 16) & 0xFF) <= p >> 24));

        for (var t = 0f; t < 1f; t += Frame) animator.Tick(Frame);
        renderer.Render(buffer, w, h, 1f, animator.Frame, restCenterY: baseH / 2f, edgeY: h);
        Assert.True(EdgeAlpha() < 10, "detached after the pinch-off");
        Assert.True(NeckRow(baseH / 2) > 60, "drop at its resting place");
    }

    [Fact]
    public void Zero_opacity_draws_nothing()
    {
        var frame = Settled(OverlayMode.Listening, 1f) with { Opacity = 0f };

        Assert.All(Render(frame), p => Assert.Equal(0u, p));
    }

    [Fact]
    public void Notice_draws_a_wide_capsule_with_readable_text()
    {
        var text = TextMask.Create(OverlayText.For(OverlayNoticeKind.MicBlocked), 15);
        Assert.True(text.Width > 40);

        var frame = Settled(OverlayMode.Notice, 1.5f);
        var withText = Render(frame, 1f, text);
        var withoutText = Render(frame, 1f, TextMask.Empty);
        var (w, h) = DropLayout.WindowSize(1f, true, text.Width);

        Assert.True(w > h);
        Assert.NotEqual(withoutText, withText);
        var edge = withText[(h / 2) * w + (w - text.Width) / 2 - 8] >> 24;   // capsule extends beyond the text
        Assert.True(edge > 40, $"capsule edge alpha {edge}");
    }

    [Fact]
    public void High_dpi_uses_the_upscaled_path_and_still_renders_a_centered_drop()
    {
        var frame = Settled(OverlayMode.Listening, 1.5f, 0.12f);
        var scaled = Render(frame, 2f);
        var (w, h) = DropLayout.WindowSize(2f, false, 0f);

        Assert.Equal(w * h, scaled.Length);
        Assert.Equal(0u, scaled[0]);
        Assert.True((scaled[(h / 2) * w + w / 2] >> 24) > 20);
    }

    [Fact]
    public void Animator_frames_render_through_a_full_cycle_without_exceptions()
    {
        var animator = new OverlayAnimator();
        var (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var buffer = new uint[w * h];
        var renderer = new DropRenderer();
        animator.Show(OverlayMode.Listening);

        for (var i = 0; i < 120; i++)
        {
            animator.SetLevel(0.1f);
            animator.Tick(Frame);
            renderer.Render(buffer, w, h, 1f, animator.Frame);
        }

        animator.Show(OverlayMode.Transcribing);
        animator.Hide();
        for (var i = 0; i < 40; i++)
        {
            animator.Tick(Frame);
            renderer.Render(buffer, w, h, 1f, animator.Frame);
        }

        Assert.False(animator.IsActive);
    }
}
