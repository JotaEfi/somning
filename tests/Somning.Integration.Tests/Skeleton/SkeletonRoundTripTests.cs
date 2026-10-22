using System.Diagnostics;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Xunit;
using Somning.App.Engine;
using Somning.Core.Engine;
using Somning.Engine.Protocol;
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
    [Fact]
    public async Task Clipboard_held_exits_11()
    {
        Requires.Models(); Requires.Desktop();
        using var holder = new ClipboardHolder();
        var (exit, log) = await RunAppAsync();
        Assert.Equal(11, exit); Assert.Contains("clipboard_failed", log);
        Assert.DoesNotContain("Hoje vamos testar", log);
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
public sealed class ProtocolMappingTests
{
    private static readonly EngineSessionInfo Session = new("vulkan", "test-model", 1200, 800, true, true);
    [Theory]
    [InlineData(ErrorCodes.InvalidPath, false)]
    [InlineData(ErrorCodes.BadRequest, false)]
    [InlineData(ErrorCodes.GpuError, true)]
    public void Maps_retryability(string code, bool retryable)
    {
        var mapped = ProtocolMapping.Map(new ErrorMessage("id", code, "private error text"), Session);
        Assert.Equal("error", mapped.Outcome); Assert.Equal(code, mapped.ErrorCode);
        Assert.Equal(retryable, mapped.Retryable);
    }
    [Fact]
    public void Carries_session_load_and_warm_timings()
    {
        var mapped = ProtocolMapping.Map(new ResultMessage("id", "Olá.", "text", null, 100, 2, 30, 0,
            "vulkan", "test-model"), Session);
        Assert.Equal(1200, mapped.LoadMs); Assert.Equal(800, mapped.WarmMs);
        Assert.Equal("Olá.", mapped.Text);
    }
}
