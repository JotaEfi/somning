using System.Diagnostics;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Xunit;
namespace Somning.Integration.Tests.Skeleton;
public sealed class SkeletonRoundTripTests
{
    [Fact]
    public async Task Transcribe_file_puts_text_on_clipboard()
    {
        Requires.Models(); Requires.Desktop();
        var (exit, _) = await RunAppAsync();
        Assert.Equal(0, exit);
        var expected = File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));
        var actual = ClipboardProbe.ReadText();
        Assert.True(Wer.Compute(expected, actual) <= .1, actual);
    }
    internal static async Task<(int Exit, string Log)> RunAppAsync()
    {
        var data = Path.Combine(Path.GetTempPath(), "somning-app-test", Guid.NewGuid().ToString("N"));
        var info = new ProcessStartInfo(RepoPaths.AppExe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--transcribe-file"); info.ArgumentList.Add(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav"));
        info.Environment["SOMNING_DATA_DIR"] = data;
        using var process = Process.Start(info) ?? throw new IOException("App did not start.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            var logs = Directory.Exists(Path.Combine(data, "logs"))
                ? string.Join("\n", Directory.GetFiles(Path.Combine(data, "logs")).Select(File.ReadAllText)) : "";
            return (process.ExitCode, logs);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }
}
