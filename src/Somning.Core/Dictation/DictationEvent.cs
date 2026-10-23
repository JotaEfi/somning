using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Trigger;

namespace Somning.Core.Dictation;

/// <summary>Inputs to the pure reducer. Every timestamp is a monotonic millisecond value carried by the event.</summary>
public abstract record DictationEvent;

public sealed record TriggerFired(TriggerResult Result, long NowMs, string NewId) : DictationEvent;

public sealed record CaptureStarted(string Id, long MicOpenMs) : DictationEvent;

public sealed record CaptureStartFailed(string Id, string Reason) : DictationEvent;

public sealed record CaptureStopped(string Id, PasteTarget Target, long CaptureMs, string WavPath) : DictationEvent;

public sealed record CaptureFaulted(string Id, string Code, string Message, long NowMs) : DictationEvent;

public sealed record TranscriptionCompleted(string Id, EngineTranscription Result, long CompletedMs) : DictationEvent;

public sealed record DeliveryCompleted(string Id, DeliveryResult Result, long CompletedMs) : DictationEvent;

public sealed record EngineStatusChanged(EngineStatus Status) : DictationEvent;

public sealed record RecoveryRequested(string Id, string WavPath, string? SavedText, int Attempts, string Mode) : DictationEvent;

/// <summary>Startup recovery admission finished: recovery IDs are ordered before live IDs and dispatch resumes.</summary>
public sealed record RecoveryAdmissionCompleted : DictationEvent;
