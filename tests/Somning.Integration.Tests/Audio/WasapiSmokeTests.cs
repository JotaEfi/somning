using NAudio.Wave;
using Somning.Core.Ports;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class WasapiSmokeTests
{
    [Fact]
    [Trait("Category", "Mic")]
    public async Task Captures_one_second_of_default_device_as_16khz_mono_pcm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "somning-wasapi-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wavPath = Path.Combine(root, "capture.wav");
        var faults = new List<AudioFault>();
        try
        {
            await using var source = new WasapiAudioSource();
            source.Faulted += faults.Add;
            using (var spool = new SpoolWriter(wavPath))
            {
                AudioStartInfo start;
                try
                {
                    start = await source.StartAsync(spool, cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (AudioDeviceUnavailableException exception) when (exception.Code == "no_device")
                {
                    Assert.Skip("no capture device");
                    return;
                }

                Assert.InRange(start.MicOpenMs, 0, 1_999);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                await source.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            Assert.Empty(faults);
            using var reader = new WaveFileReader(wavPath);
            Assert.Equal(16_000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.InRange(reader.TotalTime.TotalSeconds, .90, 1.30);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
