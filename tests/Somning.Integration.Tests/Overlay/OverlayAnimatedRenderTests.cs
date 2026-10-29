using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>Headless pixel-buffer checks for the animated pill (no window, no focus, no input).</summary>
public sealed class OverlayAnimatedRenderTests
{
    private const int Width = 220;
    private const int Height = 44;

    private static uint[] Render(OverlayMode mode, OverlayFrame frame, float[] bars)
    {
        var buffer = new uint[Width * Height];
        var model = mode == OverlayMode.Transcribing ? OverlayViewModel.CreateTranscribing() : OverlayViewModel.CreateListening(bars);
        OverlayWindowManager.RenderToBuffer(buffer, Width, Height, 1f, model, frame, bars);
        return buffer;
    }

    [Fact]
    public void Entrance_scale_shrinks_the_pill_but_keeps_it_centered_and_transparent_at_edges()
    {
        var frame = new OverlayFrame(OverlayMode.Listening, 0.5f, OverlayTransition.MinScale, 0f, 0f);
        var buffer = Render(OverlayMode.Listening, frame, [0.2f, 0.4f, 0.6f, 0.4f, 0.2f]);

        Assert.Equal(0u, buffer[(Height / 2) * Width]);                 // left edge now outside the shrunken pill
        Assert.True((buffer[(Height / 2) * Width + Width / 2] >> 24) > 200);
    }

    [Fact]
    public void Pulse_changes_the_glow_around_the_dot_without_moving_the_dot()
    {
        float[] bars = [0.2f, 0.4f, 0.6f, 0.4f, 0.2f];
        var calm = Render(OverlayMode.Listening, new OverlayFrame(OverlayMode.Listening, 1f, 1f, 0f, 0f), bars);
        var glowing = Render(OverlayMode.Listening, new OverlayFrame(OverlayMode.Listening, 1f, 1f, 1f, 0.6f), bars);

        var dot = (Height / 2) * Width + 20;
        Assert.Equal(calm[dot], glowing[dot]);                          // dot core identical
        var halo = (Height / 2) * Width + 20 + 8;
        Assert.NotEqual(calm[halo], glowing[halo]);                     // halo ring differs
    }

    [Fact]
    public void Transcribing_draws_bars_with_the_cyan_dot()
    {
        float[] shimmer = [0.3f, 0.6f, 0.85f, 0.6f, 0.3f];
        var frame = new OverlayFrame(OverlayMode.Transcribing, 1f, 1f, 0.5f, 0f);
        var buffer = Render(OverlayMode.Transcribing, frame, shimmer);

        var dotPixel = buffer[(Height / 2) * Width + 20];
        Assert.Equal(56u, (dotPixel >> 16) & 0xFF);

        // The middle bar (tallest) must paint above the pill's vertical center line.
        var barX = (int)(Width - 24 - (5 * 3 + 4 * 3) + 2 * 6 + 1);
        var top = buffer[(Height / 2 - 8) * Width + barX];
        var background = buffer[(Height / 2 - 8) * Width + barX - 12];
        Assert.NotEqual(background, top);
    }

    [Fact]
    public void Animator_frames_render_without_exceptions_through_a_full_cycle()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars([0.5f, 0.7f, 0.9f, 0.7f, 0.5f]);
        var buffer = new uint[Width * Height];
        var model = OverlayViewModel.CreateListening([0.5f, 0.7f, 0.9f, 0.7f, 0.5f]);

        for (var i = 0; i < 120; i++)
        {
            animator.Tick(1f / 60f);
            OverlayWindowManager.RenderToBuffer(buffer, Width, Height, 1f, model, animator.Frame, animator.Bars);
        }

        animator.Show(OverlayMode.Transcribing);
        animator.Hide();
        for (var i = 0; i < 30; i++)
        {
            animator.Tick(1f / 60f);
            OverlayWindowManager.RenderToBuffer(buffer, Width, Height, 1f, model, animator.Frame, animator.Bars);
        }

        Assert.False(animator.IsActive);
    }
}
