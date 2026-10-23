using System.Diagnostics;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Processes;
using Xunit;

namespace Somning.Integration.Tests.Processes;

public sealed class SingleInstanceAppTests
{
    [Fact]
    public async Task SecondInstance_WhenFirstInstanceRunning_ExitsImmediatelyWithZero()
    {
        // Acquire the default app mutex to simulate an actively running first instance
        using var primary = new SingleInstanceMutex(@"Local\Somning_SingleInstance_App_Mutex");
        Assert.True(primary.IsFirstInstance);

        var startInfo = new ProcessStartInfo
        {
            FileName = RepoPaths.AppExe,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var exited = await Task.Run(() => process.WaitForExit(5000));
        Assert.True(exited, "Second Somning.App instance should have exited immediately.");
        Assert.Equal(0, process.ExitCode);
    }
}
