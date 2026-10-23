using Somning.Core.Text;
using Somning.Engine.Protocol;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Engine;

public sealed class FixtureAccuracyFixture : IAsyncLifetime
{
    public string Allowed { get; } = Path.Combine(Path.GetTempPath(), "somning-accuracy", Guid.NewGuid().ToString("N"));
    public EngineWorkerClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Requires.Models();
        Directory.CreateDirectory(Allowed);
        foreach (var fixture in FixtureManifest.Load())
            File.Copy(Path.Combine(RepoPaths.Fixtures, fixture.File), Path.Combine(Allowed, Path.GetFileName(fixture.File)));
        Client = new EngineWorkerClient(Allowed);
        await Client.ReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.SendAsync(new ShutdownRequest());
            await Client.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Client.Dispose();
        }
        if (Directory.Exists(Allowed)) Directory.Delete(Allowed, true);
    }
}

[Collection("EngineIntegration")]
public sealed class FixtureAccuracyTests(FixtureAccuracyFixture fixture) : IClassFixture<FixtureAccuracyFixture>
{
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
        await fixture.Client.SendAsync(new TranscribeRequest(Guid.NewGuid().ToString("N"), Path.Combine(fixture.Allowed, Path.GetFileName(entry.File))));
        var result = Assert.IsType<ResultMessage>(await fixture.Client.ReadMessageAsync(TimeSpan.FromSeconds(120)));
        Assert.Equal("text", result.Outcome);
        return result;
    }
}
