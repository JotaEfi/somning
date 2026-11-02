using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class DropWaveTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void A_voice_onset_launches_a_wave_from_the_rim()
    {
        var stretch = new DropStretch();
        for (var i = 0; i < 3; i++) stretch.Update(0.9f, 0f, Frame, 0.5f);

        Assert.True(stretch.Wave0Amp > 0f || stretch.Wave1Amp > 0f, "onset wave");
        Assert.InRange(MathF.Max(stretch.Wave0Front, stretch.Wave1Front), 0f, 0.3f);
    }

    [Fact]
    public void Waves_travel_inward_and_die_out()
    {
        var stretch = new DropStretch();
        for (var i = 0; i < 3; i++) stretch.Update(0.9f, 0f, Frame, 0.5f);
        var front = stretch.Wave0Front;
        for (var i = 0; i < 6; i++) stretch.Update(0.9f, 0f, Frame, 0.5f);
        Assert.True(stretch.Wave0Amp <= 0f || stretch.Wave0Front > front, "front moves toward the center");

        for (var i = 0; i < 60; i++) stretch.Update(0.9f, 0f, Frame, 0.5f);
        Assert.Equal(0f, stretch.Wave0Amp);
        Assert.Equal(0f, stretch.Wave1Amp);
    }

    [Fact]
    public void High_tones_slide_right_and_low_tones_slide_left()
    {
        var high = new DropStretch();
        var low = new DropStretch();
        for (var i = 0; i < 90; i++)
        {
            high.Update(0.6f, 0f, Frame, 0.95f);
            low.Update(0.6f, 0f, Frame, 0.05f);
        }

        Assert.True(high.Slosh > 0.2f, $"high {high.Slosh}");
        Assert.True(low.Slosh < -0.2f, $"low {low.Slosh}");
        Assert.InRange(high.Lean, -DropStretch.MaxLean, DropStretch.MaxLean);
    }

    [Fact]
    public void Animator_frame_carries_waves_and_slosh_while_listening()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 60; i++) animator.Tick(Frame);
        var sawWave = false;
        for (var i = 0; i < 30; i++)
        {
            animator.SetLevel(i % 10 < 5 ? 0.25f : 0f);
            animator.SetTone(0.95f);
            animator.Tick(Frame);
            sawWave |= animator.Frame.Wave0Amp > 0f || animator.Frame.Wave1Amp > 0f;
        }

        Assert.True(sawWave, "onsets reach the renderer");
        Assert.NotEqual(0f, animator.Frame.Slosh);
    }
}
