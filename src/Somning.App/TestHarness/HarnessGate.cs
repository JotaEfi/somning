#if SOMNING_TEST_HARNESS
namespace Somning.App.TestHarness;

/// <summary>
/// Debug/SOMNING_TEST_HARNESS-only one-shot gate. When armed, the next <see cref="WaitIfArmedAsync"/> waits
/// asynchronously until <see cref="Release"/> (or cancellation); unarmed it completes immediately. Release also
/// disarms, so a release that arrives before the waiter leaves nothing blocked. Used for the delivery barrier and
/// for delaying startup recovery processing.
/// </summary>
public sealed class HarnessGate
{
    private readonly object sync = new();
    private bool armed;
    private TaskCompletionSource? waiter;
    private TaskCompletionSource reached = NewSignal();

    public bool IsArmed
    {
        get { lock (sync) return armed; }
    }

    /// <summary>Completes when a caller is blocked at the gate.</summary>
    public Task Reached
    {
        get { lock (sync) return reached.Task; }
    }

    public void Arm()
    {
        lock (sync)
        {
            armed = true;
            if (reached.Task.IsCompleted) reached = NewSignal();
        }
    }

    public void Release()
    {
        TaskCompletionSource? toRelease;
        lock (sync)
        {
            armed = false;
            toRelease = waiter;
            waiter = null;
        }
        toRelease?.TrySetResult();
    }

    public Task WaitIfArmedAsync(CancellationToken ct)
    {
        TaskCompletionSource current;
        lock (sync)
        {
            if (!armed) return Task.CompletedTask;
            armed = false;
            current = waiter ??= NewSignal();
            reached.TrySetResult();
        }
        return current.Task.WaitAsync(ct);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
#endif
