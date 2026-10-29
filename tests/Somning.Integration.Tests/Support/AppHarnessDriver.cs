using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace Somning.Integration.Tests.Support;

/// <summary>A replayed harness journal entry (seq is monotonic from App start).</summary>
public sealed record HarnessEvent(long Seq, string Kind, string? Id, JsonObject? Metrics)
{
    public string? Outcome => Metrics?["outcome"]?.GetValue<string>();
    public string? Reason => Metrics?["reason"]?.GetValue<string>();
    public long MetricLong(string name) => Metrics?[name]?.GetValue<long>() ?? 0;
}

public sealed record HarnessStatus(string Engine, string Phase, bool Hook, long LastSeq, bool RecoveryComplete,
    int HookInstallCount = 0, int HookProbeSeen = 0, int WatchdogReinstalls = 0);

/// <summary>
/// Launches Somning.App in harness mode (<c>--test-harness --fixture &lt;wav&gt;</c>) and drives it over the harness
/// pipe <c>\\.\pipe\somning-test</c> (plan 01-11): readiness is awaited as replayable journal events (hook_installed,
/// engine_ready), never by racing the log. Dispose kills the process tree and deletes a driver-owned data dir.
/// </summary>
public sealed class AppHarnessDriver : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EngineTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ReplyMargin = TimeSpan.FromSeconds(15);
    private readonly bool ownsDataDir;
    private readonly SemaphoreSlim exchange = new(1, 1);
    private NamedPipeClientStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;

    private AppHarnessDriver(Process process, string dataDir, bool ownsDataDir)
    {
        Process = process;
        DataDir = dataDir;
        this.ownsDataDir = ownsDataDir;
    }

    public Process Process { get; }
    public string DataDir { get; }
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string RecordingsDir => Path.Combine(DataDir, "recordings");

    /// <summary>The current rolling log file (somning-yyyyMMdd.log), or null before the first write.</summary>
    public string? LogFilePath => Directory.Exists(LogsDir)
        ? Directory.GetFiles(LogsDir, "somning-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;

    /// <param name="dataDir">An existing caller-owned data dir (kept on dispose); null = fresh temp dir owned by the driver.</param>
    public static async Task<AppHarnessDriver> StartAsync(string fixture, IReadOnlyDictionary<string, string>? extraEnv = null,
        bool waitForEngine = true, string? dataDir = null)
    {
        var owns = dataDir is null;
        dataDir ??= Path.Combine(Path.GetTempPath(), "somning-harness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var info = new ProcessStartInfo(RepoPaths.AppExe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--test-harness");
        info.ArgumentList.Add("--fixture");
        info.ArgumentList.Add(Path.GetFullPath(fixture));
        info.Environment["SOMNING_DATA_DIR"] = dataDir;
        if (extraEnv is not null)
            foreach (var pair in extraEnv) info.Environment[pair.Key] = pair.Value;

        var process = Process.Start(info) ?? throw new IOException("Somning.App did not start.");
        var driver = new AppHarnessDriver(process, dataDir, owns);
        try
        {
            await driver.ConnectAsync();
            await driver.WaitEventAsync("hook_installed", 0, null, HookTimeout);
            if (waitForEngine) await driver.WaitEventAsync("engine_ready", 0, null, EngineTimeout);
            return driver;
        }
        catch
        {
            driver.Dispose();
            throw;
        }
    }

    // ---- Pipe protocol ---------------------------------------------------------------------------------------

    /// <summary>Connects (retrying for up to 30 s after start) and verifies the pipe belongs to THIS process.</summary>
    public async Task ConnectAsync()
    {
        CloseConnection();
        var until = DateTime.UtcNow + ConnectTimeout;
        while (true)
        {
            if (Process.HasExited)
                throw new InvalidOperationException($"Somning.App exited ({Process.ExitCode}) before the pipe connected. Log:\n{ReadLog()}");
            var client = new NamedPipeClientStream(".", "somning-test", PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, System.Security.Principal.TokenImpersonationLevel.Identification);
            try
            {
                await client.ConnectAsync(500);
                pipe = client;
                var encoding = new UTF8Encoding(false);
                reader = new StreamReader(client, encoding, false, 4096, leaveOpen: true);
                writer = new StreamWriter(client, encoding, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                var status = await SendAsync(new JsonObject { ["cmd"] = "status" }, TimeSpan.FromSeconds(10));
                var pid = status["pid"]?.GetValue<int>();
                if (pid == Process.Id) return;
                CloseConnection(); // a stale App from another run owns the pipe name
                throw new InvalidOperationException($"Harness pipe is served by pid {pid}, expected {Process.Id}.");
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await client.DisposeAsync();
                CloseConnection();
                if (DateTime.UtcNow > until) throw new TimeoutException($"Harness pipe not available within {ConnectTimeout}. Log:\n{ReadLog()}", ex);
                await Task.Delay(200);
            }
        }
    }

    /// <summary>Sends one JSON line and returns the JSON reply.</summary>
    public async Task<JsonObject> SendAsync(JsonObject request, TimeSpan replyTimeout)
    {
        var reply = await SendRawAsync(request.ToJsonString(), replyTimeout);
        return JsonNode.Parse(reply) as JsonObject ?? throw new InvalidDataException("Reply is not a JSON object: " + reply);
    }

    public async Task<string> SendRawAsync(string line, TimeSpan? replyTimeout = null)
    {
        await exchange.WaitAsync();
        try
        {
            if (writer is null || reader is null) throw new InvalidOperationException("Harness pipe not connected.");
            using var timeout = new CancellationTokenSource(replyTimeout ?? TimeSpan.FromSeconds(10));
            await writer.WriteLineAsync(line.AsMemory(), timeout.Token);
            return await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Harness pipe closed. Log:\n" + ReadLog());
        }
        finally { exchange.Release(); }
    }

    public async Task<HarnessStatus> StatusAsync()
    {
        var reply = await SendAsync(new JsonObject { ["cmd"] = "status" }, TimeSpan.FromSeconds(10));
        return new HarnessStatus(reply["engine"]!.GetValue<string>(), reply["phase"]!.GetValue<string>(),
            reply["hook"]!.GetValue<bool>(), reply["last_seq"]!.GetValue<long>(), reply["recovery_complete"]!.GetValue<bool>(),
            reply["hook_install_count"]?.GetValue<int>() ?? 0, reply["hook_probe_seen"]?.GetValue<int>() ?? 0,
            reply["watchdog_reinstalls"]?.GetValue<int>() ?? 0);
    }

    /// <summary>Headless quick tap through the key channel (no SendInput, no foreground dependency).</summary>
    public async Task InjectTapAsync() => await SendOkAsync(new JsonObject { ["cmd"] = "inject-tap" });

    /// <summary>Changes the live auto-paste switch in memory only (settings.json is not touched).</summary>
    public async Task SetAutoPasteAsync(bool enabled) =>
        await SendOkAsync(new JsonObject { ["cmd"] = "set-auto-paste", ["value"] = enabled });

    private async Task SendOkAsync(JsonObject request)
    {
        var reply = await SendAsync(request, TimeSpan.FromSeconds(10));
        if (reply["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(request["cmd"] + " failed: " + reply.ToJsonString());
    }

    public async Task<long> LastSeqAsync() => (await StatusAsync()).LastSeq;

    /// <summary>First journal entry after <paramref name="afterSeq"/> of the kind (and id); replayed if it already happened.</summary>
    public async Task<HarnessEvent> WaitEventAsync(string kind, long afterSeq, string? id, TimeSpan timeout) =>
        await TryWaitEventAsync(kind, afterSeq, id, timeout)
        ?? throw new TimeoutException($"No '{kind}' event after seq {afterSeq}{(id is null ? "" : " for " + id)} within {timeout}. Log:\n{ReadLog()}");

    /// <summary>Like <see cref="WaitEventAsync"/>, but returns null on timeout.</summary>
    public async Task<HarnessEvent?> TryWaitEventAsync(string kind, long afterSeq, string? id, TimeSpan timeout)
    {
        var reply = await SendAsync(new JsonObject
        {
            ["cmd"] = "wait-event",
            ["kind"] = kind,
            ["after_seq"] = afterSeq,
            ["id"] = id,
            ["timeout_ms"] = (long)timeout.TotalMilliseconds
        }, timeout + ReplyMargin);
        if (reply["error"] is { } error)
        {
            if (error.GetValue<string>() == "timeout") return null;
            throw new InvalidOperationException("wait-event failed: " + reply.ToJsonString());
        }
        return new HarnessEvent(reply["seq"]!.GetValue<long>(), reply["kind"]!.GetValue<string>(),
            reply["id"]?.GetValue<string>(), reply["metrics"] as JsonObject);
    }

    public Task<HarnessEvent> WaitOutcomeAsync(long afterSeq, TimeSpan timeout) => WaitEventAsync("outcome", afterSeq, null, timeout);

    /// <summary>Every retained entry of a kind after a cursor, as currently recorded (short per-step wait).</summary>
    public async Task<IReadOnlyList<HarnessEvent>> EventsAsync(string kind, long afterSeq = 0)
    {
        var found = new List<HarnessEvent>();
        while (await TryWaitEventAsync(kind, afterSeq, null, TimeSpan.FromMilliseconds(50)) is { } next)
        {
            found.Add(next);
            afterSeq = next.Seq;
        }
        return found;
    }

    public Task SetFixtureAsync(string path) => ExpectOkAsync(new JsonObject { ["cmd"] = "set-fixture", ["path"] = Path.GetFullPath(path) });

    public Task ArmDeliveryBarrierAsync() => ExpectOkAsync(new JsonObject { ["cmd"] = "arm-delivery-barrier" });

    public Task ReleaseDeliveryBarrierAsync() => ExpectOkAsync(new JsonObject { ["cmd"] = "release-delivery-barrier" });

    public Task ReleaseRecoveryGateAsync() => ExpectOkAsync(new JsonObject { ["cmd"] = "release-recovery-gate" });

    /// <summary>Asks the App to exit and waits for it (killed after 15 s).</summary>
    public async Task ShutdownAsync()
    {
        if (Process.HasExited) return;
        try { await ExpectOkAsync(new JsonObject { ["cmd"] = "shutdown" }); }
        catch (IOException) { }
        CloseConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await Process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync();
        }
    }

    private async Task ExpectOkAsync(JsonObject request)
    {
        var reply = await SendAsync(request, TimeSpan.FromSeconds(10));
        if (reply["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException($"{request.ToJsonString()} failed: {reply.ToJsonString()}");
    }

    private void CloseConnection()
    {
        writer?.Dispose();
        reader?.Dispose();
        pipe?.Dispose();
        writer = null;
        reader = null;
        pipe = null;
    }

    // ---- Log -------------------------------------------------------------------------------------------------

    /// <summary>Reads the whole log while the app keeps it open for writing.</summary>
    public string ReadLog()
    {
        var path = LogFilePath;
        if (path is null) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var logReader = new StreamReader(stream, Encoding.UTF8);
        return logReader.ReadToEnd();
    }

    public async Task WaitForLogAsync(string text, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (ReadLog().Contains(text, StringComparison.Ordinal)) return;
            if (Process.HasExited)
                throw new InvalidOperationException($"Somning.App exited ({Process.ExitCode}) before '{text}'. Log:\n{ReadLog()}");
            await Task.Delay(100);
        }
        throw new TimeoutException($"'{text}' not logged within {timeout}. Log:\n{ReadLog()}");
    }

    public void Dispose()
    {
        CloseConnection();
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        Process.Dispose();
        exchange.Dispose();
        if (!ownsDataDir) return;
        for (var attempt = 0; attempt < 10 && Directory.Exists(DataDir); attempt++)
        {
            try { Directory.Delete(DataDir, recursive: true); }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}
