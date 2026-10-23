namespace Somning.Core.Settings;

/// <summary>Outcome of checking a binding against Windows shortcuts. Messages are user-facing pt-BR.</summary>
public sealed record HotkeyConflictResult(bool IsConflicting, string? WarningMessage, HotkeyConflictSeverity Severity)
{
    public static HotkeyConflictResult NoConflict { get; } = new(false, null, HotkeyConflictSeverity.None);

    public static HotkeyConflictResult Warning(string message) =>
        new(true, message, HotkeyConflictSeverity.Warning);

    public static HotkeyConflictResult Blocked(string message) =>
        new(true, message, HotkeyConflictSeverity.Blocked);
}
