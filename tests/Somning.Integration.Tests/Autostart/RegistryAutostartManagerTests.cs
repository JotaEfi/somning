using Microsoft.Win32;
using Somning.Platform.Windows.Autostart;
using Xunit;

namespace Somning.Integration.Tests.Autostart;

public sealed class RegistryAutostartManagerTests : IDisposable
{
    private readonly string _keyPath = @"Software\Somning.Tests\Autostart\" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Autostart_EnableAndDisable_PreservesQuotedUnicodePathAndOtherValues()
    {
        var manager = new RegistryAutostartManager(_keyPath);
        Assert.False(manager.IsEnabled());
        manager.SetEnabled(false);

        const string executable = @"C:\Usuários\João Silva\Somning.exe";
        manager.SetEnabled(true, executable);
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true)!;
        Assert.Equal($"\"{executable}\" --tray", key.GetValue("Somning"));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind("Somning"));
        Assert.True(manager.IsEnabled());
        key.SetValue("Other", "preserved");

        manager.SetEnabled(true, executable, "--minimized");
        Assert.Equal($"\"{executable}\" --minimized", key.GetValue("Somning"));
        manager.SetEnabled(false);
        manager.SetEnabled(false);
        Assert.False(manager.IsEnabled());
        Assert.Null(key.GetValue("Somning"));
        Assert.Equal("preserved", key.GetValue("Other"));
    }

    [Fact]
    public void Autostart_DefaultPath_UsesCurrentExecutable()
    {
        var manager = new RegistryAutostartManager(_keyPath);
        manager.SetEnabled(true);
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath)!;
        Assert.Equal($"\"{Environment.ProcessPath}\" --tray", key.GetValue("Somning"));
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
}
