using Microsoft.Win32;
using Somning.Core.Settings;

namespace Somning.Platform.Windows.Autostart;

public sealed class RegistryAutostartManager : IAutostartManager
{
    private const string ValueName = "Somning";
    private readonly string _keyPath;

    public RegistryAutostartManager()
        : this(@"Software\Microsoft\Windows\CurrentVersion\Run") { }

    internal RegistryAutostartManager(string keyPath) => _keyPath = keyPath;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void SetEnabled(bool enabled, string? executablePath = null, string arguments = "--tray")
    {
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        executablePath ??= Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("An absolute executable path without quotes is required.", nameof(executablePath));

        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\" {arguments}".TrimEnd(), RegistryValueKind.String);
    }
}
