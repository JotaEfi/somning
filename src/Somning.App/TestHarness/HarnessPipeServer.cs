#if SOMNING_TEST_HARNESS
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;
using Somning.Core.Dictation;

namespace Somning.App.TestHarness;

/// <summary>Live values reported by the <c>status</c> command.</summary>
public sealed record HarnessStatusSource(Func<string> Engine, Func<DictationState> State,
    Func<int>? HookInstallCount = null, Func<int>? HookProbeSeen = null, Func<int>? WatchdogReinstalls = null);

/// <summary>Headless controls: a synthetic tap on the key channel and a live in-memory auto-paste switch.</summary>
public sealed record HarnessControls(Func<Task> InjectTapAsync, Action<bool> SetAutoPaste);

/// <summary>
/// Debug/SOMNING_TEST_HARNESS-only control channel (D-20, T-01-18): JSON lines over <c>\\.\pipe\somning-test</c>,
/// restricted to the current user (explicit user-SID ACL, per-connection SID check, CurrentUserOnly client). Serves one
/// client at a time and re-listens after a disconnect. <c>wait-event</c> replays from the journal's seq cursor
/// (<c>after_seq</c>), so milestones recorded before the client connected are never lost (review H10).
/// </summary>
public sealed class HarnessPipeServer : IAsyncDisposable
{
    public const string PipeName = "somning-test";
    private const int MaxLineChars = 64 * 1024;
    private const int MaxWaitMs = 10 * 60 * 1000;
    private readonly HarnessJournal journal;
    private readonly HarnessStatusSource status;
    private readonly HarnessFixture fixture;
    private readonly HarnessGate deliveryBarrier;
    private readonly HarnessGate recoveryGate;
    private readonly Action requestShutdown;
    private readonly HarnessControls? controls;
    private readonly CancellationTokenSource lifetime = new();
    private Task serving = Task.CompletedTask;

    public HarnessPipeServer(HarnessJournal journal, HarnessStatusSource status, HarnessFixture fixture,
        HarnessGate deliveryBarrier, HarnessGate recoveryGate, Action requestShutdown, HarnessControls? controls = null)
    {
        this.controls = controls;
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.status = status ?? throw new ArgumentNullException(nameof(status));
        this.fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        this.deliveryBarrier = deliveryBarrier ?? throw new ArgumentNullException(nameof(deliveryBarrier));
        this.recoveryGate = recoveryGate ?? throw new ArgumentNullException(nameof(recoveryGate));
        this.requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
    }

    public void Start() => serving = Task.Run(() => ServeAsync(lifetime.Token), CancellationToken.None);

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                if (!IsCurrentUser(pipe))
                {
                    Log.Warning("Harness pipe rejected a client that is not the current user");
                    pipe.Disconnect();
                    continue;
                }
                await ServeClientAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Harness pipe session ended with an error");
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                // A disconnected client can no longer release what it armed.
                deliveryBarrier.Release();
            }
        }
    }

    /// <summary>
    /// Current-user-only pipe (V4, T-01-18). Server-side <see cref="PipeOptions.CurrentUserOnly"/> cannot be combined
    /// with a PipeSecurity in .NET 10 (ArgumentException), and the ACL it builds grants the token OWNER SID, which is the
    /// Administrators group for an elevated admin. So the pipe gets an explicit protected ACL granting only the current
    /// user SID, every connection is checked against that SID (<see cref="IsCurrentUser"/>), and the client connects
    /// with CurrentUserOnly (it verifies the pipe owner is its own identity).
    /// </summary>
    private static NamedPipeServerStream CreatePipe()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Current user SID unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        if (identity.Owner is { } owner) security.SetOwner(owner);
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    /// <summary>The connected client's token user must equal this process's user SID (client allows Identification).</summary>
    private static bool IsCurrentUser(NamedPipeServerStream pipe)
    {
        using var self = WindowsIdentity.GetCurrent();
        SecurityIdentifier? client = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var impersonated = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                client = impersonated.User;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warning(ex, "Harness pipe could not identify its client");
            return false;
        }
        return self.User is { } user && client is not null && user.Equals(client);
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var encoding = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, encoding, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, encoding, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return;
            var response = line.Length > MaxLineChars ? Error("line_too_long") : await HandleAsync(line, ct).ConfigureAwait(false);
            await writer.WriteLineAsync(response.ToJsonString()).ConfigureAwait(false);
        }
    }

    private async Task<JsonObject> HandleAsync(string line, CancellationToken ct)
    {
        JsonObject request;
        try { request = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Not an object."); }
        catch (JsonException) { return Error("bad_request"); }
        var command = Text(request, "cmd");
        switch (command)
        {
            case "status": return Status();
            case "wait-event": return await WaitEventAsync(request, ct).ConfigureAwait(false);
            case "set-fixture": return SetFixture(request);
            case "arm-delivery-barrier": deliveryBarrier.Arm(); return Ok();
            case "release-delivery-barrier": deliveryBarrier.Release(); return Ok();
            case "release-recovery-gate": recoveryGate.Release(); return Ok();
            case "inject-tap":
                if (controls is null) return Error("unsupported");
                await controls.InjectTapAsync().ConfigureAwait(false);
                return Ok();
            case "set-auto-paste":
                if (controls is null || request["value"] is not JsonValue flag || !flag.TryGetValue<bool>(out var enabled))
                    return Error("bad_request");
                controls.SetAutoPaste(enabled);
                return Ok();
            case "shutdown":
                Log.Information("harness shutdown requested");
                requestShutdown();
                return Ok();
            default: return Error("unknown_command");
        }
    }

    private JsonObject Status()
    {
        var state = status.State();
        var phase = state.Capture is not null ? "Recording"
            : state.InFlight is not null || !state.Pending.IsEmpty ? "Busy" : "Idle";
        return new JsonObject
        {
            ["engine"] = status.Engine(),
            ["phase"] = phase,
            ["hook"] = journal.Contains("hook_installed"),
            ["last_seq"] = journal.LastSeq,
            ["recovery_complete"] = journal.Contains("recovery_complete"),
            ["delivery_barrier_armed"] = deliveryBarrier.IsArmed,
            ["hook_install_count"] = status.HookInstallCount?.Invoke() ?? 0,
            ["hook_probe_seen"] = status.HookProbeSeen?.Invoke() ?? 0,
            ["watchdog_reinstalls"] = status.WatchdogReinstalls?.Invoke() ?? 0,
            ["pid"] = Environment.ProcessId
        };
    }

    private async Task<JsonObject> WaitEventAsync(JsonObject request, CancellationToken ct)
    {
        var kind = Text(request, "kind");
        if (string.IsNullOrWhiteSpace(kind)) return Error("bad_request");
        var afterSeq = Number(request, "after_seq") ?? 0;
        var timeoutMs = Math.Clamp(Number(request, "timeout_ms") ?? 60_000, 0, MaxWaitMs);
        var entry = await journal.WaitAsync(kind, afterSeq, Text(request, "id"), TimeSpan.FromMilliseconds(timeoutMs), ct)
            .ConfigureAwait(false);
        if (entry is null) return Error("timeout");
        var response = new JsonObject { ["seq"] = entry.Seq, ["kind"] = entry.Kind, ["id"] = entry.Id };
        response["metrics"] = entry.Metrics is null ? null : MetricsJson(entry.Metrics);
        return response;
    }

    private static JsonObject MetricsJson(DictationMetrics metrics)
    {
        var json = new JsonObject();
        foreach (var (name, value) in metrics.ToFields())
        {
            json[name] = value switch
            {
                null => null,
                long number => JsonValue.Create(number),
                int number => JsonValue.Create(number),
                _ => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
            };
        }
        return json;
    }

    private JsonObject SetFixture(JsonObject request)
    {
        var path = Text(request, "path");
        if (string.IsNullOrWhiteSpace(path)) return Error("bad_request");
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return Error("invalid_path"); }
        if (!full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return Error("invalid_path");
        fixture.Path = full;
        return Ok();
    }

    private static string? Text(JsonObject request, string name) =>
        request[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Number(JsonObject request, string name) =>
        request[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    private static JsonObject Ok() => new() { ["ok"] = true };

    private static JsonObject Error(string code) => new() { ["error"] = code };

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        deliveryBarrier.Release();
        recoveryGate.Release();
        try { await serving.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug(ex, "Harness pipe stopped"); }
        lifetime.Dispose();
    }
}
#endif
