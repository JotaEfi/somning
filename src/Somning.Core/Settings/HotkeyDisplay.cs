namespace Somning.Core.Settings;

/// <summary>Formats a hotkey binding for display in pt-BR (e.g. "Ctrl + Shift + Espaço").</summary>
public static class HotkeyDisplay
{
    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>(5);
        if (binding.Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        if (!string.IsNullOrWhiteSpace(binding.Key)) parts.Add(FormatKey(binding.Key));
        return parts.Count == 0 ? "Nenhum" : string.Join(" + ", parts);
    }

    private static string FormatKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "space" => "Espaço",
        "enter" or "return" => "Enter",
        "escape" or "esc" => "Esc",
        "tab" => "Tab",
        _ => key.Trim().Length == 1 ? key.Trim().ToUpperInvariant() : key.Trim()
    };
}
