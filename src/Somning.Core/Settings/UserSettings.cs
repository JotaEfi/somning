namespace Somning.Core.Settings;

public sealed record UserSettings
{
    public HotkeyBinding Hotkey { get; init; } = HotkeyBinding.Default;
    public HotkeyBinding CancelHotkey { get; init; } = new(KeyModifiers.None, "Escape");
    public HotkeyBinding ConfirmHotkey { get; init; } = new(KeyModifiers.Control, "Enter");
    public string? MicrophoneDeviceId { get; init; }
    public string EngineBackend { get; init; } = Engine.EngineDefaults.Backend;
    public string CloudProvider { get; init; } = "none";
    public string? EncryptedApiKey { get; init; }
    public bool AutoPaste { get; init; } = true;
    public bool PlaySoundCues { get; init; }
    public string SoundTheme { get; init; } = "classico";
    public string DropStyle { get; init; } = Overlay.DropStyles.Classic;
    public bool PreserveInputLanguage { get; init; }
    public bool StartWithWindows { get; init; } = false;
    /// <summary>Looks for new GitHub releases once a day and asks before installing one.</summary>
    public bool CheckForUpdates { get; init; } = true;
    public string CustomVocabulary { get; init; } = string.Empty;
    public int TapThresholdMs { get; init; } = 300;
    public string Language { get; init; } = "pt-BR";
}
