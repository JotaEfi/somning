using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class VoiceLevelNormalizerTests
{
    private const float Dt = 0.03f;

    private static float Settle(VoiceLevelNormalizer normalizer, float rms, int buffers = 40)
    {
        var level = 0f;
        for (var i = 0; i < buffers; i++) level = normalizer.Normalize(rms, Dt);
        return level;
    }

    [Theory]
    [InlineData(0.01f, 0.30f)]  // quiet speech, -40 dBFS
    [InlineData(0.05f, 0.65f)]  // normal speech, -26 dBFS
    [InlineData(0.20f, 0.90f)]  // loud speech, -14 dBFS
    public void Realistic_microphone_speech_moves_the_level_visibly(float rms, float minimum)
    {
        var level = Settle(new VoiceLevelNormalizer(), rms);

        Assert.True(level >= minimum, $"rms {rms} produced level {level}, expected >= {minimum}");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.0005f)]
    [InlineData(0.002f)]
    public void Room_noise_and_silence_stay_near_zero(float rms)
    {
        Assert.True(Settle(new VoiceLevelNormalizer(), rms) < 0.15f);
    }

    [Fact]
    public void Level_rises_monotonically_with_loudness()
    {
        var previous = -1f;
        foreach (var rms in new[] { 0.003f, 0.01f, 0.03f, 0.08f, 0.2f })
        {
            var level = Settle(new VoiceLevelNormalizer(), rms);
            Assert.True(rms >= 0.2f ? level >= previous : level > previous, $"rms {rms}: {level} <= {previous}");
            previous = level;
        }
    }

    [Fact]
    public void A_consistently_quiet_speaker_still_reaches_most_of_the_range()
    {
        var normalizer = new VoiceLevelNormalizer();
        Settle(normalizer, 0.002f, 20);                  // room noise first
        var level = Settle(normalizer, 0.012f, 400);     // then ~12 s of quiet speech

        Assert.True(level > 0.5f, $"quiet speaker level {level}");
    }

    [Fact]
    public void Noise_floor_does_not_chase_sustained_speech()
    {
        var normalizer = new VoiceLevelNormalizer();
        Settle(normalizer, 0.05f, 1000); // 30 s of continuous speech

        Assert.True(normalizer.FloorDb <= VoiceLevelNormalizer.MaxFloorDb);
        Assert.True(Settle(normalizer, 0.05f, 5) > 0.5f);
    }

    [Fact]
    public void Output_is_always_within_unit_range_even_for_extreme_input()
    {
        var normalizer = new VoiceLevelNormalizer();
        foreach (var rms in new[] { 0f, 1f, 5f, float.Epsilon, 0.5f })
        {
            var level = normalizer.Normalize(rms, Dt);
            Assert.InRange(level, 0f, 1f);
        }
    }
}

public sealed class VoiceToOrbPipelineTests
{
    private const float Dt = 1f / 60f;

    /// <summary>Same chain the app uses: PCM -> RMS -> normalizer -> tracker -> bars -> level -> animator -> shape.</summary>
    private static (OrbAnimator Animator, OrbShape Shape) Drive(float rms, float seconds)
    {
        var normalizer = new VoiceLevelNormalizer();
        var tracker = new AudioLevelTracker();
        var animator = new OrbAnimator();
        var shape = new OrbShape();
        animator.Show(OverlayMode.Listening);

        var steps = (int)(seconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            if (i % 2 == 0) // ~30 audio buffers per second
            {
                var level = tracker.Update(normalizer.Normalize(rms, 2 * Dt));
                animator.SetLevel(AudioLevelTracker.LevelFromBars(tracker.GenerateWaveformBars(5, level)));
            }

            animator.Tick(Dt);
        }

        shape.Update(animator.Frame);
        return (animator, shape);
    }

    [Fact]
    public void Root_cause_of_dead_bars_raw_rms_through_the_old_linear_mapping_is_invisible()
    {
        // Pre-fix chain: tracker.Update(rawRms) then bars at 0.15 + 0.85 * level. Idle ripple alone reaches 0.27.
        var tracker = new AudioLevelTracker();
        for (var i = 0; i < 50; i++) tracker.Update(0.05f); // normal speech
        var bars = tracker.GenerateWaveformBars(5);

        Assert.True(bars.Max() < 0.27f, $"old mapping peaked at {bars.Max()}, below the idle breathing amplitude");
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.05f)]
    [InlineData(0.2f)]
    public void Normal_speech_visibly_grows_and_wobbles_the_orb(float rms)
    {
        var (silence, silentShape) = Drive(0.001f, 1.5f);
        var (speech, speechShape) = Drive(rms, 1.5f);

        Assert.True(speech.Frame.Level > silence.Frame.Level + 0.25f, $"level {speech.Frame.Level} vs {silence.Frame.Level}");
        Assert.True(speechShape.MaxRadius > silentShape.MaxRadius + 0.04f, $"max radius {speechShape.MaxRadius} vs {silentShape.MaxRadius}");
        Assert.True(speechShape.MaxRadius - speechShape.MinRadius > silentShape.MaxRadius - silentShape.MinRadius,
            "voice must deform the outline more than silence");
    }

    [Fact]
    public void Silence_keeps_the_orb_alive_by_breathing()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < 60 * 8; i++)
        {
            animator.SetLevel(0f);
            animator.Tick(Dt);
            min = Math.Min(min, animator.Frame.Breath);
            max = Math.Max(max, animator.Frame.Breath);
        }

        Assert.True(max > 0.9f && min < -0.9f, $"breath range {min}..{max}");
    }
}

public sealed class OrbAnimatorTests
{
    private const float Dt = 1f / 60f;

    [Fact]
    public void Entrance_starts_small_overshoots_elastically_then_settles_at_full_size()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        Assert.True(animator.Frame.Scale <= OrbAnimator.EntranceStartScale + 0.01f);

        var peak = 0f;
        for (var i = 0; i < 180; i++)
        {
            animator.Tick(Dt);
            peak = Math.Max(peak, animator.Frame.Scale);
        }

        Assert.True(animator.EntranceDampingRatio < 1f);
        Assert.True(peak > 1.03f, $"expected an overshoot, peak was {peak}");
        Assert.InRange(animator.Frame.Scale, 0.98f, 1.02f);
        Assert.True(animator.Frame.Opacity > 0.99f);
    }

    [Fact]
    public void Exit_shrinks_and_dissolves_then_goes_inactive()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 60; i++) animator.Tick(Dt);

        animator.Hide();
        var previousOpacity = animator.Frame.Opacity;
        var frames = 0;
        while (animator.Tick(Dt) && frames < 120)
        {
            frames++;
            Assert.True(animator.Frame.Opacity <= previousOpacity + 1e-4f);
            previousOpacity = animator.Frame.Opacity;
        }

        Assert.False(animator.IsActive);
        Assert.True(frames < 60, $"dissolve took {frames} frames");
        Assert.Equal(0f, animator.Frame.Opacity);
    }

    [Fact]
    public void Entrance_and_exit_soften_the_edge()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        animator.Tick(Dt);
        Assert.True(animator.Frame.Soften > 0.5f);

        for (var i = 0; i < 60; i++) animator.Tick(Dt);
        Assert.True(animator.Frame.Soften < 0.02f);
    }

    [Fact]
    public void Modes_crossfade_the_palette()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 30; i++) animator.Tick(Dt);
        Assert.True(animator.Frame.TranscribeMix < 0.01f && animator.Frame.NoticeMix < 0.01f);

        animator.Show(OverlayMode.Transcribing);
        for (var i = 0; i < 120; i++) animator.Tick(Dt);
        Assert.True(animator.Frame.TranscribeMix > 0.95f);

        animator.Show(OverlayMode.Notice);
        for (var i = 0; i < 120; i++) animator.Tick(Dt);
        Assert.True(animator.Frame.NoticeMix > 0.95f && animator.Frame.TranscribeMix < 0.05f);
    }

    [Fact]
    public void Voice_makes_the_color_flow_faster()
    {
        double FlowAfterOneSecond(float level)
        {
            var animator = new OrbAnimator();
            animator.Show(OverlayMode.Listening);
            animator.SetLevel(level);
            for (var i = 0; i < 60; i++) animator.Tick(Dt);
            return animator.Frame.Flow;
        }

        Assert.True(FlowAfterOneSecond(1f) > FlowAfterOneSecond(0f) * 1.8);
    }

    [Fact]
    public void Huge_frame_gaps_are_clamped_and_never_produce_nan()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        animator.SetLevel(1f);
        animator.Tick(5f);

        Assert.False(float.IsNaN(animator.Frame.Scale));
        Assert.InRange(animator.Frame.Scale, 0f, 2f);
    }

    [Fact]
    public void Retriggering_during_exit_brings_the_orb_back()
    {
        var animator = new OrbAnimator();
        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 60; i++) animator.Tick(Dt);
        animator.Hide();
        for (var i = 0; i < 5; i++) animator.Tick(Dt);

        animator.Show(OverlayMode.Listening);
        for (var i = 0; i < 90; i++) animator.Tick(Dt);

        Assert.True(animator.IsActive);
        Assert.True(animator.Frame.Opacity > 0.99f);
        Assert.InRange(animator.Frame.Scale, 0.98f, 1.02f);
    }
}

public sealed class SpringTests
{
    [Fact]
    public void Underdamped_spring_overshoots_and_converges()
    {
        var spring = new Spring(250f, 15f) { Target = 1f };
        var peak = 0f;
        for (var i = 0; i < 300; i++)
        {
            spring.Step(1f / 60f);
            peak = Math.Max(peak, spring.Value);
        }

        Assert.True(peak > 1.05f);
        Assert.InRange(spring.Value, 0.999f, 1.001f);
    }

    [Fact]
    public void Critically_damped_spring_never_overshoots()
    {
        var spring = new Spring(170f, 2f * MathF.Sqrt(170f)) { Target = 1f };
        for (var i = 0; i < 300; i++)
        {
            spring.Step(1f / 60f);
            Assert.True(spring.Value <= 1.0001f);
        }
    }

    [Fact]
    public void Large_steps_are_stable()
    {
        var spring = new Spring(250f, 15f) { Target = 1f };
        spring.Step(0.1f);
        Assert.True(float.IsFinite(spring.Value));
        Assert.InRange(spring.Value, 0f, 2f);
    }
}

public sealed class OrbShapeTests
{
    [Fact]
    public void Silence_is_nearly_round_and_voice_deforms_it()
    {
        var shape = new OrbShape();
        shape.Update(OrbFrame.Static(OverlayMode.Listening, 0f));
        var silentSpread = shape.MaxRadius - shape.MinRadius;
        shape.Update(OrbFrame.Static(OverlayMode.Listening, 1f));
        var loudSpread = shape.MaxRadius - shape.MinRadius;

        Assert.True(silentSpread < 0.05f, $"silent spread {silentSpread}");
        Assert.True(loudSpread > 0.12f, $"loud spread {loudSpread}");
        Assert.True(shape.MinRadius > 0.8f, "outline must stay a convex-ish drop, never fold in on itself");
    }

    [Fact]
    public void Outline_is_continuous_across_the_wrap_point()
    {
        var shape = new OrbShape();
        shape.Update(OrbFrame.Static(OverlayMode.Listening, 0.7f));

        Assert.Equal(shape.Radii[0], shape.Radii[OrbShape.Samples], 5);
        Assert.Equal(shape.RadiusAt(-MathF.PI), shape.RadiusAt(MathF.PI - 1e-4f), 2);
    }

    [Fact]
    public void Shape_follows_the_flow_phase_over_time()
    {
        var shape = new OrbShape();
        shape.Update(OrbFrame.Static(OverlayMode.Listening, 0.8f) with { Flow = 0f });
        var before = shape.RadiusAt(0.4f);
        shape.Update(OrbFrame.Static(OverlayMode.Listening, 0.8f) with { Flow = 2f });

        Assert.NotEqual(before, shape.RadiusAt(0.4f));
    }
}

public sealed class OrbPaletteTests
{
    [Fact]
    public void Palettes_crossfade_between_listening_transcribing_and_notice()
    {
        var listening = new float[OrbPalette.Floats];
        var transcribing = new float[OrbPalette.Floats];
        var notice = new float[OrbPalette.Floats];
        OrbPalette.Resolve(0f, 0f, listening);
        OrbPalette.Resolve(1f, 0f, transcribing);
        OrbPalette.Resolve(0f, 1f, notice);

        Assert.NotEqual(listening, transcribing);
        Assert.True(notice[0] > 0.9f && notice[2] < 0.2f, "notice primary is amber/red (high red, low blue)");
        Assert.True(transcribing[2] > 0.9f, "transcribing primary is cyan (high blue)");

        var half = new float[OrbPalette.Floats];
        OrbPalette.Resolve(0.5f, 0f, half);
        Assert.Equal((listening[0] + transcribing[0]) / 2f, half[0], 4);
    }

    [Fact]
    public void All_palette_values_are_valid_colors()
    {
        var palette = new float[OrbPalette.Floats];
        foreach (var (t, n) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (0.5f, 0.5f) })
        {
            OrbPalette.Resolve(t, n, palette);
            Assert.All(palette, v => Assert.InRange(v, 0f, 1f));
        }
    }
}

public sealed class OrbRendererTests
{
    private static uint[] Render(OrbFrame frame, float dpi = 1f, int plate = 0, OrbRenderer? renderer = null)
    {
        var layout = OrbLayout.Scaled(dpi);
        var buffer = new uint[layout.Width * layout.Height];
        (renderer ?? new OrbRenderer()).Render(buffer, layout.Width, layout.Height, dpi, frame, plate);
        return buffer;
    }

    private static int CountOpaque(uint[] buffer, byte threshold) => buffer.Count(p => (p >> 24) > threshold);

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Orb_is_centered_translucent_glass_with_transparent_corners(float dpi)
    {
        var layout = OrbLayout.Scaled(dpi);
        var buffer = Render(OrbFrame.Static(OverlayMode.Listening), dpi);

        Assert.Equal(0u, buffer[0]);
        Assert.Equal(0u, buffer[layout.Width - 1]);
        Assert.Equal(0u, buffer[(layout.Height - 1) * layout.Width]);
        Assert.Equal(0u, buffer[buffer.Length - 1]);

        var center = buffer[(int)layout.CenterY * layout.Width + layout.Width / 2];
        var alpha = center >> 24;
        Assert.InRange(alpha, 120u, 252u); // glass: visible but not a solid disc
    }

    [Theory]
    [InlineData(OverlayMode.Listening)]
    [InlineData(OverlayMode.Transcribing)]
    [InlineData(OverlayMode.Notice)]
    public void Every_pixel_is_valid_premultiplied_alpha(OverlayMode mode)
    {
        var buffer = Render(OrbFrame.Static(mode, 0.9f) with { Soften = 0.3f }, 1f, 120);

        foreach (var p in buffer)
        {
            var a = p >> 24;
            Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a, $"pixel {p:X8} is not premultiplied");
        }
    }

    [Fact]
    public void Specular_highlight_sits_upper_left_and_is_brighter_than_the_opposite_side()
    {
        var layout = OrbLayout.Scaled(1f);
        var buffer = Render(OrbFrame.Static(OverlayMode.Listening));
        var cx = layout.Width / 2;
        var cy = (int)layout.CenterY;
        var offset = (int)(layout.Radius * 0.45f);

        uint Luma(int x, int y)
        {
            var p = buffer[y * layout.Width + x];
            return ((p >> 16) & 0xFF) + ((p >> 8) & 0xFF) + (p & 0xFF);
        }

        Assert.True(Luma(cx - offset, cy - offset) > Luma(cx + offset, cy - offset) + 30);
    }

    [Fact]
    public void Rim_is_brighter_and_more_opaque_than_the_translucent_core()
    {
        var layout = OrbLayout.Scaled(1f);
        var buffer = Render(OrbFrame.Static(OverlayMode.Listening));
        var y = (int)layout.CenterY;
        var rimX = layout.Width / 2 + (int)(layout.Radius * 0.93f);
        var coreX = layout.Width / 2 + (int)(layout.Radius * 0.15f);

        Assert.True((buffer[y * layout.Width + rimX] >> 24) > (buffer[y * layout.Width + coreX] >> 24));
    }

    [Fact]
    public void Voice_grows_the_drop()
    {
        var calm = CountOpaque(Render(OrbFrame.Static(OverlayMode.Listening, 0f)), 128);
        var shout = CountOpaque(Render(OrbFrame.Static(OverlayMode.Listening, 1f)), 128);

        Assert.True(shout > calm * 1.15, $"{shout} vs {calm}");
    }

    [Fact]
    public void Opacity_scales_the_whole_image_and_zero_draws_nothing()
    {
        var full = Render(OrbFrame.Static(OverlayMode.Listening));
        var half = Render(OrbFrame.Static(OverlayMode.Listening) with { Opacity = 0.5f });
        var none = Render(OrbFrame.Static(OverlayMode.Listening) with { Opacity = 0f });

        Assert.True(CountOpaque(half, 200) < CountOpaque(full, 200) / 4);
        Assert.All(none, p => Assert.Equal(0u, p));
    }

    [Fact]
    public void Softened_edge_spreads_further_than_a_crisp_one()
    {
        static int Translucent(uint[] buffer) => buffer.Count(p => (p >> 24) is > 40 and < 215);

        var crisp = Translucent(Render(OrbFrame.Static(OverlayMode.Listening)));
        var soft = Translucent(Render(OrbFrame.Static(OverlayMode.Listening) with { Soften = 1f }));

        Assert.True(soft > crisp, $"{soft} vs {crisp}");
    }

    [Fact]
    public void Caption_plate_is_drawn_below_the_orb_only_when_requested()
    {
        var layout = OrbLayout.Scaled(1f);
        var without = Render(OrbFrame.Static(OverlayMode.Notice));
        var with = Render(OrbFrame.Static(OverlayMode.Notice), 1f, 160);
        var index = (int)layout.CaptionCenterY * layout.Width + layout.Width / 2;

        Assert.Equal(0u, without[index]);
        Assert.True((with[index] >> 24) > 150);
    }

    [Fact]
    public void Notice_palette_is_warm_and_transcribing_is_cool()
    {
        static (long R, long B) Average(uint[] buffer)
        {
            long r = 0, b = 0;
            foreach (var p in buffer)
            {
                r += (p >> 16) & 0xFF;
                b += p & 0xFF;
            }

            return (r, b);
        }

        var notice = Average(Render(OrbFrame.Static(OverlayMode.Notice)));
        var cool = Average(Render(OrbFrame.Static(OverlayMode.Transcribing)));

        Assert.True(notice.R > notice.B * 1.3);
        Assert.True(cool.B > cool.R);
    }

    [Fact]
    public void Rendering_is_deterministic_and_reuses_the_renderer_without_leftovers()
    {
        var renderer = new OrbRenderer();
        var frame = OrbFrame.Static(OverlayMode.Listening, 0.6f);
        var first = Render(frame, 1f, 100, renderer);
        Render(OrbFrame.Static(OverlayMode.Transcribing, 1f), 1f, 0, renderer); // dirty the scratch state
        var again = Render(frame, 1f, 100, renderer);

        Assert.Equal(first, again);
    }

    [Fact]
    public void Upscaled_hidpi_render_matches_the_direct_one_in_extent()
    {
        var frame = OrbFrame.Static(OverlayMode.Listening, 0.5f);
        var up = Render(frame, 2f);
        var direct = Render(frame, 2f, 0, new OrbRenderer { AllowUpscale = false });

        var upCount = CountOpaque(up, 128);
        var directCount = CountOpaque(direct, 128);
        Assert.InRange(upCount / (double)directCount, 0.95, 1.05);
    }

    [Fact]
    public void A_full_animation_cycle_renders_without_exceptions_or_bad_pixels()
    {
        var animator = new OrbAnimator();
        var renderer = new OrbRenderer();
        var layout = OrbLayout.Scaled(1f);
        var buffer = new uint[layout.Width * layout.Height];
        animator.Show(OverlayMode.Listening);

        for (var i = 0; i < 240; i++)
        {
            animator.SetLevel(0.5f + 0.5f * MathF.Sin(i * 0.2f));
            animator.Tick(1f / 60f);
            renderer.Render(buffer, layout.Width, layout.Height, 1f, animator.Frame, 100);
        }

        animator.Show(OverlayMode.Transcribing);
        animator.Hide();
        while (animator.Tick(1f / 60f)) renderer.Render(buffer, layout.Width, layout.Height, 1f, animator.Frame);

        Assert.False(animator.IsActive);
    }
}

public sealed class LevelFromBarsTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(0.8f)]
    [InlineData(1f)]
    public void Level_survives_the_trip_through_waveform_bars(float level)
    {
        var tracker = new AudioLevelTracker();
        var bars = tracker.GenerateWaveformBars(5, level);

        Assert.Equal(level, AudioLevelTracker.LevelFromBars(bars), 3);
    }

    [Fact]
    public void Empty_bars_mean_silence()
    {
        Assert.Equal(0f, AudioLevelTracker.LevelFromBars([]));
    }
}

public sealed class FailureNoticeTests
{
    [Theory]
    [InlineData("engine_failed")]
    [InlineData("engine_retry_failed")]
    [InlineData("engine_exception")]
    [InlineData(null)]
    [InlineData("anything_else")]
    public void Engine_failures_always_produce_a_recoverable_notice(string? code)
    {
        Assert.Equal(OverlayNoticeKind.EngineFailed, OverlayNotices.ForFailure(code));
        Assert.Contains("áudio salvo", OverlayText.For(OverlayNoticeKind.EngineFailed));
    }

    [Fact]
    public void Timeout_and_cloud_codes_keep_their_specific_notices()
    {
        Assert.Equal(OverlayNoticeKind.TranscribeTimeout, OverlayNotices.ForFailure(OverlayNotices.TranscribeTimeoutCode));
        Assert.Contains("áudio salvo", OverlayText.For(OverlayNoticeKind.TranscribeTimeout));
        Assert.Equal(OverlayNoticeKind.CloudNoNetwork, OverlayNotices.ForFailure(Somning.Core.Cloud.CloudErrorCodes.NoNetwork));
    }
}
