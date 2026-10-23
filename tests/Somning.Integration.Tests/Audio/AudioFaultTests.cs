using NAudio.Wave;
using Somning.Integration.Tests.Support;
using Somning.Core.Ports;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class AudioFaultTests
{
    [Fact]
    public async Task First_data_guard_reports_no_audio_data_within_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstData = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<AudioDeviceUnavailableException>(() =>
            FirstDataGuard.WaitAsync(firstData.Task, TimeSpan.FromMilliseconds(150), cancellationToken));

        Assert.Equal("no_audio_data", exception.Code);
        Assert.InRange(started.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public async Task File_audio_source_reports_injected_fault_once_and_spool_remains_valid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "somning-audio-fault", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wavPath = Path.Combine(root, "faulted.wav");
        var faults = new List<AudioFault>();
        try
        {
            await using var source = new FileAudioSource(
                Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav"), realTime: true,
                faultAfter: TimeSpan.FromMilliseconds(300));
            source.Faulted += faults.Add;
            using (var spool = new SpoolWriter(wavPath))
            {
                await source.StartAsync(spool, cancellationToken);
                await source.Completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                await source.StopAsync();
            }

            Assert.Single(faults);
            Assert.Equal("injected", faults[0].Code);
            using var reader = new WaveFileReader(wavPath);
            Assert.Equal(16_000, reader.WaveFormat.SampleRate);
            Assert.True(reader.Length > 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
