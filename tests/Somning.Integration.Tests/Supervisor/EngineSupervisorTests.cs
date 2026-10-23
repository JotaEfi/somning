using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Text;
using Somning.Integration.Tests.Engine;
using Somning.Integration.Tests.Support;
using Somning.App.Engine;
using Xunit;

namespace Somning.Integration.Tests.Supervisor;

[Collection("EngineIntegration")]
[Trait("Category", "Engine")]
public sealed class EngineSupervisorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "somning-supervisor", Guid.NewGuid().ToString("N"));
    private readonly string? previousDataRoot = Environment.GetEnvironmentVariable("SOMNING_DATA_DIR");
    private string recordings => Path.Combine(root, "recordings");

    public EngineSupervisorTests() => Environment.SetEnvironmentVariable("SOMNING_DATA_DIR", root);

    private string CopyFixture()
    {
        Requires.Models();
        Directory.CreateDirectory(recordings);
        var wav = Path.Combine(recordings, "hoje.wav");
        File.Copy(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav"), wav);
        return wav;
    }

    private EngineSupervisor Create(string fault) => new(RepoPaths.EngineExe, new SupervisorOptions(
        FirstWorkerEnvironment: new Dictionary<string, string> { ["SOMNING_ENGINE_FAULT"] = fault }));

    [Fact]
    public async Task Killed_worker_restarts_in_cpu_and_retries()
    {
        var wav = CopyFixture();
        await using var supervisor = Create("slow_transcribe");
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.RequestWritten += path => { if (path == wav) written.TrySetResult(); };
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);
        var request = supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken);
        await written.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await supervisor.InferenceStarted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        supervisor.ActiveProcess!.Kill(entireProcessTree: true);
        var result = await request.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        Assert.Equal("text", result.Outcome);
        Assert.Equal("cpu", result.Backend);
        Assert.True(Wer.Compute(File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt")), result.Text) <= .10);
        Assert.True(result.LoadMs > 0);
        Assert.Equal(1, supervisor.RestartCount);
        Assert.True(Environment.ProcessId > 0);
    }

    [Fact]
    public async Task Gpu_crash_fault_restarts_in_cpu()
    {
        await AssertCpuRetry("crash_on_transcribe");
    }

    [Fact]
    public async Task Gpu_error_message_restarts_in_cpu()
    {
        await AssertCpuRetry("gpu_error_on_transcribe");
    }

    [Fact]
    public async Task Request_before_ready_is_queued_until_warm_worker_is_ready()
    {
        var wav = CopyFixture();
        await using var supervisor = new EngineSupervisor(RepoPaths.EngineExe, new SupervisorOptions(
            FirstWorkerEnvironment: new Dictionary<string, string> { ["SOMNING_ENGINE_READY_DELAY_MS"] = "750" }));
        Task start = supervisor.StartAsync("cpu", TestContext.Current.CancellationToken);
        Task<EngineTranscription> request = supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken);
        await start.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var result = await request.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);

        Assert.Equal("text", result.Outcome);
        Assert.True(result.QueueMs >= 500, $"Expected queue time >= 500 ms, got {result.QueueMs} ms.");
        Assert.True(result.WarmMs > 0);
    }

    [Fact]
    public async Task Restart_cap_enters_failed_and_later_requests_fail_fast()
    {
        var missingModels = Path.Combine(root, "missing-models");
        await using var supervisor = new EngineSupervisor(RepoPaths.EngineExe, new SupervisorOptions(
            ModelsDirOverride: missingModels, ZeroBackoff: true));
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(EngineStatus.Failed, supervisor.Status);
        Assert.Equal(3, supervisor.RestartCount);
        var result = await supervisor.TranscribeAsync(Path.Combine(recordings, "never.wav"), TestContext.Current.CancellationToken);
        Assert.Equal("error", result.Outcome);
        Assert.Equal("engine_failed", result.ErrorCode);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task Dispose_during_inference_is_bounded_and_completes_pending_request()
    {
        var wav = CopyFixture();
        var supervisor = Create("slow_transcribe");
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.RequestWritten += path => { if (path == wav) written.TrySetResult(); };
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);
        var request = supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken);
        await written.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await supervisor.InferenceStarted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        await supervisor.DisposeAsync();
        timer.Stop();
        var result = await request.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(6), $"Dispose took {timer.Elapsed}.");
        Assert.Equal("shutdown", result.ErrorCode);
        Assert.True(result.Retryable);
        await WaitForNoEngineProcessesAsync();
    }

    [Fact]
    public async Task Request_during_background_recovery_waits_for_cpu_worker()
    {
        var wav = CopyFixture();
        await using var supervisor = new EngineSupervisor(RepoPaths.EngineExe);
        TaskCompletionSource restarting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.StatusChanged += status => { if (status == EngineStatus.Restarting) restarting.TrySetResult(); };
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);
        supervisor.ActiveProcess!.Kill(entireProcessTree: true);
        await restarting.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var result = await supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        Assert.Equal("text", result.Outcome);
        Assert.Equal("cpu", result.Backend);
        Assert.True(result.QueueMs > 0);
    }

    [Fact]
    public async Task Dispose_during_restart_backoff_does_not_launch_another_worker()
    {
        var wav = CopyFixture();
        var supervisor = Create("gpu_error_on_transcribe");
        TaskCompletionSource restarting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.StatusChanged += status => { if (status == EngineStatus.Restarting) restarting.TrySetResult(); };
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);
        var recoveredResult = await supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        Assert.Equal("cpu", recoveredResult.Backend);

        restarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.ActiveProcess!.Kill(entireProcessTree: true);
        await restarting.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await supervisor.DisposeAsync();
        await WaitForNoEngineProcessesAsync();
        Assert.Equal(EngineStatus.Restarting, supervisor.Status);
    }

    [Fact]
    public async Task Cancelling_request_during_recovery_does_not_cancel_cpu_restart()
    {
        var wav = CopyFixture();
        await using var supervisor = Create("gpu_error_on_transcribe");
        using var cancellation = new CancellationTokenSource();
        supervisor.StatusChanged += status =>
        {
            if (status == EngineStatus.Restarting && supervisor.RestartCount == 1) cancellation.Cancel();
        };
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => supervisor.TranscribeAsync(wav, cancellation.Token));
        Assert.Equal(EngineStatus.Ready, supervisor.Status);
        Assert.Equal("cpu", supervisor.ActiveBackend);
    }

    private async Task AssertCpuRetry(string fault)
    {
        var wav = CopyFixture();
        await using var supervisor = Create(fault);
        await supervisor.StartAsync("vulkan", TestContext.Current.CancellationToken);
        var result = await supervisor.TranscribeAsync(wav, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        Assert.Equal("text", result.Outcome);
        Assert.Equal("cpu", result.Backend);
        Assert.True(Wer.Compute(File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt")), result.Text) <= .10);
        Assert.True(result.LoadMs > 0);
        Assert.Equal(1, supervisor.RestartCount);
        Assert.True(Environment.ProcessId > 0);
    }

    private static async Task WaitForNoEngineProcessesAsync()
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("Somning.Engine");
            if (processes.Length == 0) return;
            foreach (var process in processes) process.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
        Assert.Empty(System.Diagnostics.Process.GetProcessesByName("Somning.Engine"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SOMNING_DATA_DIR", previousDataRoot);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
