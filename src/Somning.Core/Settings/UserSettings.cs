namespace Somning.Core.Settings;

public sealed record UserSettings
{
    public HotkeyBinding Hotkey { get; init; } = HotkeyBinding.Default;
    public string? MicrophoneDeviceId { get; init; }
    public string EngineBackend { get; init; } = "vulkan";
    public string CloudProvider { get; init; } = "none";
    public string? EncryptedApiKey { get; init; }
    public bool AutoPaste { get; init; } = true;
    public bool PlaySoundCues { get; init; }
    public bool StartWithWindows { get; init; } = false;
    public string CustomVocabulary { get; init; } = string.Empty;
    public int TapThresholdMs { get; init; } = 300;
    public string Language { get; init; } = "pt-BR";
}
