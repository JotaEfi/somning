using System.Diagnostics;
using System.Text;

namespace Somning.Integration.Tests.Support;

/// <summary>
/// Launches Somning.App in harness mode (<c>--test-harness --fixture &lt;wav&gt;</c>) with a fresh SOMNING_DATA_DIR and
/// waits on its log for "hook installed" (and "engine ready" when asked). Dispose kills the process tree and
/// deletes the data dir. Plan 01-11 extends this with the harness pipe client.
/// </summary>
public sealed class AppHarnessDriver : IDisposable
{
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EngineTimeout = TimeSpan.FromSeconds(120);

    private AppHarnessDriver(Process process, string dataDir)
    {
        Process = process;
        DataDir = dataDir;
    }

    public Process Process { get; }
    public string DataDir { get; }
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string RecordingsDir => Path.Combine(DataDir, "recordings");

    /// <summary>The current rolling log file (somning-yyyyMMdd.log), or null before the first write.</summary>
    public string? LogFilePath => Directory.Exists(LogsDir)
        ? Directory.GetFiles(LogsDir, "somning-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;

    public static async Task<AppHarnessDriver> StartAsync(string fixture, IReadOnlyDictionary<string, string>? extraEnv = null,
        bool waitForEngine = true)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "somning-harness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var info = new ProcessStartInfo(RepoPaths.AppExe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--test-harness");
        info.ArgumentList.Add("--fixture");
        info.ArgumentList.Add(Path.GetFullPath(fixture));
        info.Environment["SOMNING_DATA_DIR"] = dataDir;
        if (extraEnv is not null)
            foreach (var pair in extraEnv) info.Environment[pair.Key] = pair.Value;

        var process = Process.Start(info) ?? throw new IOException("Somning.App did not start.");
        var driver = new AppHarnessDriver(process, dataDir);
        try
        {
            await driver.WaitForLogAsync("hook installed", HookTimeout);
            if (waitForEngine) await driver.WaitForLogAsync("engine ready", EngineTimeout);
            return driver;
        }
        catch
        {
            driver.Dispose();
            throw;
        }
    }

    /// <summary>Reads the whole log while the app keeps it open for writing.</summary>
    public string ReadLog()
    {
        var path = LogFilePath;
        if (path is null) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
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
        for (var attempt = 0; attempt < 10 && Directory.Exists(DataDir); attempt++)
        {
            try { Directory.Delete(DataDir, recursive: true); }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}
