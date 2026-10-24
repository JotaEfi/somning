namespace Somning.Core.Settings;

public interface IAutostartManager
{
    bool IsEnabled();
    void SetEnabled(bool enabled, string? executablePath = null, string arguments = "--tray");
}
