using System.Collections.Immutable;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Recording;

namespace Somning.Core.Dictation;

public enum CapturePhase
{
    Starting,
    Recording,
    Stopping
}

/// <summary>The one microphone capture that may be live at a time (separate from the processing FIFO).</summary>
public sealed record CaptureSlot(string Id, CapturePhase Phase, bool StopRequested);

/// <summary>A start that arrived while the previous capture was still stopping; executed on its CaptureStopped.</summary>
public sealed record PendingStart(string Id, string Mode, long NowMs);

/// <summary>Immutable per-recording record: paths, paste target, timings and durable stage.</summary>
public sealed record RecordingRecord(
    string Id,
    string Mode,
    bool IsRecovery,
    long StartMs,
    long? StopMs,
    string? StopReason,
    string? WavPath,
    PasteTarget? Target,
    long CaptureMs,
    long MicOpenMs,
    RecordingStage Stage,
    string? Text,
    int Attempts,
    EngineTranscription? Result)
{
    /// <summary>
    /// Builds the sidecar persisted by UpdateSidecar. The reducer never reads a wall clock, so
    /// CreatedUtc is the Unix epoch placeholder; the sidecar adapter stamps the real creation time on
    /// first save and preserves the stored value afterwards.
    /// </summary>
    public RecordingSidecar ToSidecar(RecordingStage stage, string? outcome = null, string? reason = null, string? lastError = null) =>
        new(Id, DateTimeOffset.UnixEpoch, stage, Mode, Text, outcome, reason, Attempts, lastError);
}

public sealed record DictationState(
    CaptureSlot? Capture,
    PendingStart? NextStart,
    ImmutableDictionary<string, RecordingRecord> Records,
    ImmutableQueue<string> Pending,
    string? InFlight,
    bool EngineReady,
    bool RecoveryAdmissionPending = false)
{
    public static DictationState Initial { get; } = new(
        null,
        null,
        ImmutableDictionary.Create<string, RecordingRecord>(StringComparer.Ordinal),
        ImmutableQueue<string>.Empty,
        null,
        false);
}
