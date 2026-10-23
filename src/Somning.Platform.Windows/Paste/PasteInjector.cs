using Somning.Platform.Windows.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Somning.Platform.Windows.Paste;

public interface IPasteInjector
{
    PasteInjectionResult SendCtrlV();
}

public readonly record struct PasteInjectionResult(uint Inserted, bool CleanupSucceeded);
public readonly record struct KeyboardInput(ushort VirtualKey, bool KeyUp, nuint Marker);

public interface IKeyboardInputSender
{
    uint Send(IReadOnlyList<KeyboardInput> inputs);
}

public sealed class WindowsKeyboardInputSender : IKeyboardInputSender
{
    public unsafe uint Send(IReadOnlyList<KeyboardInput> inputs)
    {
        var nativeInputs = new INPUT[inputs.Count];
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
            input.ki.wVk = (VIRTUAL_KEY)inputs[index].VirtualKey;
            input.ki.dwExtraInfo = inputs[index].Marker;
            input.ki.dwFlags = inputs[index].KeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
            nativeInputs[index] = input;
        }
        return PInvoke.SendInput(nativeInputs, sizeof(INPUT));
    }
}

public sealed class PasteInjector : IPasteInjector
{
    private readonly IKeyboardInputSender sender;

    public PasteInjector() : this(new WindowsKeyboardInputSender()) { }
    public PasteInjector(IKeyboardInputSender sender) => this.sender = sender;

    public PasteInjectionResult SendCtrlV()
    {
        KeyboardInput[] inputs =
        [
            Key((ushort)VIRTUAL_KEY.VK_CONTROL, false),
            Key((ushort)VIRTUAL_KEY.VK_V, false),
            Key((ushort)VIRTUAL_KEY.VK_V, true),
            Key((ushort)VIRTUAL_KEY.VK_CONTROL, true)
        ];
        var inserted = Math.Min(sender.Send(inputs), (uint)inputs.Length);
        if (inserted == (uint)inputs.Length) return new(inserted, true);

        var held = new List<KeyboardInput>();
        for (var index = 0; index < inserted; index++)
        {
            var input = inputs[index];
            if (input.KeyUp)
            {
                var priorDown = held.FindLastIndex(key => key.VirtualKey == input.VirtualKey);
                if (priorDown >= 0) held.RemoveAt(priorDown);
            }
            else
            {
                held.Add(input);
            }
        }

        if (held.Count == 0) return new(inserted, true);
        var cleanup = held.AsEnumerable().Reverse().Select(key => Key(key.VirtualKey, true)).ToArray();
        var cleanupInserted = sender.Send(cleanup);
        if (cleanupInserted >= (uint)cleanup.Length) return new(inserted, true);

        var remaining = cleanup.Skip((int)cleanupInserted).ToArray();
        var retryInserted = sender.Send(remaining);
        return new(inserted, retryInserted == (uint)remaining.Length);
    }

    private static KeyboardInput Key(ushort key, bool up) => new(key, up, InputMarkers.Paste);
}
