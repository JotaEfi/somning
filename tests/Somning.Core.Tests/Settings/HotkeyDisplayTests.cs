using Somning.Core.Settings;
using Xunit;

namespace Somning.Core.Tests.Settings;

public sealed class HotkeyDisplayTests
{
    [Fact]
    public void Format_DefaultBinding_ShowsCtrlShiftEspaco() =>
        Assert.Equal("Ctrl + Shift + Espaço", HotkeyDisplay.Format(HotkeyBinding.Default));

    [Theory]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Windows, "d", "Alt + Win + D")]
    [InlineData(KeyModifiers.None, "F9", "F9")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, "", "Ctrl + Shift")]
    [InlineData(KeyModifiers.None, "", "Nenhum")]
    public void Format_ReflectsModifiersAndKey(KeyModifiers modifiers, string key, string expected) =>
        Assert.Equal(expected, HotkeyDisplay.Format(new HotkeyBinding(modifiers, key)));
}
