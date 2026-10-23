using Somning.Core.Ports;
using Somning.Platform.Windows.Recording;

namespace Somning.App.Dictation;

/// <summary>One live microphone capture: its spool writer, its audio source and the pending start.</summary>
public sealed class CaptureSession(string Id, SpoolWriter Writer, IAudioSource Source, Task<AudioStartInfo> StartTask)
{
    private int released;

    public string Id { get; } = Id;
    public SpoolWriter Writer { get; } = Writer;
    public IAudioSource Source { get; } = Source;
    public Task<AudioStartInfo> StartTask { get; } = StartTask;
    public string WavPath { get; init; } = "";

    /// <summary>Disposes the source (no more writes), then the writer (finalizing the WAV header), exactly once.</summary>
    public async Task ReleaseAsync()
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        try { await Source.DisposeAsync().ConfigureAwait(false); }
        finally { Writer.Dispose(); }
    }
}
