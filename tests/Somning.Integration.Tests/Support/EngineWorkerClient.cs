using System.Diagnostics;
using System.Text;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Engine.Protocol;
namespace Somning.Integration.Tests.Support;
public sealed class EngineWorkerClient : IDisposable
{
    private readonly Process process;
    private readonly Task<string> stderr;
    public EngineWorkerClient(string allowedDir, string backend = "cpu", string? fault = null)
    {
        var info = new ProcessStartInfo(RepoPaths.EngineExe)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "--backend", backend, "--model", Path.Combine(SomningPaths.ModelsDir, EngineDefaults.ModelFile),
            "--vad", Path.Combine(SomningPaths.ModelsDir, EngineDefaults.VadModelFile), "--flash-attn", "on",
            "--threads", EngineDefaults.Threads.ToString(), "--allowed-dir", allowedDir }) info.ArgumentList.Add(arg);
        if (fault is not null) info.Environment["SOMNING_ENGINE_FAULT"] = fault;
        process = Process.Start(info) ?? throw new InvalidOperationException("Engine did not start.");
        stderr = process.StandardError.ReadToEndAsync();
    }
    public async Task<ReadyMessage> ReadyAsync() => await ReadMessageAsync(TimeSpan.FromSeconds(120))
        as ReadyMessage ?? throw new InvalidDataException("Expected ready.");
    public async Task SendAsync(EngineMessage message)
    { await process.StandardInput.WriteLineAsync(JsonLines.Serialize(message)); await process.StandardInput.FlushAsync(); }
    public async Task<EngineMessage> ReadMessageAsync(TimeSpan timeout)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(timeout);
        if (line is null) throw new EndOfStreamException("Worker exited: " + await stderr);
        return JsonLines.TryDeserialize(line, out var message, out _) ? message!
            : throw new InvalidDataException("Invalid worker protocol.");
    }
    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    { await process.WaitForExitAsync().WaitAsync(timeout); return process.ExitCode; }
    public void Dispose()
    { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } process.Dispose(); }
}
