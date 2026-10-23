using Somning.Engine.Protocol;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Engine;

public sealed class SilenceGateFixture : IAsyncLifetime
{
    public string Allowed { get; } = Path.Combine(Path.GetTempPath(), "somning-fixtures", Guid.NewGuid().ToString("N"));
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
public sealed class SilenceGateTests(SilenceGateFixture fixture) : IClassFixture<SilenceGateFixture>
{
    [Fact]
    public async Task Rejects_all_empty_fixtures()
    {
        foreach (var entry in FixtureManifest.Load().Where(x => x.ExpectOutcome == "empty"))
        {
            var path = Path.Combine(fixture.Allowed, Path.GetFileName(entry.File));
            await fixture.Client.SendAsync(new TranscribeRequest(Guid.NewGuid().ToString("N"), path));
            var result = Assert.IsType<ResultMessage>(await fixture.Client.ReadMessageAsync(TimeSpan.FromSeconds(60)));
            Assert.True(result.Outcome == "empty", $"{entry.File}: expected empty, got {result.Outcome}: {result.Text}");
            if (!string.IsNullOrEmpty(entry.ExpectReason)) Assert.Equal(entry.ExpectReason, result.Reason);
            else Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
    }

    [Theory]
    [InlineData("cmd-curto.wav")]
    [InlineData("sim.wav")]
    [InlineData("e-ai.wav")]
    public async Task Preserves_legitimate_speech(string file)
    {
        await fixture.Client.SendAsync(new TranscribeRequest(Guid.NewGuid().ToString("N"), Path.Combine(fixture.Allowed, file)));
        var result = Assert.IsType<ResultMessage>(await fixture.Client.ReadMessageAsync(TimeSpan.FromSeconds(90)));
        Assert.Equal("text", result.Outcome);
    }
}
