using System.Diagnostics;
using System.Text;
using Serilog;
using Somning.Core.Engine;
using Somning.Engine.Protocol;
using Somning.Platform.Windows.Processes;

namespace Somning.App.Engine;

/// <summary>Owns one isolated engine process and its protocol readers.</summary>
internal sealed class EngineProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly JobObject job;
    private readonly Task stdout;
    private readonly Task stderr;
    private readonly TaskCompletionSource<EngineSessionInfo> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource inferenceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

    private EngineProcess(Process process, JobObject job)
    {
        this.process = process;
        this.job = job;
        stdout = ReadOutputAsync();
        stderr = ReadErrorsAsync();
        _ = WatchExitAsync();
    }

    public Task<EngineSessionInfo> ReadyTask => ready.Task;
    public Task InferenceStarted => inferenceStarted.Task;
    public Task Exited => exited.Task;
    public Process Process => process;
    public event Action<EngineMessage>? MessageReceived;
    public void FailReady(Exception error) => ready.TrySetException(error);

    public static EngineProcess Start(ProcessStartInfo startInfo)
    {
        var job = JobObject.CreateKillOnClose();
        Process? process = null;
        try
        {
            process = Process.Start(startInfo) ?? throw new IOException("Worker did not start.");
            job.Assign(process);
            return new EngineProcess(process, job);
        }
        catch
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.Dispose();
            }
            job.Dispose();
            throw;
        }
    }

    public async Task SendAsync(EngineMessage message, CancellationToken ct)
    {
        await process.StandardInput.WriteLineAsync(JsonLines.Serialize(message).AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }

    public async Task WaitForExitAsync(CancellationToken ct = default) => await process.WaitForExitAsync(ct);
    public void Kill() { if (!process.HasExited) process.Kill(entireProcessTree: true); }

    private async Task ReadOutputAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!JsonLines.TryDeserialize(line, out var message, out _))
                { Log.Warning("Invalid worker protocol line"); continue; }
                if (message is ReadyMessage r && r.Backend is "cpu" or "vulkan" && !string.IsNullOrWhiteSpace(r.Model)
                    && double.IsFinite(r.LoadMs) && r.LoadMs >= 0 && double.IsFinite(r.WarmMs) && r.WarmMs >= 0)
                    ready.TrySetResult(new(r.Backend, r.Model, (long)r.LoadMs, (long)r.WarmMs, r.GpuInUse, r.FlashAttention));
                if (message is not null) MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Log.Debug("Worker output reader ended"); }
        finally { ready.TrySetException(new IOException("Engine exited before ready.")); }
    }

    private async Task ReadErrorsAsync()
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (line == "somning-test:inference-started") inferenceStarted.TrySetResult();
                else Log.Debug("Worker native {NativeLog}", line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Log.Debug("Worker diagnostics ended"); }
    }

    private async Task WatchExitAsync()
    {
        try { await process.WaitForExitAsync(); }
        catch (InvalidOperationException) { }
        finally { exited.TrySetResult(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        job.Dispose(); // KILL_ON_JOB_CLOSE is the final orphan guard.
        try { await Task.WhenAll(stdout, stderr); } catch (IOException) { }
        process.Dispose();
    }
}
