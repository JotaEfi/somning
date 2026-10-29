using Somning.Core.Ports;

namespace Somning.Core.Trigger;

/// <summary>
/// Periodically probes the keyboard hook and reinstalls it when Windows silently dropped it (no event announces this).
/// Probe or callback failures are logged and treated as inconclusive; they never escape the loop.
/// </summary>
public sealed class HookLivenessWatchdog(
    IHookLivenessProbe probe,
    HookLivenessPolicy policy,
    Action reinstall,
    Func<bool> isRecordingActive,
    Action<string> log,
    TimeProvider? timeProvider = null,
    TimeSpan? interval = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan period = interval ?? TimeSpan.FromSeconds(30);
    private int reinstallCount;
    private DateTimeOffset? lastReinstallAt;

    /// <summary>Reinstall REQUESTS issued (the callback returned without throwing). Completed reinstalls are counted by the hook itself.</summary>
    public int ReinstallRequests => Volatile.Read(ref reinstallCount);

    public async Task<LivenessAction> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        ProbeOutcome outcome;
        try { outcome = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log($"Hook liveness probe failed: {ex.Message}");
            outcome = ProbeOutcome.Inconclusive;
        }

        var now = time.GetUtcNow();
        var action = policy.Evaluate(outcome, now);
        if (action == LivenessAction.Reinstall)
        {
            lastReinstallAt = now;
            try
            {
                reinstall();
                Interlocked.Increment(ref reinstallCount);
            }
            catch (Exception ex)
            {
                log($"Hook reinstall callback failed: {ex.Message}");
                return LivenessAction.None;
            }
        }
        else if (action == LivenessAction.Escalate)
        {
            log("Hook liveness: reinstall limit reached; not reinstalling until the rolling window clears");
        }

        return action;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(period, time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (isRecordingActive()) continue;
                if (lastReinstallAt is { } last && time.GetUtcNow() - last < period) continue;
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
