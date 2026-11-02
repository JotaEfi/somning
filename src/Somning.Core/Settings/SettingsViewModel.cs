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
        CancelHotkey = CurrentSettings.CancelHotkey;
        ConfirmHotkey = CurrentSettings.ConfirmHotkey;
        AutoPaste = CurrentSettings.AutoPaste;
        PlaySoundCues = CurrentSettings.PlaySoundCues;
        SoundTheme = CurrentSettings.SoundTheme;
        DropStyle = Overlay.DropStyles.Normalize(CurrentSettings.DropStyle);
        PreserveInputLanguage = CurrentSettings.PreserveInputLanguage;
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
    public HotkeyBinding CancelHotkey { get; set; }
    public HotkeyBinding ConfirmHotkey { get; set; }
    public string? ConflictWarning { get; private set; }
    public bool AutoPaste { get; set; }
    public bool PlaySoundCues { get; set; }
    public string SoundTheme { get; set; }
    public string DropStyle { get; set; }
    public bool PreserveInputLanguage { get; set; }
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
            CancelHotkey = CancelHotkey,
            ConfirmHotkey = ConfirmHotkey,
            MicrophoneDeviceId = SelectedDeviceId,
            AutoPaste = AutoPaste,
            PlaySoundCues = PlaySoundCues,
            SoundTheme = SoundTheme,
            DropStyle = Overlay.DropStyles.Normalize(DropStyle),
            PreserveInputLanguage = PreserveInputLanguage,
            StartWithWindows = StartWithWindows,
            CustomVocabulary = CustomVocabulary.Trim(),
            CloudProvider = CloudProvider,
            EncryptedApiKey = encryptedApiKey ?? CurrentSettings.EncryptedApiKey
        };
    }
}
