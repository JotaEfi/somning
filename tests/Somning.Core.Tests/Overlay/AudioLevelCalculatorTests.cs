using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

public sealed class AudioLevelCalculatorTests
{
    private const float Tolerance = 0.0005f;

    [Fact]
    public void Rms_of_empty_short_span_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputeRms(ReadOnlySpan<short>.Empty));
    }

    [Fact]
    public void Rms_of_empty_pcm_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputeRms(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Rms_of_a_single_trailing_odd_byte_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputeRms(new byte[] { 0x7F }));
    }

    [Fact]
    public void Rms_of_silence_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputeRms(new short[] { 0, 0, 0, 0 }));
        Assert.Equal(0f, AudioLevelCalculator.ComputeRms(Pcm(0, 0, 0, 0)));
    }

    [Fact]
    public void Rms_of_constant_half_scale_is_half()
    {
        var samples = new short[] { 16384, 16384, 16384, 16384 };

        AssertClose(0.5f, AudioLevelCalculator.ComputeRms(samples));
        AssertClose(0.5f, AudioLevelCalculator.ComputeRms(Pcm(samples)));
    }

    [Fact]
    public void Rms_of_a_full_scale_square_wave_is_one()
    {
        var samples = new short[] { short.MinValue, short.MaxValue, short.MinValue, short.MaxValue };

        Assert.InRange(AudioLevelCalculator.ComputeRms(samples), 0.99f, 1f);
        Assert.InRange(AudioLevelCalculator.ComputeRms(Pcm(samples)), 0.99f, 1f);
    }

    [Fact]
    public void Rms_of_a_sine_wave_matches_the_analytic_value()
    {
        var samples = new short[4096];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = (short)(Math.Sin(2 * Math.PI * index / 64d) * 32000);

        Assert.InRange(AudioLevelCalculator.ComputeRms(samples), 0.69f, 0.71f);
    }

    [Fact]
    public void Rms_is_always_normalized_between_zero_and_one()
    {
        var samples = new short[] { short.MinValue, 0, short.MaxValue, -1, 1 };

        var rmsShort = AudioLevelCalculator.ComputeRms(samples);
        var rmsBytes = AudioLevelCalculator.ComputeRms(Pcm(samples));

        Assert.InRange(rmsShort, 0f, 1f);
        Assert.InRange(rmsBytes, 0f, 1f);
    }

    [Fact]
    public void Byte_pcm_is_read_as_little_endian()
    {
        // 0x00 0x40 = 16384 (0.5 full scale), 0x00 0xC0 = -16384 (0.5 full scale).
        var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0 };

        AssertClose(0.5f, AudioLevelCalculator.ComputeRms(pcm));
    }

    [Fact]
    public void Byte_pcm_ignores_a_trailing_odd_byte()
    {
        var withTrailingByte = Pcm(16384, -16384).Append((byte)0xFF).ToArray();

        AssertClose(0.5f, AudioLevelCalculator.ComputeRms(withTrailingByte));
    }

    [Fact]
    public void Peak_of_empty_inputs_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputePeak(ReadOnlySpan<short>.Empty));
        Assert.Equal(0f, AudioLevelCalculator.ComputePeak(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Peak_of_silence_is_zero()
    {
        Assert.Equal(0f, AudioLevelCalculator.ComputePeak(new short[] { 0, 0, 0, 0 }));
        Assert.Equal(0f, AudioLevelCalculator.ComputePeak(Pcm(0, 0, 0, 0)));
    }

    [Fact]
    public void Peak_uses_the_largest_magnitude_including_negative_samples()
    {
        var samples = new short[] { 100, -16384, 4096 };

        AssertClose(0.5f, AudioLevelCalculator.ComputePeak(samples));
        AssertClose(0.5f, AudioLevelCalculator.ComputePeak(Pcm(samples)));
    }

    [Fact]
    public void Peak_of_full_scale_samples_is_one()
    {
        Assert.Equal(1f, AudioLevelCalculator.ComputePeak(new short[] { short.MinValue }));
        Assert.InRange(AudioLevelCalculator.ComputePeak(new short[] { short.MaxValue }), 0.99f, 1f);
        Assert.Equal(1f, AudioLevelCalculator.ComputePeak(new byte[] { 0x00, 0x80 }));
    }

    [Fact]
    public void Peak_is_never_below_rms()
    {
        var samples = new short[] { 1000, -500, 0, 32000 };

        var rms = AudioLevelCalculator.ComputeRms(samples);
        var peak = AudioLevelCalculator.ComputePeak(samples);

        Assert.InRange(rms, 0f, 1f);
        Assert.InRange(peak, 0f, 1f);
        Assert.True(peak >= rms);
    }

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            bytes[index * 2] = (byte)(sample & 0xFF);
            bytes[index * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return bytes;
    }

    private static void AssertClose(float expected, float actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
