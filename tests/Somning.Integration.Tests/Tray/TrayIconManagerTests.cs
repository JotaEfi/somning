using Somning.Platform.Windows.Tray;
using Xunit;

namespace Somning.Integration.Tests.Tray;

public sealed class TrayIconManagerTests
{
    [Fact]
    public void TrayIconManager_CanInitializeAndDisposeCleanly()
    {
        using var tray = new TrayIconManager("Somning Test Tooltip");
        Assert.NotNull(tray);
        Assert.True(tray.IsRegistered);

        tray.UpdateTooltip("Novo Tooltip");
    }

    [Fact]
    public void TrayIconManager_CanRepastePredicate_ControlsState()
    {
        using var tray = new TrayIconManager();
        Assert.Null(tray.CanRepaste);

        bool canRepaste = false;
        tray.CanRepaste = () => canRepaste;

        Assert.False(tray.CanRepaste());

        canRepaste = true;
        Assert.True(tray.CanRepaste());
    }
    [Fact]
    public void Registration_RetriesTransientShellFailure()
    {
        var attempts = 0;
        var waits = 0;
        Assert.True(TrayIconManager.RegisterWithRetry(() => ++attempts == 3, () => waits++));
        Assert.Equal(3, attempts);
        Assert.Equal(2, waits);
    }

    [Fact]
    public void Registration_PermanentFailureIsBoundedAndReported()
    {
        var attempts = 0;
        var waits = 0;
        Assert.False(TrayIconManager.RegisterWithRetry(() => { attempts++; return false; }, () => waits++));
        Assert.Equal(5, attempts);
        Assert.Equal(4, waits);
    }
}
