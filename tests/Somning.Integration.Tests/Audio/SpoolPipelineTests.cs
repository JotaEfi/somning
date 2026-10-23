using NAudio.Wave;
using Somning.App.Engine;
using Somning.Core.Engine;
using Somning.Core.Recording;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Audio;

[Collection("EngineIntegration")]
public sealed class SpoolPipelineTests
{
    [Fact]
    public async Task File_to_spool_to_engine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Requires.Models();
        var dataDir = Path.Combine(Path.GetTempPath(), "somning-spool", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var priorDataDir = Environment.GetEnvironmentVariable("SOMNING_DATA_DIR");
        Environment.SetEnvironmentVariable("SOMNING_DATA_DIR", dataDir);
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var wavPath = Path.Combine(dataDir, "recordings", id + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);

        try
        {
            await using var source = new FileAudioSource(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav"), realTime: false);
            using (var spool = new SpoolWriter(wavPath))
            {
                var start = await source.StartAsync(spool, cancellationToken);
                Assert.Equal(0, start.MicOpenMs);
                await source.Completion.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                await source.StopAsync();
                Assert.InRange(spool.Duration.TotalSeconds, 1.0, 30.0);
            }
            Assert.Equal(source.FramesWritten, (new FileInfo(wavPath).Length - 44) / 2);
            using (var reader = new WaveFileReader(wavPath))
            {
                Assert.Equal(16_000, reader.WaveFormat.SampleRate);
                Assert.Equal(1, reader.WaveFormat.Channels);
                Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            }

            var store = new SidecarStore(Path.Combine(dataDir, "recordings"));
            var sidecar = new RecordingSidecar(id, DateTimeOffset.UtcNow, RecordingStage.Captured,
                "harness", null, null, null, 0, null);
            store.Save(sidecar);
            Assert.Equal(sidecar, store.Load(id));

            await using var engine = new EngineSupervisor();
            await engine.StartAsync("cpu", cancellationToken);
            var transcription = await engine.TranscribeAsync(wavPath, cancellationToken);
            var expected = File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt")).Trim();
            Assert.Equal("text", transcription.Outcome);
            Assert.True(Wer.Compute(expected, transcription.Text) <= .10,
                $"WER {Wer.Compute(expected, transcription.Text):P1}; expected '{expected}', got '{transcription.Text}'.");
            store.Save(sidecar with { Stage = RecordingStage.Transcribed, Text = transcription.Text, Outcome = transcription.Outcome });
            Assert.Equal(RecordingStage.Transcribed, store.Load(id)!.Stage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOMNING_DATA_DIR", priorDataDir);
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Disposed_spool_is_released_after_write_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-spool", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "recording.wav");
        try
        {
            var spool = new SpoolWriter(path);
            Assert.Throws<ArgumentException>(() => spool.Write([0x01]));
            spool.Dispose();
            spool.Dispose();

            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
