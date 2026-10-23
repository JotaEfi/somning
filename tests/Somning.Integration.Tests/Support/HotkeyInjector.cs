using Somning.Platform.Windows.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using PInvoke = Windows.Win32.IntegrationPInvoke;
namespace Somning.Integration.Tests.Support;
public static class HotkeyInjector
{
    private static INPUT Input(VIRTUAL_KEY key, bool down, nuint marker)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.ki.wVk = key; input.ki.dwExtraInfo = marker;
        input.ki.dwFlags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        return input;
    }
    private static unsafe void Send(ReadOnlySpan<INPUT> inputs)
    {
        var sent = PInvoke.SendInput(inputs, sizeof(INPUT));
        if (sent != inputs.Length)
        {
            var err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new IOException($"SendInput incomplete: sent {sent} of {inputs.Length}, error={err}");
        }
    }
    public static void Down() => Send([Input(VIRTUAL_KEY.VK_CONTROL, true, InputMarkers.Test),
        Input(VIRTUAL_KEY.VK_SHIFT, true, InputMarkers.Test), Input(VIRTUAL_KEY.VK_SPACE, true, InputMarkers.Test)]);
    public static void Up() => Send([Input(VIRTUAL_KEY.VK_SPACE, false, InputMarkers.Test),
        Input(VIRTUAL_KEY.VK_SHIFT, false, InputMarkers.Test), Input(VIRTUAL_KEY.VK_CONTROL, false, InputMarkers.Test)]);
    public static void ModifiersUpOnly() => Send([Input(VIRTUAL_KEY.VK_SHIFT, false, InputMarkers.Test),
        Input(VIRTUAL_KEY.VK_CONTROL, false, InputMarkers.Test)]);
    public static void TriggerOnly(bool down) => Send([Input(VIRTUAL_KEY.VK_SPACE, down, InputMarkers.Test)]);
    public static void Chord(int holdMs) { Down(); Thread.Sleep(holdMs); Up(); }
    public static void PasteChord() => Send([Input(VIRTUAL_KEY.VK_CONTROL, true, InputMarkers.Paste),
        Input(VIRTUAL_KEY.VK_V, true, InputMarkers.Paste), Input(VIRTUAL_KEY.VK_V, false, InputMarkers.Paste),
        Input(VIRTUAL_KEY.VK_CONTROL, false, InputMarkers.Paste)]);
    public static void ReleaseAll() => Send([Input(VIRTUAL_KEY.VK_SPACE, false, InputMarkers.Test),
        Input(VIRTUAL_KEY.VK_SHIFT, false, InputMarkers.Test), Input(VIRTUAL_KEY.VK_CONTROL, false, InputMarkers.Test)]);
}
