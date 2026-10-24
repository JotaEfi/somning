using Somning.Core.Trigger;

namespace Somning.Core.Settings;

public sealed class SettingsViewModel
{
    public SettingsViewModel(UserSettings initialSettings, IReadOnlyList<string>? availableDevices = null)
    {
        CurrentSettings = initialSettings ?? new UserSettings();
        AvailableDevices = availableDevices ?? [];
        SelectedDeviceId = CurrentSettings.MicrophoneDeviceId;
        Hotkey = CurrentSettings.Hotkey;
        AutoPaste = CurrentSettings.AutoPaste;
        PlaySoundCues = CurrentSettings.PlaySoundCues;
        StartWithWindows = CurrentSettings.StartWithWindows;
        CustomVocabulary = CurrentSettings.CustomVocabulary;
        CloudProvider = CurrentSettings.CloudProvider;
        ApiKey = string.Empty;
        UpdateConflictWarning();
    }

    public UserSettings CurrentSettings { get; }
    public IReadOnlyList<string> AvailableDevices { get; }
    public string? SelectedDeviceId { get; set; }
    public HotkeyBinding Hotkey { get; set; }
    public string? ConflictWarning { get; private set; }
    public bool AutoPaste { get; set; }
    public bool PlaySoundCues { get; set; }
    public bool StartWithWindows { get; set; }
    public string CustomVocabulary { get; set; }
    public string CloudProvider { get; set; }
    public string ApiKey { get; set; }

    public void UpdateHotkey(HotkeyBinding binding)
    {
        Hotkey = binding;
        UpdateConflictWarning();
    }

    private void UpdateConflictWarning()
    {
        var result = HotkeyConflictValidator.Validate(Hotkey);
        ConflictWarning = result.IsConflicting ? result.WarningMessage : null;
    }

    public UserSettings BuildUpdatedSettings(string? encryptedApiKey = null)
    {
        return CurrentSettings with
        {
            Hotkey = Hotkey,
            MicrophoneDeviceId = SelectedDeviceId,
            AutoPaste = AutoPaste,
            PlaySoundCues = PlaySoundCues,
            StartWithWindows = StartWithWindows,
            CustomVocabulary = CustomVocabulary.Trim(),
            CloudProvider = CloudProvider,
            EncryptedApiKey = encryptedApiKey ?? CurrentSettings.EncryptedApiKey
        };
    }
}
