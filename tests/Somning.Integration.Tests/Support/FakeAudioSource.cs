using System.Diagnostics;
using Somning.Core.Ports;

namespace Somning.Integration.Tests.Support;

/// <summary>
/// Scriptable IAudioSource: configurable start delay, start failure, mid-capture fault raised from a thread-pool
/// thread, and 20 ms silent frames written to the sink while running. Records start/stop ordering.
/// </summary>
public sealed class FakeAudioSource : IAudioSource
{
    private const int FrameBytes = 640; // 20 ms of 16 kHz mono s16
    private readonly CancellationTokenSource stop = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Task pump = Task.CompletedTask;
    private long startCompletedAt = -1;
    private long stopCalledAt = -1;
    private int stopCalls;
    private int faultRaised;

    public TimeSpan StartDelay { get; init; }
    public Exception? StartException { get; init; }
    public TimeSpan? FaultAfter { get; init; }
    public string FaultCode { get; init; } = "device_lost";
    public long MicOpenMs { get; init; } = 7;

    public int StopCalls => Volatile.Read(ref stopCalls);
    public int FramesWritten { get; private set; }
    public bool StartCompleted => Interlocked.Read(ref startCompletedAt) >= 0;

    /// <summary>True when StopAsync was entered before StartAsync had completed (review H8 violation).</summary>
    public bool StopCalledBeforeStartCompleted { get; private set; }

    public event Action<float>? Level;
    public event Action<AudioFault>? Faulted;

    public async Task<AudioStartInfo> StartAsync(IPcmSink sink, CancellationToken ct)
    {
        if (StartDelay > TimeSpan.Zero) await Task.Delay(StartDelay, ct).ConfigureAwait(false);
        if (StartException is not null) throw StartException;
        Interlocked.Exchange(ref startCompletedAt, clock.ElapsedTicks);
        pump = Task.Run(() => PumpAsync(sink, stop.Token), CancellationToken.None);
        if (FaultAfter is { } after)
            _ = Task.Run(async () =>
            {
                await Task.Delay(after).ConfigureAwait(false);
                RaiseFault();
            }, CancellationToken.None);
        return new AudioStartInfo(MicOpenMs, "fake: 16 kHz mono s16");
    }

    private async Task PumpAsync(IPcmSink sink, CancellationToken ct)
    {
        var frame = new byte[FrameBytes];
        try
        {
            while (!ct.IsCancellationRequested && Volatile.Read(ref faultRaised) == 0)
            {
                sink.Write(frame);
                FramesWritten++;
                Level?.Invoke(0);
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void RaiseFault()
    {
        if (Interlocked.Exchange(ref faultRaised, 1) != 0) return;
        Faulted?.Invoke(new AudioFault(FaultCode, "Injected fault."));
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref stopCalls);
        if (Interlocked.CompareExchange(ref stopCalledAt, clock.ElapsedTicks, -1) == -1 && !StartCompleted)
            StopCalledBeforeStartCompleted = true;
        stop.Cancel();
        await pump.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        await pump.ConfigureAwait(false);
    }
}
