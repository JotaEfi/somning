namespace Somning.Core.Settings;

/// <summary>
/// Checks a user-chosen binding against Windows shortcuts that would steal it (HOTK-05). "Blocked" means the
/// combination belongs to the OS secure-attention/lock path and cannot be used; "Warning" means Windows or the
/// shell also acts on it and the user should pick another combination.
/// </summary>
public static class HotkeyConflictValidator
{
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;
    private const KeyModifiers AltShift = KeyModifiers.Alt | KeyModifiers.Shift;

    public static HotkeyConflictResult Validate(HotkeyBinding binding)
    {
        var modifiers = binding.Modifiers;
        var key = NormalizeKey(binding.Key);

        if (key.Length == 0 || IsModifierKey(key))
        {
            var effective = ModifierFrom(key) is { } modifier ? modifiers | modifier : modifiers;
            if (effective == CtrlShift)
                return HotkeyConflictResult.Warning(
                    "Ctrl+Shift sozinho é usado pelo Windows para alternar o layout do teclado. Escolha uma combinação com outra tecla.");
            if (effective == AltShift)
                return HotkeyConflictResult.Warning(
                    "Alt+Shift é usado pelo Windows para alternar o layout do teclado. Escolha uma combinação com outra tecla.");
            return HotkeyConflictResult.NoConflict;
        }

        if (modifiers == (KeyModifiers.Control | KeyModifiers.Alt) && key == "delete")
            return HotkeyConflictResult.Blocked(
                "Ctrl+Alt+Delete é reservado pelo Windows e não pode ser usado como atalho.");

        if (modifiers == KeyModifiers.Windows && key == "l")
            return HotkeyConflictResult.Blocked(
                "Win+L bloqueia o Windows e não pode ser usado como atalho.");

        if (modifiers == KeyModifiers.Windows && key == "d")
            return HotkeyConflictResult.Warning(
                "Win+D mostra a área de trabalho do Windows. Escolha uma combinação que não conflite.");

        if (modifiers == KeyModifiers.Windows && key == "space")
            return HotkeyConflictResult.Warning(
                "Win+Space é usado pelo Windows para alternar o idioma de entrada. Escolha outra combinação.");

        if (modifiers == KeyModifiers.Alt && key == "tab")
            return HotkeyConflictResult.Warning(
                "Alt+Tab alterna entre janelas do Windows. Escolha outra combinação.");

        return HotkeyConflictResult.NoConflict;
    }

    private static bool IsModifierKey(string key) => ModifierFrom(key) is not null;

    private static KeyModifiers? ModifierFrom(string key) => key switch
    {
        "control" or "ctrl" => KeyModifiers.Control,
        "shift" => KeyModifiers.Shift,
        "alt" => KeyModifiers.Alt,
        "windows" or "win" => KeyModifiers.Windows,
        _ => null
    };

    private static string NormalizeKey(string? key)
    {
        var normalized = key?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "del" => "delete",
            "esc" => "escape",
            "spacebar" => "space",
            _ => normalized
        };
    }
}
