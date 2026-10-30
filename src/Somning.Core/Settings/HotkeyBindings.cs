namespace Somning.Core.Settings;

/// <summary>Shared validation and Windows virtual-key mapping for persisted shortcut names.</summary>
public static class HotkeyBindings
{
    private const KeyModifiers AllModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Windows;

    public static bool TryValidate(UserSettings settings, out string? error)
    {
        var seen = new HashSet<(KeyModifiers, uint)>();
        foreach (var (name, binding) in new[] { ("Ativar", settings.Hotkey), ("Cancelar", settings.CancelHotkey), ("Confirmar", settings.ConfirmHotkey) })
        {
            if ((binding.Modifiers & ~AllModifiers) != 0 || !TryGetVirtualKey(binding, out var key))
            {
                error = $"{name}: escolha uma tecla válida além dos modificadores.";
                return false;
            }
            var conflict = HotkeyConflictValidator.Validate(binding);
            if (conflict.Severity == HotkeyConflictSeverity.Blocked)
            {
                error = conflict.WarningMessage;
                return false;
            }
            if (!seen.Add((binding.Modifiers, key)))
            {
                error = "Ativar, cancelar e confirmar precisam usar combinações diferentes.";
                return false;
            }
        }
        error = null;
        return true;
    }

    public static bool TryGetVirtualKey(HotkeyBinding binding, out uint virtualKey)
    {
        var key = binding.Key?.Trim().ToUpperInvariant() ?? string.Empty;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) { virtualKey = key[0]; return true; }
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) { virtualKey = key[1]; return true; }
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) { virtualKey = (uint)(0x70 + f - 1); return true; }
        if (key.StartsWith("NUMPAD", StringComparison.Ordinal) && int.TryParse(key.AsSpan(6), out var n) && n is >= 0 and <= 9) { virtualKey = (uint)(0x60 + n); return true; }
        virtualKey = key switch
        {
            "SPACE" or "SPACEBAR" => 0x20, "ENTER" or "RETURN" => 0x0D, "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09, "BACK" or "BACKSPACE" => 0x08, "DELETE" or "DEL" => 0x2E, "INSERT" => 0x2D,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" or "PRIOR" => 0x21, "PAGEDOWN" or "NEXT" => 0x22,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "MULTIPLY" => 0x6A, "ADD" => 0x6B, "SUBTRACT" => 0x6D, "DECIMAL" => 0x6E, "DIVIDE" => 0x6F,
            "OEM1" or "OEMSEMICOLON" => 0xBA, "OEMPLUS" => 0xBB, "OEMCOMMA" => 0xBC, "OEMMINUS" => 0xBD,
            "OEMPERIOD" => 0xBE, "OEM2" or "OEMQUESTION" => 0xBF, "OEM3" or "OEMTILDE" => 0xC0,
            "OEM4" or "OEMOPENBRACKETS" => 0xDB, "OEM5" or "OEMPIPE" => 0xDC, "OEM6" or "OEMCLOSEBRACKETS" => 0xDD,
            "OEM7" or "OEMQUOTES" => 0xDE, "OEM102" or "OEMBACKSLASH" => 0xE2,
            _ => 0
        };
        return virtualKey != 0;
    }
}
