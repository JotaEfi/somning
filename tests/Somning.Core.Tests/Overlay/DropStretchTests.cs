using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class DropStretchTests
{
    private const float Frame = 1f / 60f;

    private static DropStretch Drive(float drive, float seconds)
    {
        var stretch = new DropStretch();
        for (var t = 0f; t < seconds; t += Frame) stretch.Update(drive, 0f, Frame);
        return stretch;
    }

    [Fact]
    public void Silence_relaxes_to_a_circle()
    {
        var stretch = Drive(0f, 4f);
        Assert.InRange(stretch.Elongation, -0.03f, 0.03f);
        Assert.InRange(stretch.Lean, -0.02f, 0.02f);
    }

    [Fact]
    public void Sustained_loud_voice_elongates_the_drop_within_bounds()
    {
        var stretch = Drive(1f, 3f);
        Assert.InRange(stretch.Elongation, 0.30f, DropStretch.MaxElongation * 1.12f);
    }

    [Fact]
    public void Elongation_grows_with_the_voice_level()
    {
        var quiet = Drive(0.25f, 3f).Elongation;
        var medium = Drive(0.55f, 3f).Elongation;
        var loud = Drive(1f, 3f).Elongation;
        Assert.True(quiet < medium && medium < loud, $"{quiet} < {medium} < {loud}");
    }

    [Fact]
    public void Attack_is_faster_than_release()
    {
        var stretch = new DropStretch();
        var rise = 0;
        while (stretch.Elongation < 0.2f && rise < 600)
        {
            stretch.Update(1f, 0f, Frame);
            rise++;
        }

        for (var i = 0; i < 180; i++) stretch.Update(1f, 0f, Frame);
        var peak = stretch.Elongation;
        var fall = 0;
        while (stretch.Elongation > peak * 0.5f && fall < 600)
        {
            stretch.Update(0f, 0f, Frame);
            fall++;
        }

        Assert.True(rise < fall, $"rise {rise} frames, fall {fall} frames");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.2f)]
    [InlineData(0.5f)]
    [InlineData(0.56f)]
    public void Squash_and_stretch_preserve_volume(float elongation)
    {
        var (width, height) = DropStretch.Scales(elongation);
        Assert.Equal(1f, width * height * height, 4);
    }

    [Fact]
    public void Phoneme_onsets_raise_ripple_and_it_decays()
    {
        var stretch = Drive(0f, 1f);
        Assert.True(stretch.Ripple < 0.05f);
        var peak = 0f;
        for (var i = 0; i < 6; i++)
        {
            stretch.Update(1f, 0f, Frame);
            peak = Math.Max(peak, stretch.Ripple);
        }

        Assert.True(peak > 0.2f, $"ripple {peak}");
        for (var i = 0; i < 300; i++) stretch.Update(0f, 0f, Frame);
        Assert.True(stretch.Ripple < 0.05f);
    }

    [Fact]
    public void Chaotic_input_stays_finite_and_bounded()
    {
        var stretch = new DropStretch();
        var random = new Random(7);
        for (var i = 0; i < 4000; i++)
        {
            stretch.Update((float)random.NextDouble(), (float)random.NextDouble(), 0.001f + (float)random.NextDouble() * 0.1f);
            Assert.True(float.IsFinite(stretch.Elongation) && float.IsFinite(stretch.Lean) && float.IsFinite(stretch.Ripple));
            Assert.InRange(stretch.Elongation, -0.05f, DropStretch.MaxElongation * 1.13f);
            Assert.InRange(stretch.Lean, -DropStretch.MaxLean - 1e-4f, DropStretch.MaxLean + 1e-4f);
        }
    }

    [Fact]
    public void Animator_frames_carry_the_elongation_and_stay_round_in_notices()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        for (var t = 0f; t < 2f; t += Frame)
        {
            animator.SetLevel(0.2f);
            animator.Tick(Frame);
        }

        Assert.True(animator.Frame.Elongation > 0.25f, $"{animator.Frame.Elongation}");

        animator.Show(OverlayMode.Notice);
        for (var t = 0f; t < 1.5f; t += Frame) animator.Tick(Frame);
        Assert.True(animator.Frame.Elongation < 0.02f, $"{animator.Frame.Elongation}");
    }

    [Fact]
    public void Calm_is_reported_only_when_settled_and_quiet()
    {
        var animator = new OverlayAnimator();
        Assert.False(animator.IsCalm);
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 6; i++) animator.Tick(Frame);
        Assert.False(animator.IsCalm);                 // still popping in

        for (var t = 0f; t < 2.5f; t += Frame) animator.Tick(Frame);
        Assert.True(animator.IsCalm);

        for (var t = 0f; t < 0.5f; t += Frame)
        {
            animator.SetLevel(0.2f);
            animator.Tick(Frame);
        }

        Assert.False(animator.IsCalm);                 // speaking needs the full frame rate
        animator.Hide();
        animator.Tick(Frame);
        Assert.False(animator.IsCalm);
    }
}
