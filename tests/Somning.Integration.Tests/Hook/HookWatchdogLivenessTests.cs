using System.Threading.Channels;
using Somning.Core.Ports;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Paste;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Hook;

public sealed class HookWatchdogLivenessTests
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Fake_probe_loop_reinstalls_only_after_threshold()
    {
        var probe = new ScriptedProbe(ProbeOutcome.NotSeen, ProbeOutcome.Seen, ProbeOutcome.NotSeen, ProbeOutcome.NotSeen);
        var reinstalls = 0;
        var watchdog = new HookLivenessWatchdog(probe, new HookLivenessPolicy(), () => reinstalls++, () => false, _ => { });
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));      // miss 1
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));      // seen resets
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));      // miss 1 again
        Assert.Equal(0, reinstalls);
        Assert.Equal(LivenessAction.Reinstall, await watchdog.RunOnceAsync(token)); // miss 2 -> reinstall
        Assert.Equal(1, reinstalls);
        Assert.Equal(1, watchdog.ReinstallRequests);
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public async Task Silently_removed_hook_is_reinstalled_and_probe_is_seen_again()
    {
        Requires.Desktop();
        var channel = Channel.CreateUnbounded<KeyEvent>();
        using var hook = new KeyboardHook(channel.Writer);
        hook.Start();
        var probe = new HookLivenessProbe(hook);
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(ProbeOutcome.Seen, await probe.ProbeAsync(token));
        var installsBefore = hook.InstallCount;

        hook.SimulateSilentRemovalForTests();
        await Task.Delay(200, token); // hook thread processes the posted removal message

        var watchdog = new HookLivenessWatchdog(probe, new HookLivenessPolicy(), hook.Reinstall, () => false, _ => { });
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));
        Assert.Equal(LivenessAction.Reinstall, await watchdog.RunOnceAsync(token));

        await WaitUntilAsync(() => hook.InstallCount == installsBefore + 1, token);
        Assert.Equal(installsBefore + 1, hook.InstallCount);
        Assert.Equal(ProbeOutcome.Seen, await probe.ProbeAsync(token));
        Assert.False(channel.Reader.TryRead(out _), "the F24 probe key must never reach the key channel");
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public async Task Blocked_injection_is_inconclusive_and_causes_no_reinstall()
    {
        Requires.Desktop();
        var integrity = new IntegrityReader();
        var foreground = new ForegroundProbe(integrity).Capture();
        Requires.That(foreground.IntegrityRid is { } rid && rid > integrity.OwnRid(),
            "SendInput can only be blocked when an elevated window is foreground; this session cannot demonstrate a blocked state");

        var channel = Channel.CreateUnbounded<KeyEvent>();
        using var hook = new KeyboardHook(channel.Writer);
        hook.Start();
        var probe = new HookLivenessProbe(hook);
        var token = TestContext.Current.CancellationToken;
        var installsBefore = hook.InstallCount;
        var reinstalls = 0;
        var watchdog = new HookLivenessWatchdog(probe, new HookLivenessPolicy(), () => reinstalls++, () => false, _ => { });

        Assert.Equal(ProbeOutcome.Inconclusive, await probe.ProbeAsync(token));
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(token));
        Assert.Equal(0, reinstalls);
        Assert.Equal(installsBefore, hook.InstallCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        var until = DateTime.UtcNow + SettleTimeout;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20, token);
    }

    private sealed class ScriptedProbe(params ProbeOutcome[] script) : IHookLivenessProbe
    {
        private int index;
        public Task<ProbeOutcome> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(script[index++]);
    }
}
