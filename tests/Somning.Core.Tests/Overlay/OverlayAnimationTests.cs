using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class OverlayAnimationTests
{
    private const float Frame = 1f / 60f;

    private static void Run(OverlayAnimator animator, float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame) animator.Tick(Frame);
    }

    [Fact]
    public void Smoother_attack_is_faster_than_release()
    {
        var smoother = new BarSmoother(1);
        smoother.Reset(0.2f);

        smoother.Update([1f], Frame);
        var risen = smoother.Values[0] - 0.2f;

        smoother.Reset(1f);
        smoother.Update([0.2f], Frame);
        var fallen = 1f - smoother.Values[0];

        Assert.True(risen > 0.2f, $"attack should move quickly, moved {risen}");
        Assert.True(fallen < risen / 2f, $"release should be slower: rise {risen}, fall {fallen}");
    }

    [Fact]
    public void Smoother_is_frame_rate_independent()
    {
        var fine = new BarSmoother(1);
        var coarse = new BarSmoother(1);
        for (var i = 0; i < 6; i++) fine.Update([1f], 1f / 120f);
        for (var i = 0; i < 3; i++) coarse.Update([1f], 1f / 60f);

        Assert.Equal(fine.Values[0], coarse.Values[0], 4);
    }

    [Fact]
    public void Smoother_converges_and_stays_in_range()
    {
        var smoother = new BarSmoother(3);
        for (var i = 0; i < 300; i++) smoother.Update([2f, -1f, 0.5f], Frame);

        Assert.Equal(1f, smoother.Values[0], 3);
        Assert.Equal(0f, smoother.Values[1], 3);
        Assert.Equal(0.5f, smoother.Values[2], 3);
    }

    [Fact]
    public void Smoother_ignores_non_positive_delta()
    {
        var smoother = new BarSmoother(1);
        smoother.Reset(0.3f);
        smoother.Update([1f], 0f);
        smoother.Update([1f], -1f);

        Assert.Equal(0.3f, smoother.Values[0]);
    }

    [Fact]
    public void Transition_fades_in_and_out_with_scale()
    {
        var transition = new OverlayTransition();
        Assert.True(transition.IsHidden);

        transition.Show();
        transition.Advance(OverlayTransition.EnterSeconds / 2f);
        Assert.InRange(transition.Opacity, 0.01f, 0.99f);
        Assert.InRange(transition.Scale, OverlayTransition.MinScale, 1f);

        transition.Advance(1f);
        Assert.Equal(1f, transition.Opacity);
        Assert.Equal(1f, transition.Scale);
        Assert.False(transition.IsAnimating);

        transition.Hide();
        Assert.False(transition.IsHidden);
        transition.Advance(1f);
        Assert.True(transition.IsHidden);
        Assert.Equal(0f, transition.Opacity);
        Assert.Equal(OverlayTransition.MinScale, transition.Scale, 4);
    }

    [Fact]
    public void Listening_in_silence_still_moves()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars([0.15f, 0.15f, 0.15f, 0.15f, 0.15f]);

        float min = 1f, max = 0f;
        var pulseMin = 1f;
        var pulseMax = 0f;
        for (var t = 0f; t < 3f; t += Frame)
        {
            animator.Tick(Frame);
            var bar = animator.Bars[2];
            min = Math.Min(min, bar);
            max = Math.Max(max, bar);
            pulseMin = Math.Min(pulseMin, animator.Frame.Pulse);
            pulseMax = Math.Max(pulseMax, animator.Frame.Pulse);
        }

        Assert.True(max - min > 0.05f, "idle bars should breathe");
        Assert.True(max <= OverlayWaves.IdleBase + OverlayWaves.IdleAmplitude + 0.001f, "idle must stay subtle");
        Assert.True(pulseMax - pulseMin > 0.8f, "pulse should sweep nearly 0..1");
    }

    [Fact]
    public void Listening_bars_follow_volume_rise_fast_and_fall_slow()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 0.5f);

        animator.SetLiveBars([0.9f, 0.9f, 0.9f, 0.9f, 0.9f]);
        Run(animator, 0.15f);
        var loud = animator.Bars[2];
        Assert.True(loud > 0.7f, $"bars should reach speech level quickly, got {loud}");

        animator.SetLiveBars([0.15f, 0.15f, 0.15f, 0.15f, 0.15f]);
        Run(animator, 0.1f);
        var afterShortSilence = animator.Bars[2];
        Assert.True(afterShortSilence > 0.4f, $"bars should fall slowly, got {afterShortSilence}");

        Run(animator, 2f);
        Assert.True(animator.Bars[2] < 0.35f, "bars should eventually settle");
    }

    [Fact]
    public void Bars_stay_within_unit_range()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars([1f, 1f, 1f, 1f, 1f]);
        Run(animator, 2f);

        foreach (var bar in animator.Bars) Assert.InRange(bar, 0f, 1f);
        Assert.InRange(animator.Frame.Energy, 0.5f, 1f);
    }

    [Fact]
    public void Transcribing_uses_shimmer_independent_of_live_bars()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars([1f, 1f, 1f, 1f, 1f]);
        Run(animator, 0.5f);

        animator.Show(OverlayMode.Transcribing);
        Run(animator, 0.6f);

        Assert.Equal(OverlayMode.Transcribing, animator.Frame.Mode);
        Assert.Equal(0f, animator.Frame.Energy);
        var bars = animator.Bars.ToArray();
        Assert.True(bars.Max() - bars.Min() > 0.05f, "shimmer should give bars different heights (a wave)");
        Assert.All(bars, b => Assert.InRange(b, OverlayWaves.ShimmerMin - 0.05f, OverlayWaves.ShimmerMax + 0.05f));
    }

    [Fact]
    public void Shimmer_travels_across_bars_over_time()
    {
        var early = Enumerable.Range(0, 5).Select(i => OverlayWaves.Shimmer(i, 5, 0f)).ToArray();
        var later = Enumerable.Range(0, 5).Select(i => OverlayWaves.Shimmer(i, 5, 0.3f)).ToArray();

        Assert.NotEqual(early, later);
        Assert.All(early.Concat(later), v => Assert.InRange(v, OverlayWaves.ShimmerMin, OverlayWaves.ShimmerMax));
    }

    [Fact]
    public void Hide_fades_out_keeps_mode_then_deactivates()
    {
        var animator = new OverlayAnimator();
        Assert.False(animator.IsActive);

        animator.Show(OverlayMode.Notice);
        Run(animator, 0.3f);
        Assert.True(animator.IsActive);
        Assert.Equal(1f, animator.Frame.Opacity);

        animator.Hide();
        animator.Tick(OverlayTransition.ExitSeconds / 2f);
        Assert.True(animator.IsActive);
        Assert.Equal(OverlayMode.Notice, animator.Frame.Mode);
        Assert.InRange(animator.Frame.Opacity, 0.01f, 0.99f);

        var stillActive = animator.Tick(1f);
        Assert.False(stillActive);
        Assert.False(animator.IsActive);
        Assert.Equal(0f, animator.Frame.Opacity);
    }

    [Fact]
    public void Showing_again_after_full_hide_resets_bars_to_rest()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars([1f, 1f, 1f, 1f, 1f]);
        Run(animator, 1f);
        animator.Hide();
        Run(animator, 1f);
        Assert.False(animator.IsActive);

        animator.Show(OverlayMode.Listening);

        Assert.All(animator.Bars.ToArray(), b => Assert.Equal(AudioLevelTracker.MinimumBarHeight, b, 4));
    }

    [Fact]
    public void Showing_during_fade_out_keeps_animation_continuous()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Notice);
        Run(animator, 0.3f);
        animator.Hide();
        animator.Tick(OverlayTransition.ExitSeconds / 2f);
        var midOpacity = animator.Frame.Opacity;

        animator.Show(OverlayMode.Listening);
        animator.Tick(Frame);

        Assert.Equal(OverlayMode.Listening, animator.Frame.Mode);
        Assert.True(animator.Frame.Opacity > 0f);
        Assert.True(midOpacity < 1f);
    }

    [Fact]
    public void Bar_count_follows_live_bars()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLiveBars(new float[9]);

        Assert.Equal(9, animator.Bars.Length);
    }

    [Fact]
    public void Tick_clamps_huge_deltas()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.Tick(30f);

        Assert.True(animator.Frame.Opacity < 1f, "a stall must not skip the whole entrance");
    }

    [Fact]
    public void Static_frame_is_neutral()
    {
        var frame = OverlayFrame.Static(OverlayMode.Listening);

        Assert.Equal(1f, frame.Opacity);
        Assert.Equal(1f, frame.Scale);
        Assert.Equal(0f, frame.Pulse);
        Assert.Equal(0f, frame.Energy);
    }
}
