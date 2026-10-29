using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class OverlayAnimationTests
{
    private const float Frame = 1f / 60f;

    private static void Run(OverlayAnimator animator, float seconds, float level = 0f)
    {
        for (var t = 0f; t < seconds; t += Frame)
        {
            animator.SetLevel(level);
            animator.Tick(Frame);
        }
    }

    [Fact]
    public void Spring_overshoots_when_underdamped_and_settles()
    {
        var spring = new Spring(0f);
        var peak = 0f;
        for (var i = 0; i < 30; i++)
        {
            spring.Step(1f, 18f, 0.4f, Frame);
            peak = Math.Max(peak, spring.Value);
        }

        for (var i = 0; i < 300; i++) spring.Step(1f, 18f, 0.4f, Frame);

        Assert.True(peak > 1.1f, $"expected elastic overshoot, peak {peak}");
        Assert.Equal(1f, spring.Value, 3);
    }

    [Fact]
    public void Spring_is_stable_for_large_frame_times()
    {
        var spring = new Spring(0f);
        for (var i = 0; i < 50; i++) spring.Step(1f, 30f, 0.3f, 0.1f);

        Assert.True(float.IsFinite(spring.Value));
        Assert.InRange(spring.Value, 0.9f, 1.1f);
    }

    [Fact]
    public void Normalizer_maps_silence_to_zero_and_quiet_or_loud_speech_to_a_wide_range()
    {
        var normalizer = new VoiceLevelNormalizer();

        Assert.Equal(0f, normalizer.Update(0.0005f, Frame));

        var quiet = 0f;
        for (var i = 0; i < 120; i++) quiet = normalizer.Update(0.03f, Frame);
        Assert.InRange(quiet, 0.85f, 1f);       // a quiet mic still reaches nearly the full range

        var loud = normalizer.Update(0.5f, Frame);
        Assert.Equal(1f, loud, 3);
    }

    [Fact]
    public void Normalizer_reference_decays_so_a_loud_burst_does_not_mute_later_speech()
    {
        var normalizer = new VoiceLevelNormalizer();
        normalizer.Update(0.9f, Frame);
        var afterBurst = normalizer.Update(0.03f, Frame);
        for (var i = 0; i < 60 * 12; i++) normalizer.Update(0.0f, Frame);
        var later = normalizer.Update(0.03f, Frame);

        Assert.True(later > afterBurst, $"later {later} should exceed {afterBurst}");
    }

    [Fact]
    public void Appear_starts_small_overshoots_then_settles_at_full_size()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        animator.Tick(Frame);
        Assert.True(animator.Frame.Scale < 0.3f, $"born small, got {animator.Frame.Scale}");

        var peak = 0f;
        for (var i = 0; i < 40; i++)
        {
            animator.Tick(Frame);
            peak = Math.Max(peak, animator.Frame.Scale);
        }

        Run(animator, 2f);

        Assert.True(peak > 1.05f, $"elastic overshoot expected, peak {peak}");
        Assert.Equal(1f, animator.Frame.Scale, 2);
        Assert.Equal(1f, animator.Frame.Opacity, 3);
    }

    [Fact]
    public void Hide_shrinks_dissolves_and_then_deactivates()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 1f);
        animator.Hide();
        Run(animator, 0.08f);

        Assert.True(animator.IsActive);
        Assert.InRange(animator.Frame.Opacity, 0.05f, 0.95f);
        Assert.True(animator.Frame.Scale < 1f);

        Run(animator, 0.5f);

        Assert.False(animator.IsActive);
    }

    [Fact]
    public void Voice_swells_and_deforms_the_drop_but_silence_keeps_it_calm()
    {
        var calm = new OverlayAnimator();
        calm.Show(OverlayMode.Listening);
        Run(calm, 2f, 0f);

        var loud = new OverlayAnimator();
        loud.Show(OverlayMode.Listening);
        Run(loud, 2f, 0.2f);

        Assert.True(loud.Frame.Level > 0.8f);
        Assert.True(calm.Frame.Level < 0.01f);
        Assert.True(loud.Frame.Radius > calm.Frame.Radius + 0.02f);
        Assert.True(loud.Frame.Elongation > calm.Frame.Elongation + 0.25f, "the drop stretches with the voice");
        Assert.True(loud.Frame.Time > calm.Frame.Time * 1.5f, "liquid flows faster while speaking");
    }

    [Theory]
    [InlineData(0.01f, 0.4f)]
    [InlineData(0.05f, 0.6f)]
    [InlineData(0.2f, 0.9f)]
    public void Realistic_microphone_rms_moves_the_drop_visibly(float rms, float minimumLevel)
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 2f, rms);

        Assert.True(animator.Frame.Level >= minimumLevel, $"rms {rms} only reached {animator.Frame.Level}");
    }

    [Theory]
    [InlineData(0.01f, 0.5f, 0.15f)]
    [InlineData(0.05f, 0.8f, 0.25f)]
    [InlineData(0.2f, 0.8f, 0.25f)]
    public void Speech_onset_reaches_the_waves_and_splash_within_100ms(float rms, float minimumLevel, float minimumElongation)
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 1f);
        for (var i = 0; i < 6; i++)
        {
            animator.SetLevel(rms);
            animator.Tick(Frame);
        }
        Assert.True(animator.Frame.Level > minimumLevel, $"level {animator.Frame.Level}");
        Assert.True(animator.Frame.Elongation > minimumElongation, $"splash {animator.Frame.Elongation}");
        Assert.True(animator.Frame.Ripple > 0.2f, $"ripple {animator.Frame.Ripple}");
    }

    [Theory]
    [InlineData(0.0005f)]
    [InlineData(0.002f)]
    [InlineData(0.003f)]
    public void Room_noise_barely_moves_the_drop(float rms)
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 2f, rms);

        Assert.True(animator.Frame.Level < 0.12f, $"room noise {rms} reached {animator.Frame.Level}");
    }

    [Fact]
    public void Loud_voice_keeps_the_shape_contained()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        var maxLobe = 0f;
        var maxStretch = 0f;
        for (var i = 0; i < 240; i++)
        {
            animator.SetLevel(i % 20 < 10 ? 0.3f : 0f);   // choppy speech, harsh onsets
            animator.Tick(Frame);
            var f = animator.Frame;
            maxLobe = Math.Max(maxLobe, MathF.Max(MathF.Sqrt(f.Lobe2X * f.Lobe2X + f.Lobe2Y * f.Lobe2Y), MathF.Sqrt(f.Lobe3X * f.Lobe3X + f.Lobe3Y * f.Lobe3Y)));
            maxStretch = Math.Max(maxStretch, MathF.Abs(f.Stretch));
        }

        Assert.True(maxLobe < 0.036f, $"lobes {maxLobe}");
        Assert.True(maxStretch <= 0.06f + 1e-4f, $"stretch {maxStretch}");
    }

    [Fact]
    public void Silence_still_breathes()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 1f);

        float min = float.MaxValue, max = float.MinValue;
        for (var i = 0; i < 60 * 3; i++)
        {
            animator.Tick(Frame);
            min = Math.Min(min, animator.Frame.Radius);
            max = Math.Max(max, animator.Frame.Radius);
        }

        Assert.True(max - min > 0.02f, $"breathing range {max - min}");
    }

    [Fact]
    public void Modes_crossfade_palette_and_notice_morphs_to_a_capsule()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        Run(animator, 0.5f);
        Assert.Equal(0f, animator.Frame.TranscribeMix, 2);

        animator.Show(OverlayMode.Transcribing);
        Run(animator, 1.5f);
        Assert.Equal(1f, animator.Frame.TranscribeMix, 2);

        animator.Show(OverlayMode.Notice);
        Run(animator, 1.5f);
        Assert.Equal(1f, animator.Frame.NoticeMix, 2);
        Assert.Equal(1f, animator.Frame.Morph, 2);
    }

    [Fact]
    public void Long_runs_with_extreme_input_stay_finite_and_bounded()
    {
        var animator = new OverlayAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 60 * 30; i++)
        {
            animator.SetLevel(i % 7 == 0 ? 1f : i % 3 == 0 ? 0f : 0.15f);
            animator.Tick(i % 50 == 0 ? 0.5f : Frame);
            var f = animator.Frame;
            Assert.True(float.IsFinite(f.Scale) && float.IsFinite(f.Radius) && float.IsFinite(f.Stretch) && float.IsFinite(f.Lobe2X));
            Assert.InRange(f.Scale, 0f, 1.4f);
            Assert.InRange(f.Radius, 0.8f, 1.3f);
            Assert.InRange(f.Stretch, -0.12f, 0.12f);
        }
    }

    [Fact]
    public void LevelFromBars_inverts_the_tracker_waveform()
    {
        var tracker = new AudioLevelTracker();
        Assert.Equal(0f, AudioLevelTracker.LevelFromBars(tracker.GenerateWaveformBars(5, 0f)), 3);
        Assert.Equal(0.4f, AudioLevelTracker.LevelFromBars(tracker.GenerateWaveformBars(5, 0.4f)), 3);
        Assert.Equal(0f, AudioLevelTracker.LevelFromBars([]));
    }

    [Fact]
    public void Palette_blend_hits_each_state_at_the_extremes()
    {
        var listening = new float[DropPalette.Length];
        var transcribing = new float[DropPalette.Length];
        var notice = new float[DropPalette.Length];
        DropPalette.Blend(listening, 0f, 0f);
        DropPalette.Blend(transcribing, 1f, 0f);
        DropPalette.Blend(notice, 0f, 1f);

        Assert.NotEqual(listening, transcribing);
        Assert.True(notice[0] > 0.9f && notice[2] < 0.2f);   // amber: strong red, weak blue
    }
}
