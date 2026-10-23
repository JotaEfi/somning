using System.Diagnostics;
using System.Text;
using Somning.Engine.Protocol;

namespace Somning.Bench;

internal sealed class BenchEngineClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly StreamWriter input;
    private readonly Task<string> stderr;
    private BenchEngineClient(Process process)
    {
        this.process = process;
        input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        stderr = process.StandardError.ReadToEndAsync();
    }

    public static async Task<(BenchEngineClient? Client, ReadyMessage? Ready, string? Diagnostic)> StartAsync(
        string engine, string stage, string backend, string model, string vad, bool flash)
    {
        var start = new ProcessStartInfo(engine)
        {
            WorkingDirectory = stage, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var pair in new Dictionary<string, string>
        {
            ["--backend"] = backend, ["--model"] = model, ["--vad"] = vad,
            ["--flash-attn"] = flash ? "on" : "off", ["--allowed-dir"] = Path.Combine(stage, "fixtures")
        }) { start.ArgumentList.Add(pair.Key); start.ArgumentList.Add(pair.Value); }
        start.Environment["PATH"] = @"C:\Windows\system32;C:\Windows";
        start.Environment["DOTNET_ROOT"] = Path.Combine(stage, "no-sdk");
        start.Environment["DOTNET_ROOT_X64"] = Path.Combine(stage, "no-sdk");
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        foreach (var name in new[] { "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "VULKAN_SDK", "VK_LAYER_PATH", "SOMNING_ENGINE_FAULT" }) start.Environment.Remove(name);
        var p = new Process { StartInfo = start };
        p.Start();
        var client = new BenchEngineClient(p);
        var line = await p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(3));
        if (line is null || !JsonLines.TryDeserialize(line, out var message, out _) || message is not ReadyMessage ready)
        {
            var error = line ?? "engine exited before ready";
            if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); }
            return (null, null, error + " " + await client.stderr);
        }
        return (client, ready, null);
    }

    public async Task<ResultMessage> TranscribeAsync(string id, string wav)
    {
        var req = new TranscribeRequest(id, Path.GetFullPath(wav));
        await input.WriteLineAsync(JsonLines.Serialize(req));
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(3))
                ?? throw new InvalidOperationException("engine exited before result");
            if (!JsonLines.TryDeserialize(line, out var message, out _)) throw new InvalidOperationException("invalid engine protocol response");
            if (message is ErrorMessage error) throw new InvalidOperationException($"engine error {error.Code}: {error.Message}");
            if (message is ResultMessage result && result.Id == id) return result;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await input.WriteLineAsync(JsonLines.Serialize(new ShutdownRequest())); } catch (IOException) { }
        input.Dispose();
        if (!process.WaitForExit(10000)) { process.Kill(true); await process.WaitForExitAsync(); }
        process.Dispose();
    }
}
