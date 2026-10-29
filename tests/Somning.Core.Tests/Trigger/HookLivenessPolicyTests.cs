using Somning.Core.Ports;
using Somning.Core.Trigger;
using Xunit;

namespace Somning.Core.Tests.Trigger;

public sealed class HookLivenessPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seen_resets_misses_and_yields_none()
    {
        var policy = new HookLivenessPolicy();
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.NotSeen, T0));
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.Seen, T0));
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.NotSeen, T0)); // counter restarted
    }

    [Fact]
    public void Inconclusive_never_counts_as_a_miss()
    {
        var policy = new HookLivenessPolicy();
        for (var i = 0; i < 10; i++)
            Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.Inconclusive, T0));
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.NotSeen, T0));
    }

    [Fact]
    public void Misses_reaching_threshold_yield_reinstall_and_reset()
    {
        var policy = new HookLivenessPolicy();
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.NotSeen, T0));
        Assert.Equal(LivenessAction.Reinstall, policy.Evaluate(ProbeOutcome.NotSeen, T0));
        Assert.Equal(LivenessAction.None, policy.Evaluate(ProbeOutcome.NotSeen, T0)); // reset after reinstall
    }

    [Fact]
    public void Inconclusive_between_misses_does_not_reset_or_advance()
    {
        var policy = new HookLivenessPolicy();
        policy.Evaluate(ProbeOutcome.NotSeen, T0);
        policy.Evaluate(ProbeOutcome.Inconclusive, T0);
        Assert.Equal(LivenessAction.Reinstall, policy.Evaluate(ProbeOutcome.NotSeen, T0));
    }

    [Fact]
    public void Reinstalls_beyond_limit_in_window_escalate()
    {
        var policy = new HookLivenessPolicy();
        for (var i = 0; i < 5; i++)
        {
            policy.Evaluate(ProbeOutcome.NotSeen, T0.AddSeconds(i));
            Assert.Equal(LivenessAction.Reinstall, policy.Evaluate(ProbeOutcome.NotSeen, T0.AddSeconds(i)));
        }
        policy.Evaluate(ProbeOutcome.NotSeen, T0.AddSeconds(6));
        Assert.Equal(LivenessAction.Escalate, policy.Evaluate(ProbeOutcome.NotSeen, T0.AddSeconds(6)));
    }

    [Fact]
    public void Reinstalls_older_than_window_stop_counting()
    {
        var policy = new HookLivenessPolicy();
        for (var i = 0; i < 5; i++)
        {
            policy.Evaluate(ProbeOutcome.NotSeen, T0);
            policy.Evaluate(ProbeOutcome.NotSeen, T0);
        }
        var later = T0.AddMinutes(11);
        policy.Evaluate(ProbeOutcome.NotSeen, later);
        Assert.Equal(LivenessAction.Reinstall, policy.Evaluate(ProbeOutcome.NotSeen, later));
    }
}
