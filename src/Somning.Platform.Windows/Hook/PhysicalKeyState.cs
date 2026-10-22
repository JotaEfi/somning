using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
namespace Somning.Platform.Windows.Hook;
public static class PhysicalKeyState
{
    public static bool AreChordModifiersDown() =>
        (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0
        && (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0;
}
