using Somning.Core.Settings;
using Xunit;

namespace Somning.Core.Tests.Settings;

public sealed class HotkeyConflictValidatorTests
{
    [Theory]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, "", HotkeyConflictSeverity.Warning)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift, "", HotkeyConflictSeverity.Warning)]
    [InlineData(KeyModifiers.Windows, "Space", HotkeyConflictSeverity.Warning)]
    [InlineData(KeyModifiers.Alt, "Tab", HotkeyConflictSeverity.Warning)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "Delete", HotkeyConflictSeverity.Blocked)]
    [InlineData(KeyModifiers.Windows, "L", HotkeyConflictSeverity.Blocked)]
    [InlineData(KeyModifiers.Windows, "D", HotkeyConflictSeverity.Warning)]
    public void Known_windows_shortcuts_are_flagged(KeyModifiers modifiers, string key, HotkeyConflictSeverity expected)
    {
        var result = HotkeyConflictValidator.Validate(new HotkeyBinding(modifiers, key));

        Assert.True(result.IsConflicting);
        Assert.Equal(expected, result.Severity);
        Assert.False(string.IsNullOrWhiteSpace(result.WarningMessage));
    }

    [Theory]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, "Space")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "Space")]
    [InlineData(KeyModifiers.Alt, "F9")]
    [InlineData(KeyModifiers.None, "F8")]
    public void Safe_bindings_are_not_conflicting(KeyModifiers modifiers, string key)
    {
        var result = HotkeyConflictValidator.Validate(new HotkeyBinding(modifiers, key));

        Assert.False(result.IsConflicting);
        Assert.Equal(HotkeyConflictSeverity.None, result.Severity);
        Assert.Null(result.WarningMessage);
        Assert.Equal(HotkeyConflictResult.NoConflict, result);
    }

    [Fact]
    public void Default_somning_hotkey_is_not_conflicting()
    {
        var result = HotkeyConflictValidator.Validate(HotkeyBinding.Default);

        Assert.False(result.IsConflicting);
    }

    [Theory]
    [InlineData(KeyModifiers.Windows, "l", HotkeyConflictSeverity.Blocked)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "DEL", HotkeyConflictSeverity.Blocked)]
    [InlineData(KeyModifiers.Windows, "space", HotkeyConflictSeverity.Warning)]
    [InlineData(KeyModifiers.Alt, "tab", HotkeyConflictSeverity.Warning)]
    public void Key_matching_is_case_insensitive(KeyModifiers modifiers, string key, HotkeyConflictSeverity expected)
    {
        var result = HotkeyConflictValidator.Validate(new HotkeyBinding(modifiers, key));

        Assert.Equal(expected, result.Severity);
    }

    [Fact]
    public void Modifier_key_names_form_the_same_ctrl_shift_conflict()
    {
        var result = HotkeyConflictValidator.Validate(new HotkeyBinding(KeyModifiers.Control, "Shift"));

        Assert.Equal(HotkeyConflictSeverity.Warning, result.Severity);
        Assert.True(result.IsConflicting);
    }
}
