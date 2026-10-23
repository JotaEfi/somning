using Somning.Core.Engine;
using Xunit;

namespace Somning.Core.Tests.Engine;

public sealed class RestartPolicyTests
{
    [Fact]
    public void Applies_cpu_backoff_and_restart_cap()
    {
        var policy = new RestartPolicy();
        (policy, var first) = policy.OnFailure(0, "vulkan", "exit");
        (policy, var second) = policy.OnFailure(10_000, "cpu", "exit");
        (policy, var third) = policy.OnFailure(20_000, "cpu", "exit");
        (policy, var fourth) = policy.OnFailure(30_000, "cpu", "exit");

        Assert.Equal(TimeSpan.Zero, Assert.IsType<RestartDecision.Restart>(first).Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<RestartDecision.Restart>(second).Delay);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.IsType<RestartDecision.Restart>(third).Delay);
        Assert.IsType<RestartDecision.GiveUp>(fourth);
    }

    [Fact]
    public void Drops_failures_outside_the_ten_minute_window()
    {
        var policy = new RestartPolicy();
        (policy, _) = policy.OnFailure(0, "vulkan", "exit");
        (policy, _) = policy.OnFailure(60_000, "cpu", "exit");
        (policy, _) = policy.OnFailure(120_000, "cpu", "exit");
        (_, var decision) = policy.OnFailure(660_000, "cpu", "exit");

        Assert.IsType<RestartDecision.Restart>(decision);
    }

    [Fact]
    public void On_failure_returns_a_new_policy_without_mutating_the_old_one()
    {
        var original = new RestartPolicy();
        var (next, first) = original.OnFailure(0, "vulkan", "exit");
        var (_, second) = next.OnFailure(1, "cpu", "exit");

        Assert.Equal(TimeSpan.Zero, Assert.IsType<RestartDecision.Restart>(first).Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<RestartDecision.Restart>(second).Delay);
        var (_, originalDecision) = original.OnFailure(1, "vulkan", "exit");
        Assert.Equal(TimeSpan.Zero, Assert.IsType<RestartDecision.Restart>(originalDecision).Delay);
    }
}
