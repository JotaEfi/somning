namespace Somning.Core.Settings;

public readonly record struct HotkeyBinding
{
    public KeyModifiers Modifiers { get; init; }
    public string Key { get; init; } = "Space";

    public HotkeyBinding() : this(KeyModifiers.Control | KeyModifiers.Shift, "Space") { }

    public HotkeyBinding(KeyModifiers modifiers, string key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public static HotkeyBinding Default => new();
}
