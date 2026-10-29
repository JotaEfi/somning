using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class DropEmergenceTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void Starts_hidden_below_the_edge_with_no_mound()
    {
        var s = DropEmergence.Evaluate(0f);
        Assert.True(s.Active);
        Assert.True(s.Rise < 0f, $"rise {s.Rise}");
        Assert.Equal(0f, s.Pool, 3);
        Assert.Equal(0f, s.VoiceGate);
    }

    [Fact]
    public void Neck_thins_monotonically_then_snaps_and_tails_retract()
    {
        var previous = float.MaxValue;
        for (var t = 0.1f; t < DropEmergence.BreakSeconds; t += 0.02f)
        {
            var neck = DropEmergence.Evaluate(t).Neck;
            Assert.True(neck <= previous + 1e-5f, $"neck grew at {t}");
            previous = neck;
        }

        var snapped = DropEmergence.Evaluate(DropEmergence.BreakSeconds + 0.01f);
        Assert.Equal(0f, snapped.Neck);
        Assert.True(snapped.TopTail < 1f && snapped.TopTail > 0f);
        var later = DropEmergence.Evaluate(DropEmergence.BreakSeconds + 0.2f);
        Assert.True(later.TopTail < 0.01f && later.BottomTail < 0.05f && later.Pool < 0.01f);
    }

    [Fact]
    public void Drop_rises_overshoots_and_ends_exactly_at_rest_within_700ms()
    {
        var peak = 0f;
        for (var t = 0f; t < DropEmergence.DurationSeconds; t += Frame)
            peak = Math.Max(peak, DropEmergence.Evaluate(t).Rise);

        Assert.True(peak > 1.01f, $"elastic overshoot expected, peak {peak}");
        Assert.InRange(DropEmergence.DurationSeconds, 0.45f, 0.7f);
        Assert.Equal(EmergeShape.Rest, DropEmergence.Evaluate(DropEmergence.DurationSeconds));
    }

    [Fact]
    public void Shape_is_continuous_across_the_snap()
    {
        var before = DropEmergence.Evaluate(DropEmergence.BreakSeconds - 1e-4f);
        var after = DropEmergence.Evaluate(DropEmergence.BreakSeconds + 1e-4f);
        Assert.Equal(before.Rise, after.Rise, 2);
        Assert.Equal(before.Squash, after.Squash, 2);
        Assert.Equal(before.Pool, after.Pool, 2);
        Assert.True(before.Neck < 0.01f);
    }

    [Fact]
    public void Values_stay_finite_and_bounded_for_any_time()
    {
        foreach (var t in new[] { -1f, 0f, 0.001f, 0.2f, 0.33f, 0.5f, 0.63f, 5f, float.NaN })
        {
            var s = DropEmergence.Evaluate(t);
            Assert.True(float.IsFinite(s.Rise) && float.IsFinite(s.Neck) && float.IsFinite(s.Squash));
            Assert.InRange(s.Rise, -0.2f, 1.15f);
            Assert.InRange(s.Size, 0.3f, 1f);
            Assert.InRange(s.Goo, 0f, 1f);
        }
    }

    [Fact]
    public void Animator_emerges_from_the_edge_and_holds_voice_until_settled()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLevel(0.3f);
        animator.Tick(Frame);
        Assert.True(animator.Frame.IsEmerging);
        Assert.True(animator.Frame.Rise < 0.2f);
        Assert.Equal(0f, animator.Frame.Level, 3);   // the audio does not deform the goo mid-pull
        Assert.False(animator.IsCalm);

        for (var t = 0f; t < 0.8f; t += Frame) animator.Tick(Frame);
        Assert.False(animator.Frame.IsEmerging);
        Assert.Equal(1f, animator.Frame.Rise);
        Assert.Equal(1f, animator.Frame.Scale, 2);
    }

    [Fact]
    public void Notices_keep_the_pop_in_without_goo()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Notice);
        animator.Tick(Frame);
        Assert.False(animator.Frame.IsEmerging);
    }
}
