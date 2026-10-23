using Somning.Core.Text;
using Somning.Engine.Protocol;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Engine;

[Collection("EngineIntegration")]
public sealed class FixtureAccuracyTests : IAsyncLifetime
{
    private readonly string allowed = Path.Combine(Path.GetTempPath(), "somning-accuracy", Guid.NewGuid().ToString("N"));
    private EngineWorkerClient client = null!;

    public async ValueTask InitializeAsync()
    {
        Requires.Models();
        Directory.CreateDirectory(allowed);
        foreach (var fixture in FixtureManifest.Load())
            File.Copy(Path.Combine(RepoPaths.Fixtures, fixture.File), Path.Combine(allowed, Path.GetFileName(fixture.File)));
        client = new EngineWorkerClient(allowed);
        await client.ReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.SendAsync(new ShutdownRequest());
            await client.WaitForExitAsync(TimeSpan.FromSeconds(10));
            client.Dispose();
        }
        if (Directory.Exists(allowed)) Directory.Delete(allowed, true);
    }

    [Fact]
    public async Task Clean_fixtures_meet_quality_target()
    {
        var failures = new List<string>();
        foreach (var entry in FixtureManifest.Load().Where(x => x.Clean && x.Kind == "speech"))
        {
            var actual = await Transcribe(entry);
            var wer = Wer.Compute(entry.Expected!, actual.Text);
            if (wer > .10) failures.Add($"{entry.File}: WER {wer:P1}; transcript: {actual.Text}");
            if (entry.File is "speech/longo-numeros.wav" or "speech/tecnico.wav" or "tracer/hoje.wav")
            {
                if (!actual.Text.Any(char.IsUpper) || !actual.Text.Any(c => ".,?!".Contains(c)))
                    failures.Add($"{entry.File}: missing capitalization/punctuation; transcript {actual.Text}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Non_clean_speech_remains_usable()
    {
        var failures = new List<string>();
        foreach (var entry in FixtureManifest.Load().Where(x => !x.Clean && x.Kind == "speech" && x.ExpectOutcome == "text"))
        {
            var actual = await Transcribe(entry);
            var wer = Wer.Compute(entry.Expected!, actual.Text);
            if (wer > .25) failures.Add($"{entry.File}: WER {wer:P1}; transcript {actual.Text}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private async Task<ResultMessage> Transcribe(FixtureEntry entry)
    {
        await client.SendAsync(new TranscribeRequest(Guid.NewGuid().ToString("N"), Path.Combine(allowed, Path.GetFileName(entry.File))));
        var result = Assert.IsType<ResultMessage>(await client.ReadMessageAsync(TimeSpan.FromSeconds(120)));
        Assert.Equal("text", result.Outcome);
        return result;
    }
}
