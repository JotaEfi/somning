using Somning.Core.Settings;

namespace Somning.Platform.Windows.Settings;

public interface ISettingsWindow : IDisposable
{
    bool IsOpen { get; }

    /// <summary>True while the shortcut recorder is waiting for keys; only then must the global hook stay out of the way.</summary>
    bool IsCapturingHotkey { get; }
    void Show(UserSettings currentSettings, Action<UserSettings> onSave);
    void Close();
}
