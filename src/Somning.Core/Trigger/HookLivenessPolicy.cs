using Somning.Core.Ports;

namespace Somning.Core.Trigger;

/// <summary>Action the watchdog must take after a probe.</summary>
public enum LivenessAction { None, Reinstall, Escalate }

/// <summary>Tunables for <see cref="HookLivenessPolicy"/>.</summary>
public sealed record HookLivenessOptions(int MissThreshold = 2, int MaxReinstalls = 5, TimeSpan? RollingWindow = null)
{
    public TimeSpan Window => RollingWindow ?? TimeSpan.FromMinutes(10);
}

/// <summary>
/// Pure decision logic for the hook liveness watchdog: probe outcomes in, action out. No clock access (the caller
/// passes <c>now</c>) and no static state. Reinstalls are rate limited so a permanently failing environment escalates
/// instead of spinning.
/// </summary>
public sealed class HookLivenessPolicy(HookLivenessOptions? options = null)
{
    private readonly HookLivenessOptions options = options ?? new HookLivenessOptions();
    private readonly Queue<DateTimeOffset> reinstalls = new();
    private int misses;

    public LivenessAction Evaluate(ProbeOutcome outcome, DateTimeOffset now)
    {
        switch (outcome)
        {
            case ProbeOutcome.Seen:
                misses = 0;
                return LivenessAction.None;
            case ProbeOutcome.NotSeen:
                misses++;
                if (misses < options.MissThreshold) return LivenessAction.None;
                misses = 0;
                while (reinstalls.Count > 0 && now - reinstalls.Peek() >= options.Window) reinstalls.Dequeue();
                if (reinstalls.Count >= options.MaxReinstalls) return LivenessAction.Escalate;
                reinstalls.Enqueue(now);
                return LivenessAction.Reinstall;
            default:
                return LivenessAction.None;
        }
    }
}
