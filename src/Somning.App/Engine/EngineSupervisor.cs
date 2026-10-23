using System.Diagnostics;
using System.Text;
using Serilog;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Ports;
using Somning.Engine.Protocol;

namespace Somning.App.Engine;

public sealed class EngineSupervisor(string? enginePath = null, SupervisorOptions? options = null) : ITranscriber
{
    private readonly SupervisorOptions options = options ?? new SupervisorOptions();
    private readonly IClock clock = options?.Clock ?? new SystemClock();
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly SemaphoreSlim restartGate = new(1, 1);
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource lifetime = new();
    private EngineProcess? worker;
    private EngineSessionInfo? session;
    private Task startTask = Task.CompletedTask;
    private TaskCompletionSource<EngineMessage>? pending;
    private string? pendingRequestId;
    private string? requestedBackend;
    private bool disposing;
    private bool firstWorker = true;
    private TaskCompletionSource? activeRecovery;
    private int restartCount;
    private RestartPolicy restartPolicy = new();

    public EngineStatus Status { get; private set; } = EngineStatus.Starting;
    public event Action<EngineStatus>? StatusChanged;
    public event Action<string>? RequestWritten;
    public string? ActiveBackend => session?.Backend;
    public int RestartCount => Volatile.Read(ref restartCount);
    internal Process? ActiveProcess => worker?.Process;
    internal Task InferenceStarted => worker?.InferenceStarted ?? Task.CompletedTask;

    private void SetStatus(EngineStatus status)
    {
        lock (lifecycle) { Status = status; StatusChanged?.Invoke(status); }
    }

    public Task StartAsync(string backend, CancellationToken ct)
    {
        if (backend is not ("cpu" or "vulkan")) throw new ArgumentException("Invalid backend.", nameof(backend));
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            if (worker is not null || startTask != Task.CompletedTask) throw new InvalidOperationException("Engine already started.");
            requestedBackend = backend;
            startTask = StartInitialAsync(backend, ct);
            return startTask;
        }
    }

    private async Task StartInitialAsync(string backend, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        string nextBackend = backend;
        bool initial = true;
        while (!disposing && !linked.IsCancellationRequested)
        {
            try
            {
                await StartWorkerAsync(nextBackend, linked.Token, initial);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                Log.Warning(ex, "Engine worker startup failed");
                var decision = DecideRestart(nextBackend, ex.Message);
                if (decision is RestartDecision.GiveUp)
                {
                    SetStatus(EngineStatus.Failed);
                    return;
                }
                var restart = (RestartDecision.Restart)decision;
                if (!options.ZeroBackoff && restart.Delay > TimeSpan.Zero) await Task.Delay(restart.Delay, linked.Token);
                nextBackend = restart.Backend;
                initial = false;
            }
        }
    }

    private async Task StartWorkerAsync(string backend, CancellationToken ct, bool initial)
    {
        SetStatus(initial ? EngineStatus.Starting : EngineStatus.Restarting);
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        string modelsDir = options.ModelsDirOverride ?? SomningPaths.ModelsDir;
        var info = new ProcessStartInfo(EngineLocator.Find(enginePath))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        string model = backend == "cpu" ? EngineDefaults.CpuFallbackModelFile : EngineDefaults.ModelFile;
        foreach (var arg in new[] { "--backend", backend, "--model", Path.Combine(modelsDir, model),
            "--vad", Path.Combine(modelsDir, EngineDefaults.VadModelFile), "--flash-attn",
            EngineDefaults.FlashAttention ? "on" : "off", "--threads", EngineDefaults.Threads.ToString(),
            "--allowed-dir", SomningPaths.RecordingsDir }) info.ArgumentList.Add(arg);
        info.Environment["VK_LOADER_LAYERS_DISABLE"] = "~implicit~";
        info.Environment.Remove("SOMNING_ENGINE_FAULT");
        info.Environment.Remove("SOMNING_ENGINE_READY_DELAY_MS");
        if (firstWorker && options.FirstWorkerEnvironment is not null)
            foreach (var pair in options.FirstWorkerEnvironment) info.Environment[pair.Key] = pair.Value;
        firstWorker = false;

        var next = EngineProcess.Start(info);
        worker = next;
        next.MessageReceived += message => OnMessage(next, message);
        try
        {
            session = await next.ReadyTask.WaitAsync(options.EffectiveReadinessTimeout, ct);
            if (next.Process.HasExited) throw new IOException("Worker exited during startup.");
            SetStatus(EngineStatus.Ready);
            _ = MonitorWorkerExitAsync(next);
        }
        catch
        {
            if (ReferenceEquals(worker, next)) worker = null;
            await next.DisposeAsync();
            throw;
        }
    }

    public async Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
    {
        long queuedAt = clock.NowMs;
        if (Status == EngineStatus.Failed) return Failed("engine_failed");
        await requestGate.WaitAsync(ct);
        try
        {
            if (Status == EngineStatus.Failed) return Failed("engine_failed");
            if (!await EnsureWorkerAvailableAsync(ct)) return Failed(disposing ? "shutdown" : "engine_failed");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var current = worker;
                var currentSession = session;
                if (current is null || currentSession is null) return Failed("engine_failed");
                var (result, queueMs) = await RunRequestAsync(current, wavPath, queuedAt, ct);
                if (result is not null)
                {
                    if (result is ErrorMessage { Code: ErrorCodes.GpuError or ErrorCodes.BackendInitFailed })
                    {
                        bool recovered = await RestartAsync(current, "worker reported " + ((ErrorMessage)result).Code);
                        if (attempt == 0 && recovered) continue;
                        return Failed(recovered ? "engine_retry_failed" : "engine_failed");
                    }
                    return Map(result, currentSession, queueMs);
                }
                if (attempt == 0)
                {
                    bool recovered = await RestartAsync(current, "worker exited or request timed out");
                    if (attempt == 0 && recovered) continue;
                    return Failed(recovered ? "engine_retry_failed" : "engine_failed");
                }
                return Failed("engine_retry_failed");
            }
            return Failed("engine_retry_failed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (disposing) { return Failed("shutdown"); }
        catch (ObjectDisposedException) when (disposing) { return Failed("shutdown"); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Warning(ex, "Engine request could not be completed");
            return Failed("engine_retry_failed");
        }
        finally { requestGate.Release(); }
    }

    private async Task<bool> EnsureWorkerAvailableAsync(CancellationToken ct)
    {
        await startTask.WaitAsync(options.EffectiveReadinessTimeout, ct);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (Status == EngineStatus.Ready && worker is not null && session is not null) return true;
            if (Status == EngineStatus.Failed || disposing) return false;
            Task? recovery;
            lock (lifecycle) recovery = activeRecovery?.Task;
            if (recovery is null) return Status == EngineStatus.Ready && worker is not null && session is not null;
            TimeSpan remaining = options.EffectiveReadinessTimeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("Engine did not become ready before the timeout.");
            await recovery.WaitAsync(remaining, ct);
        }
    }

    private async Task<(EngineMessage? Message, long QueueMs)> RunRequestAsync(EngineProcess current, string wavPath, long queuedAt, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<EngineMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        pendingRequestId = id;
        try
        {
            try { await SendAsync(current, new TranscribeRequest(id, wavPath), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            { return (null, 0); }
            long queueMs = QueueMs(queuedAt);
            RequestWritten?.Invoke(wavPath);
            double audioSeconds = Math.Max(0, new FileInfo(wavPath).Length - 44) / 32000d;
            TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(options.EffectiveMinRequestTimeout.TotalSeconds,
                options.RequestTimeoutFactor * audioSeconds));
            var winner = await Task.WhenAny(completion.Task, current.Exited, Task.Delay(timeout, ct));
            if (winner == completion.Task) return (await completion.Task, queueMs);
            if (winner == current.Exited) return (null, queueMs);
            ct.ThrowIfCancellationRequested();
            return (null, queueMs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { await SendAsync(current, new CancelRequest(id), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ObjectDisposedException)
            { Log.Debug("Worker cancellation unavailable"); }
            throw;
        }
        finally { pending = null; pendingRequestId = null; }
    }

    private void OnMessage(EngineProcess source, EngineMessage message)
    {
        if (!ReferenceEquals(worker, source)) return;
        if (message is ErrorMessage { Id: null, Code: not null } startupError)
        {
            // Model/backend startup errors are surfaced through readiness failure.
            worker?.FailReady(new IOException("Engine initialization error: " + startupError.Code));
            return;
        }
        var current = pending;
        if (current is null) return;
        string? id = message switch { ResultMessage result => result.Id, ErrorMessage { Id: not null } error => error.Id, _ => null };
        if (id is not null && string.Equals(id, pendingRequestId, StringComparison.Ordinal)) current.TrySetResult(message);
    }

    private async Task MonitorWorkerExitAsync(EngineProcess source)
    {
        await source.Exited;
        if (disposing || !ReferenceEquals(worker, source) || Status != EngineStatus.Ready) return;
        try { await RestartAsync(source, "worker process exited"); }
        catch (OperationCanceledException) when (disposing) { }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ObjectDisposedException)
        { Log.Warning(ex, "Background engine recovery failed"); SetStatus(EngineStatus.Failed); }
    }

    private async Task<bool> RestartAsync(EngineProcess failedWorker, string reason)
    {
        CancellationToken recoveryToken = lifetime.Token;
        await restartGate.WaitAsync(recoveryToken);
        TaskCompletionSource? recovery = null;
        try
        {
            if (!ReferenceEquals(worker, failedWorker)) return Status == EngineStatus.Ready;
            recovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (lifecycle)
            {
                activeRecovery = recovery;
                SetStatus(EngineStatus.Restarting);
            }
            var old = worker;
            worker = null;
            session = null;
            if (old is not null) await old.DisposeAsync();
            string backend = "cpu";
            string failureReason = reason;
            while (!disposing && !recoveryToken.IsCancellationRequested)
            {
                var decision = DecideRestart(backend, failureReason);
                if (decision is RestartDecision.GiveUp)
                {
                    Log.Error("engine failed: restart cap reached");
                    SetStatus(EngineStatus.Failed);
                    return false;
                }
                var restart = (RestartDecision.Restart)decision;
                if (!options.ZeroBackoff && restart.Delay > TimeSpan.Zero) await Task.Delay(restart.Delay, recoveryToken);
                if (disposing || recoveryToken.IsCancellationRequested) return false;
                backend = restart.Backend;
                try
                {
                    await StartWorkerAsync(backend, recoveryToken, initial: false);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
                {
                    Log.Warning(ex, "CPU worker restart failed");
                    failureReason = ex.Message;
                    old = worker;
                    worker = null;
                    session = null;
                    if (old is not null) await old.DisposeAsync();
                }
            }
            if (!disposing) SetStatus(EngineStatus.Failed);
            return false;
        }
        finally
        {
            if (recovery is not null)
            {
                lock (lifecycle)
                {
                    if (ReferenceEquals(activeRecovery, recovery)) activeRecovery = null;
                    recovery.TrySetResult();
                }
            }
            restartGate.Release();
        }
    }

    private RestartDecision DecideRestart(string backend, string reason)
    {
        var (next, decision) = restartPolicy.OnFailure(clock.NowMs, backend, reason);
        restartPolicy = next;
        if (decision is RestartDecision.Restart) Interlocked.Increment(ref restartCount);
        return decision;
    }

    private async Task SendAsync(EngineProcess target, EngineMessage message, CancellationToken ct)
    {
        await writes.WaitAsync(ct);
        try { await target.SendAsync(message, ct); }
        finally { writes.Release(); }
    }

    private long QueueMs(long queuedAt) => Math.Max(0, clock.NowMs - queuedAt);
    private static EngineTranscription Map(EngineMessage message, EngineSessionInfo current, long queueMs) => message switch
    {
        ResultMessage result => ProtocolMapping.Map(result, current, queueMs),
        ErrorMessage error => ProtocolMapping.Map(error, current) with { QueueMs = queueMs },
        _ => Failed("engine_protocol_error")
    };
    private static EngineTranscription Failed(string code) => new("", "error", null, code, true, 0, 0, 0, 0, 0, 0, "cpu", "", 0);

    public async ValueTask DisposeAsync()
    {
        if (disposing) return;
        disposing = true;
        lifetime.Cancel();
        var current = worker;
        if (pending is { } request) request.TrySetResult(new ErrorMessage("shutdown", "shutdown", ""));
        try { await startTask.WaitAsync(options.EffectiveShutdownTimeout); }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        { Log.Debug("Engine startup ended during shutdown"); }
        await restartGate.WaitAsync();
        current = worker;
        if (current is null) { restartGate.Release(); lifetime.Dispose(); return; }
        try
        {
            if (!current.Process.HasExited) await ShutdownWorkerAsync(current).WaitAsync(options.EffectiveShutdownTimeout);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ObjectDisposedException)
        { current.Kill(); try { await current.WaitForExitAsync(); } catch (InvalidOperationException) { } }
        finally
        {
            await current.DisposeAsync();
            worker = null;
            restartGate.Release();
            lifetime.Dispose();
        }
    }

    private async Task ShutdownWorkerAsync(EngineProcess current)
    {
        await SendAsync(current, new ShutdownRequest(), CancellationToken.None);
        await current.WaitForExitAsync();
    }
}
