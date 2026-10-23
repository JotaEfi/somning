using Somning.Core.Output;
using Somning.Core.Recording;

namespace Somning.Core.Dictation;

/// <summary>Outputs of the pure reducer; adapters execute them in order.</summary>
public abstract record DictationEffect;

public sealed record StartCapture(string Id, string Mode) : DictationEffect;

/// <summary>The adapter captures the paste target FIRST, then stops the capture.</summary>
public sealed record StopCapture(string Id) : DictationEffect;

/// <summary>Must be persisted before any later effect of the same batch runs.</summary>
public sealed record UpdateSidecar(RecordingSidecar Sidecar) : DictationEffect;

public sealed record Transcribe(string Id, string WavPath) : DictationEffect;

public sealed record Deliver(string Id, string Text, PasteTarget Target) : DictationEffect;

public sealed record CopyOnly(string Id, string Text) : DictationEffect;

public sealed record EmitMetrics(DictationMetrics Metrics) : DictationEffect;
