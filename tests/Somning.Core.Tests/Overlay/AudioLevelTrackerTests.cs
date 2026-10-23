using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class AudioLevelTrackerTests
{
    private const float Tolerance = 0.0005f;

    [Fact]
    public void New_tracker_starts_at_zero()
    {
        var tracker = new AudioLevelTracker();

        Assert.Equal(0f, tracker.Level);
        Assert.Equal(0f, tracker.CurrentLevel);
        Assert.Equal(AudioLevelTracker.DefaultAttack, tracker.Attack);
        Assert.Equal(AudioLevelTracker.DefaultDecay, tracker.Decay);
    }

    [Fact]
    public void Attack_rises_fast_and_decay_falls_smoothly()
    {
        var tracker = new AudioLevelTracker(attack: 0.5f, decay: 0.25f);

        AssertClose(0.5f, tracker.Update(1f));
        AssertClose(0.75f, tracker.Update(1f));
        AssertClose(0.5625f, tracker.Update(0f));
        AssertClose(0.421875f, tracker.Update(0f));
    }

    [Fact]
    public void Attack_of_one_reaches_the_target_immediately()
    {
        var tracker = new AudioLevelTracker(attack: 1f, decay: 0f);

        Assert.Equal(0.8f, tracker.Update(0.8f));
        Assert.Equal(0.8f, tracker.Update(0f));
    }

    [Fact]
    public void Attack_of_zero_holds_the_previous_level()
    {
        var tracker = new AudioLevelTracker(attack: 0f, decay: 0f);

        Assert.Equal(0f, tracker.Update(0.9f));
    }

    [Fact]
    public void Updates_are_clamped_to_the_normalized_range()
    {
        var tracker = new AudioLevelTracker(attack: 1f, decay: 1f);

        Assert.Equal(1f, tracker.Update(5f));
        Assert.Equal(0f, tracker.Update(-5f));
        Assert.InRange(tracker.Level, 0f, 1f);
    }

    [Fact]
    public void Constructor_rejects_out_of_range_coefficients()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelTracker(attack: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelTracker(attack: 1.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelTracker(decay: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelTracker(decay: 1.1f));
    }

    [Fact]
    public void Pcm16_overload_updates_from_the_rms_level()
    {
        var tracker = new AudioLevelTracker(attack: 1f, decay: 1f);

        AssertClose(0.5f, tracker.Update(new short[] { 16384, 16384, 16384, 16384 }));
        AssertClose(0.5f, tracker.Update(new byte[] { 0x00, 0x40, 0x00, 0xC0 }));

        var silent = new AudioLevelTracker(attack: 1f, decay: 1f);
        Assert.Equal(0f, silent.Update(new short[] { 0, 0, 0, 0 }));
        Assert.Equal(0f, silent.Update(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Reset_returns_the_level_to_zero()
    {
        var tracker = new AudioLevelTracker(attack: 1f);
        tracker.Update(1f);

        tracker.Reset();

        Assert.Equal(0f, tracker.Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Waveform_with_a_non_positive_count_is_empty(int count)
    {
        var tracker = new AudioLevelTracker();

        Assert.Empty(tracker.GenerateWaveformBars(count, 0.5f));
        Assert.Empty(tracker.GenerateWaveformBars(count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(17)]
    public void Waveform_has_the_requested_number_of_bars_in_range(int count)
    {
        var tracker = new AudioLevelTracker();

        var bars = tracker.GenerateWaveformBars(count, 0.75f);

        Assert.Equal(count, bars.Length);
        Assert.All(bars, bar => Assert.InRange(bar, AudioLevelTracker.MinimumBarHeight, 1f));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Waveform_at_zero_level_sits_on_the_minimum_baseline(int count)
    {
        var tracker = new AudioLevelTracker();

        var bars = tracker.GenerateWaveformBars(count, 0f);

        Assert.All(bars, bar => AssertClose(AudioLevelTracker.MinimumBarHeight, bar));
    }

    [Fact]
    public void Waveform_at_full_level_reaches_the_top_and_is_center_weighted()
    {
        var tracker = new AudioLevelTracker();

        var bars = tracker.GenerateWaveformBars(5, 1f);

        AssertClose(1f, bars[2]);
        Assert.True(bars[2] > bars[1]);
        Assert.True(bars[1] > bars[0]);
        Assert.True(bars[2] > bars[3]);
        Assert.True(bars[3] > bars[4]);
        AssertClose(bars[1], bars[3]);
        AssertClose(bars[0], bars[4]);
        Assert.All(bars, bar => Assert.InRange(bar, AudioLevelTracker.MinimumBarHeight, 1f));
    }

    [Fact]
    public void Waveform_bars_vary_between_neighbors()
    {
        var tracker = new AudioLevelTracker();

        var bars = tracker.GenerateWaveformBars(7, 0.6f);

        Assert.True(bars.Distinct().Count() > 1);
    }

    [Fact]
    public void Waveform_is_deterministic_for_the_same_inputs()
    {
        var tracker = new AudioLevelTracker();

        var first = tracker.GenerateWaveformBars(6, 0.42f);
        var second = tracker.GenerateWaveformBars(6, 0.42f);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Waveform_grows_with_the_level()
    {
        var tracker = new AudioLevelTracker();

        var quiet = tracker.GenerateWaveformBars(5, 0.2f);
        var loud = tracker.GenerateWaveformBars(5, 0.9f);

        for (var index = 0; index < quiet.Length; index++)
            Assert.True(loud[index] > quiet[index], $"bar {index} should grow with the level");
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Waveform_clamps_out_of_range_levels(float level)
    {
        var tracker = new AudioLevelTracker();

        var bars = tracker.GenerateWaveformBars(5, level);

        Assert.All(bars, bar => Assert.InRange(bar, AudioLevelTracker.MinimumBarHeight, 1f));
    }

    [Fact]
    public void Out_of_range_levels_are_equivalent_to_their_clamped_values()
    {
        var tracker = new AudioLevelTracker();

        Assert.Equal(
            tracker.GenerateWaveformBars(5, 0f),
            tracker.GenerateWaveformBars(5, -3f));
        Assert.Equal(
            tracker.GenerateWaveformBars(5, 1f),
            tracker.GenerateWaveformBars(5, 3f));
    }

    [Fact]
    public void Waveform_overload_without_level_uses_the_current_smoothed_level()
    {
        var tracker = new AudioLevelTracker(attack: 1f);

        Assert.Equal(tracker.GenerateWaveformBars(5, 0f), tracker.GenerateWaveformBars(5));

        tracker.Update(0.5f);

        Assert.Equal(tracker.GenerateWaveformBars(5, 0.5f), tracker.GenerateWaveformBars(5));
    }

    [Fact]
    public void Waveform_returns_a_fresh_array_on_every_call()
    {
        var tracker = new AudioLevelTracker();

        var first = tracker.GenerateWaveformBars(5, 0.5f);
        var second = tracker.GenerateWaveformBars(5, 0.5f);

        Assert.NotSame(first, second);
    }

    private static void AssertClose(float expected, float actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
