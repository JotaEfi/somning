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
}
