using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Serilog;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Ports;
using Somning.Engine.Protocol;
namespace Somning.App.Engine;
public sealed class EngineSupervisor(string? enginePath = null) : ITranscriber
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<EngineMessage>> pending = new();
    private readonly TaskCompletionSource<EngineSessionInfo> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim writes = new(1, 1);
    private Process? process;
    private Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
    private EngineSessionInfo? session;
    private bool disposing;
    private bool outputEnded;
    private readonly object lifecycle = new();
    public EngineStatus Status { get; private set; } = EngineStatus.Starting;
    public event Action<EngineStatus>? StatusChanged;
    public string? ActiveBackend => session?.Backend;
    private void SetStatus(EngineStatus status)
    { lock (lifecycle) { Status = status; StatusChanged?.Invoke(status); } }
    public async Task StartAsync(string backend, CancellationToken ct)
    {
        if (process is not null) throw new InvalidOperationException("Engine already started.");
        if (backend is not ("cpu" or "vulkan")) throw new ArgumentException("Invalid backend.", nameof(backend));
        SetStatus(EngineStatus.Starting);
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        var info = new ProcessStartInfo(EngineLocator.Find(enginePath))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "--backend", backend, "--model", Path.Combine(SomningPaths.ModelsDir, EngineDefaults.ModelFile),
            "--vad", Path.Combine(SomningPaths.ModelsDir, EngineDefaults.VadModelFile), "--flash-attn",
            EngineDefaults.FlashAttention ? "on" : "off", "--threads", EngineDefaults.Threads.ToString(),
            "--allowed-dir", SomningPaths.RecordingsDir }) info.ArgumentList.Add(arg);
        info.Environment["VK_LOADER_LAYERS_DISABLE"] = "~implicit~";
        try
        {
            process = Process.Start(info) ?? throw new IOException("Worker did not start.");
            stdout = ReadOutputAsync(process); stderr = ReadErrorsAsync(process);
            var startedSession = await ready.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            lock (lifecycle)
            {
                if (outputEnded || process.HasExited) throw new IOException("Worker exited during startup.");
                session = startedSession; SetStatus(EngineStatus.Ready);
            }
        }
        catch { SetStatus(EngineStatus.Failed); throw; }
    }
    public async Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
    {
        var current = session ?? throw new InvalidOperationException("Engine is not ready.");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<EngineMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate request id.");
        using var registration = ct.Register(() => _ = CancelAsync(id));
        try
        {
            await SendAsync(new TranscribeRequest(id, wavPath), ct);
            var response = await completion.Task.WaitAsync(TimeSpan.FromMinutes(5), ct);
            return response switch
            {
                ResultMessage result => ProtocolMapping.Map(result, current),
                ErrorMessage error => ProtocolMapping.Map(error, current),
                _ => throw new InvalidDataException("Unexpected worker response.")
            };
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task CancelAsync(string id)
    {
        try { await SendAsync(new CancelRequest(id), CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        { Log.Debug("Worker cancellation unavailable"); }
    }
    private async Task SendAsync(EngineMessage message, CancellationToken ct)
    {
        var child = process ?? throw new InvalidOperationException("Engine is not running.");
        await writes.WaitAsync(ct);
        try
        {
            await child.StandardInput.WriteLineAsync(JsonLines.Serialize(message).AsMemory(), ct);
            await child.StandardInput.FlushAsync(ct);
        }
        finally { writes.Release(); }
    }
    private async Task ReadOutputAsync(Process child)
    {
        try
        {
            while (await child.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!JsonLines.TryDeserialize(line, out var message, out _))
                { Log.Warning("Invalid worker protocol line"); continue; }
                switch (message)
                {
                    case ReadyMessage r when r.Backend is "cpu" or "vulkan" && !string.IsNullOrWhiteSpace(r.Model)
                        && double.IsFinite(r.LoadMs) && r.LoadMs >= 0 && double.IsFinite(r.WarmMs) && r.WarmMs >= 0:
                        ready.TrySetResult(new(r.Backend, r.Model, (long)r.LoadMs, (long)r.WarmMs, r.GpuInUse, r.FlashAttention)); break;
                    case ResultMessage r when !string.IsNullOrWhiteSpace(r.Id) && r.Text is not null
                        && r.Outcome is "text" or "empty":
                        if (pending.TryGetValue(r.Id, out var completion)) completion.TrySetResult(r);
                        break;
                    case ErrorMessage e when e.Id is not null && pending.TryGetValue(e.Id, out var errorCompletion):
                        errorCompletion.TrySetResult(e); break;
                    case ErrorMessage e when e.Id is null && !string.IsNullOrWhiteSpace(e.Code):
                        ready.TrySetException(new IOException("Engine initialization error: " + e.Code)); break;
                    default: Log.Warning("Unexpected or malformed worker response"); break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { Log.Debug("Worker output reader ended"); }
        finally
        {
            var error = new IOException("Engine exited before completing request.");
            ready.TrySetException(error);
            foreach (var request in pending.Values) request.TrySetException(error);
            lock (lifecycle)
            {
                outputEnded = true;
                if (!disposing) SetStatus(EngineStatus.Failed);
            }
        }
    }
    private static async Task ReadErrorsAsync(Process child)
    {
        try
        {
            while (await child.StandardError.ReadLineAsync() is { } line) Log.Debug("Worker native {NativeLog}", line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Log.Debug("Worker diagnostics ended"); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposing) return;
        disposing = true;
        var child = process;
        if (child is null) return;
        try
        {
            if (!child.HasExited) await SendAsync(new ShutdownRequest(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
        finally
        {
            await Task.WhenAll(stdout, stderr);
            child.Dispose(); writes.Dispose();
        }
    }
}
