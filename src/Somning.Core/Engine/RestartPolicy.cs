namespace Somning.Core.Engine;

public abstract record RestartDecision
{
    private RestartDecision() { }
    public sealed record Restart(string Backend, TimeSpan Delay) : RestartDecision;
    public sealed record GiveUp(string Reason) : RestartDecision;
}

/// <summary>Immutable restart budget. Time is supplied by the caller, so decisions are deterministic.</summary>
public sealed class RestartPolicy
{
    private static readonly long[] BackoffMilliseconds = [0, 2_000, 5_000];
    private readonly long[] failures;

    public RestartPolicy(int maxRestarts = 3, long windowMs = 600_000)
        : this(maxRestarts, windowMs, []) { }

    private RestartPolicy(int maxRestarts, long windowMs, long[] failures)
    {
        if (maxRestarts < 0) throw new ArgumentOutOfRangeException(nameof(maxRestarts));
        if (windowMs <= 0) throw new ArgumentOutOfRangeException(nameof(windowMs));
        MaxRestarts = maxRestarts;
        WindowMs = windowMs;
        this.failures = failures;
    }

    public int MaxRestarts { get; }
    public long WindowMs { get; }

    public (RestartPolicy Next, RestartDecision Decision) OnFailure(long nowMs, string failedBackend, string reason)
    {
        ArgumentNullException.ThrowIfNull(failedBackend);
        ArgumentNullException.ThrowIfNull(reason);
        long cutoff = nowMs - WindowMs;
        long[] recent = failures.Where(timestamp => timestamp > cutoff).ToArray();
        var next = new RestartPolicy(MaxRestarts, WindowMs, recent);
        if (recent.Length >= MaxRestarts)
            return (next, new RestartDecision.GiveUp(reason));

        long delayMs = BackoffMilliseconds[Math.Min(recent.Length, BackoffMilliseconds.Length - 1)];
        long[] withFailure = [.. recent, nowMs];
        next = new RestartPolicy(MaxRestarts, WindowMs, withFailure);
        return (next, new RestartDecision.Restart("cpu", TimeSpan.FromMilliseconds(delayMs)));
    }
}
