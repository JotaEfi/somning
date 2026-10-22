using Somning.Core.Text;
using Somning.Engine.Protocol;
using Somning.Integration.Tests.Support;
using Xunit;
namespace Somning.Integration.Tests.Engine;
public sealed class EngineWorkerTests : IDisposable
{
    private readonly string allowed = Path.Combine(Path.GetTempPath(), "somning-tests", Guid.NewGuid().ToString("N"));
    private string Fixture()
    {
        Requires.Models(); Directory.CreateDirectory(allowed);
        var wav = Path.Combine(allowed, "hoje.wav");
        File.Copy(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav"), wav, true);
        return wav;
    }
    [Fact]
    public async Task Transcribes_tracer_fixture()
    {
        var wav = Fixture();
        using var client = new EngineWorkerClient(allowed);
        var ready = await client.ReadyAsync();
        Assert.Equal("cpu", ready.Backend); Assert.True(ready.WarmMs > 0);
        await client.SendAsync(new TranscribeRequest("tracer", wav));
        var result = Assert.IsType<ResultMessage>(await client.ReadMessageAsync(TimeSpan.FromSeconds(180)));
        Assert.Equal("text", result.Outcome);
        var expected = File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));
        Assert.True(Wer.Compute(expected, result.Text) <= .1, result.Text);
        await client.SendAsync(new ShutdownRequest());
        Assert.Equal(0, await client.WaitForExitAsync(TimeSpan.FromSeconds(10)));
    }
    [Fact]
    public async Task Cancel_active_request()
    {
        var wav = Fixture();
        using var client = new EngineWorkerClient(allowed, fault: "slow_transcribe");
        await client.ReadyAsync();
        await client.SendAsync(new TranscribeRequest("A", wav));
        await client.SendAsync(new CancelRequest("A"));
        var error = Assert.IsType<ErrorMessage>(await client.ReadMessageAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("A", error.Id); Assert.Equal(ErrorCodes.Cancelled, error.Code);
        await client.SendAsync(new ShutdownRequest());
        Assert.Equal(0, await client.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task Cancel_queued_request()
    {
        var wav = Fixture();
        using var client = new EngineWorkerClient(allowed, fault: "slow_transcribe");
        await client.ReadyAsync();
        await client.SendAsync(new TranscribeRequest("A", wav));
        await client.SendAsync(new TranscribeRequest("B", wav));
        await client.SendAsync(new CancelRequest("B"));
        var error = Assert.IsType<ErrorMessage>(await client.ReadMessageAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("B", error.Id); Assert.Equal(ErrorCodes.Cancelled, error.Code);
        await client.SendAsync(new ShutdownRequest());
        Assert.Equal(0, await client.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task Shutdown_during_inference_is_bounded()
    {
        var wav = Fixture();
        using var client = new EngineWorkerClient(allowed, fault: "slow_transcribe");
        await client.ReadyAsync();
        await client.SendAsync(new TranscribeRequest("A", wav));
        await client.SendAsync(new ShutdownRequest());
        Assert.Equal(0, await client.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task Sibling_prefix_path_is_rejected()
    {
        Fixture();
        using var client = new EngineWorkerClient(allowed);
        await client.ReadyAsync();
        await client.SendAsync(new TranscribeRequest("invalid", allowed + "-other/hoje.wav"));
        var error = Assert.IsType<ErrorMessage>(await client.ReadMessageAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(ErrorCodes.InvalidPath, error.Code);
        await client.SendAsync(new ShutdownRequest());
        Assert.Equal(0, await client.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
    public void Dispose() { if (Directory.Exists(allowed)) Directory.Delete(allowed, true); }
}
