using Somning.Core.Ports;
using Somning.Platform.Windows.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Somning.Platform.Windows.Hook;

/// <summary>
/// Injects a marked F24 keystroke and checks that the low-level hook callback swallowed it. F24 is unbound in common
/// applications and the hook swallows the marked event anyway (T-04-01). SendInput failure is inconclusive.
/// </summary>
public sealed class HookLivenessProbe(KeyboardHook hook) : IHookLivenessProbe
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(10);

    public async Task<ProbeOutcome> ProbeAsync(CancellationToken cancellationToken)
    {
        var before = hook.ProbeSeenCount;
        if (!SendProbeKey()) return ProbeOutcome.Inconclusive;

        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (hook.ProbeSeenCount > before) return ProbeOutcome.Seen;
            await Task.Delay(PollStep, cancellationToken).ConfigureAwait(false);
        }
        return hook.ProbeSeenCount > before ? ProbeOutcome.Seen : ProbeOutcome.NotSeen;
    }

    private static unsafe bool SendProbeKey()
    {
        var inputs = new INPUT[2];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].ki.wVk = VIRTUAL_KEY.VK_F24;
            inputs[i].ki.dwExtraInfo = InputMarkers.Probe;
            inputs[i].ki.dwFlags = i == 0 ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }

        return PInvoke.SendInput(inputs, sizeof(INPUT)) >= inputs.Length;
    }
}
