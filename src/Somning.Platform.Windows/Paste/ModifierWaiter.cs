using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Somning.Platform.Windows.Paste;

public interface IModifierWaiter
{
    Task<bool> WaitForReleaseAsync(TimeSpan? timeout = null, TimeSpan? poll = null, CancellationToken ct = default);
}

public sealed class ModifierWaiter : IModifierWaiter
{
    private static readonly VIRTUAL_KEY[] Modifiers =
    [
        VIRTUAL_KEY.VK_CONTROL, VIRTUAL_KEY.VK_SHIFT, VIRTUAL_KEY.VK_MENU,
        VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_RWIN
    ];

    public async Task<bool> WaitForReleaseAsync(TimeSpan? timeout = null, TimeSpan? poll = null,
        CancellationToken ct = default)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromMilliseconds(1500);
        var interval = poll ?? TimeSpan.FromMilliseconds(15);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (Modifiers.All(key => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) == 0)) return true;
            if (deadline.Elapsed >= limit) return false;
            await Task.Delay(interval, ct).ConfigureAwait(false);
        }
    }
}
