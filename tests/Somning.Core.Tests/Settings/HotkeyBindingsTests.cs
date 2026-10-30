using System.Text.Json;
using Somning.Core.Settings;
using Xunit;

namespace Somning.Core.Tests.Settings;

public sealed class HotkeyBindingsTests
{
    [Fact]
    public void LegacySettings_RetainNewShortcutDefaults()
    {
        var settings = JsonSerializer.Deserialize<UserSettings>("{}")!;
        Assert.Equal(HotkeyBinding.Default, settings.Hotkey);
        Assert.Equal(new HotkeyBinding(KeyModifiers.None, "Escape"), settings.CancelHotkey);
        Assert.Equal(new HotkeyBinding(KeyModifiers.Control, "Enter"), settings.ConfirmHotkey);
        Assert.True(HotkeyBindings.TryValidate(settings, out _));
    }

    [Theory]
    [InlineData("Return", 0x0D)]
    [InlineData("enter", 0x0D)]
    [InlineData("D9", 0x39)]
    [InlineData("NumPad4", 0x64)]
    [InlineData("F24", 0x87)]
    [InlineData("OemQuestion", 0xBF)]
    public void MapsPersistedAndWpfNames(string key, uint expected)
    {
        Assert.True(HotkeyBindings.TryGetVirtualKey(new HotkeyBinding(KeyModifiers.None, key), out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("LeftCtrl")]
    [InlineData("F25")]
    [InlineData("")]
    [InlineData("unknown")]
    public void InvalidKeysCannotBeSaved(string key) =>
        Assert.False(HotkeyBindings.TryValidate(new UserSettings { Hotkey = new(KeyModifiers.Control, key) }, out _));

    [Fact]
    public void AliasesCannotHideDuplicateBindings() =>
        Assert.False(HotkeyBindings.TryValidate(new UserSettings
        {
            CancelHotkey = new(KeyModifiers.Control, "Return"), ConfirmHotkey = new(KeyModifiers.Control, "Enter")
        }, out _));

    [Fact]
    public void ReservedWindowsAndUnknownModifierCombinationsAreRejected()
    {
        Assert.False(HotkeyBindings.TryValidate(new UserSettings { Hotkey = new(KeyModifiers.Windows, "L") }, out _));
        Assert.False(HotkeyBindings.TryValidate(new UserSettings { Hotkey = new((KeyModifiers)128, "K") }, out _));
    }

    [Fact]
    public void ViewModelRoundTripsAllThreeShortcuts()
    {
        var initial = new UserSettings
        {
            Hotkey = new(KeyModifiers.Alt, "F8"), CancelHotkey = new(KeyModifiers.Control, "F9"),
            ConfirmHotkey = new(KeyModifiers.Shift, "F10")
        };
        Assert.Equal(initial, new SettingsViewModel(initial).BuildUpdatedSettings());
    }
}
