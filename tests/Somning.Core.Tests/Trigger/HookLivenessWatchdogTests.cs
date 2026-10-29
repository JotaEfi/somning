using Somning.Core.Ports;
using Somning.Core.Trigger;
using Xunit;

namespace Somning.Core.Tests.Trigger;

public sealed class HookLivenessWatchdogTests
{
    private sealed class ScriptedProbe(params object[] script) : IHookLivenessProbe
    {
        private int index;
        public int Calls => Volatile.Read(ref index);
        public Task<ProbeOutcome> ProbeAsync(CancellationToken cancellationToken)
        {
            var item = script[Math.Min(Interlocked.Increment(ref index) - 1, script.Length - 1)];
            if (item is Exception ex) throw ex;
            return Task.FromResult((ProbeOutcome)item);
        }
    }

    private static HookLivenessWatchdog Create(IHookLivenessProbe probe, Action reinstall, List<string> log,
        Func<bool>? recording = null, TimeSpan? interval = null) =>
        new(probe, new HookLivenessPolicy(), reinstall, recording ?? (() => false), log.Add, interval: interval);

    [Fact]
    public async Task RunOnce_invokes_reinstall_only_when_policy_says_so()
    {
        var count = 0;
        var watchdog = Create(new ScriptedProbe(ProbeOutcome.NotSeen), () => count++, []);
        Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, count);
        Assert.Equal(LivenessAction.Reinstall, await watchdog.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, count);
        Assert.Equal(1, watchdog.ReinstallRequests);
    }

    [Fact]
    public async Task RunOnce_swallows_probe_exceptions_as_inconclusive()
    {
        var log = new List<string>();
        var count = 0;
        var watchdog = Create(new ScriptedProbe(new InvalidOperationException("boom")), () => count++, log);
        for (var i = 0; i < 5; i++) Assert.Equal(LivenessAction.None, await watchdog.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, count);
        Assert.NotEmpty(log);
    }

    [Fact]
    public async Task RunOnce_swallows_reinstall_callback_exceptions()
    {
        var log = new List<string>();
        var watchdog = Create(new ScriptedProbe(ProbeOutcome.NotSeen), () => throw new InvalidOperationException("nope"), log);
        await watchdog.RunOnceAsync(TestContext.Current.CancellationToken);
        await watchdog.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, watchdog.ReinstallRequests);
        Assert.Contains(log, l => l.Contains("reinstall callback failed"));
    }

    [Fact]
    public async Task RunAsync_skips_probing_while_recording_and_stops_on_cancel()
    {
        var probe = new ScriptedProbe(ProbeOutcome.Seen);
        var watchdog = Create(probe, () => { }, [], () => true, TimeSpan.FromMilliseconds(10));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(150);
        await watchdog.RunAsync(cts.Token);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task RunAsync_probes_periodically_and_reinstalls_after_misses()
    {
        var probe = new ScriptedProbe(ProbeOutcome.NotSeen);
        var count = 0;
        var watchdog = Create(probe, () => Interlocked.Increment(ref count), [], interval: TimeSpan.FromMilliseconds(10));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(400);
        await watchdog.RunAsync(cts.Token);
        Assert.True(probe.Calls >= 2);
        Assert.True(count >= 1);
    }
}
