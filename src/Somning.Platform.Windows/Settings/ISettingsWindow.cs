using Somning.Core.Settings;

namespace Somning.Platform.Windows.Settings;

public interface ISettingsWindow : IDisposable
{
    bool IsOpen { get; }
    void Show(UserSettings currentSettings, Action<UserSettings> onSave);
    void Close();
}
