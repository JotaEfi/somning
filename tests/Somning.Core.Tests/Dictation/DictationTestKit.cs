using System.Collections.Immutable;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Trigger;

namespace Somning.Core.Tests.Dictation;

/// <summary>Small helpers that build reducer events and fold sequences through DictationController.Reduce.</summary>
internal static class DictationTestKit
{
    public static readonly PasteTarget TargetA = new(0x1001, 0x1002, 11, 12, true, 0x2000);
    public static readonly PasteTarget TargetB = new(0x2001, 0x2002, 21, 22, true, 0x2000);

    public static DictationEvent Start(long nowMs, string id) =>
        new TriggerFired(new TriggerResult(TriggerCommand.StartRecording, TriggerMode.Unknown), nowMs, id);

    public static DictationEvent Stop(long nowMs, TriggerMode mode = TriggerMode.Toggle, StopReason reason = StopReason.Tap) =>
        new TriggerFired(new TriggerResult(TriggerCommand.StopRecording, mode, reason), nowMs, "unused-" + nowMs);

    public static DictationEvent Ready() => new EngineStatusChanged(EngineStatus.Ready);

    public static DictationEvent Stopped(string id, PasteTarget target, long captureMs = 5000) =>
        new CaptureStopped(id, target, captureMs, $"C:/rec/{id}.wav");

    public static EngineTranscription TextResult(string text) =>
        new(text, "text", null, null, false, 5000, 12, 900, 3, 0, 0, "vulkan", "ggml-large-v3-turbo-q5_0.bin", 0.01f);

    public static EngineTranscription Empty(string reason = "no_speech") =>
        new("", "empty", reason, null, false, 5000, 12, 0, 3, 0, 0, "vulkan", "ggml-large-v3-turbo-q5_0.bin", 0.9f);

    public static EngineTranscription Error(string code, bool retryable) =>
        new("", "error", null, code, retryable, 0, 0, 0, 0, 0, 0, "cpu", "", 0f);

    public static DeliveryResult Delivery(string outcome, string? reason = null) => new(outcome, reason, 4, 30, 7);

    public static (DictationState State, ImmutableArray<DictationEffect> Effects) Step(DictationState state, DictationEvent e) =>
        DictationController.Reduce(state, e);

    /// <summary>Folds all events; returns the final state and every effect in emission order.</summary>
    public static (DictationState State, List<DictationEffect> Effects) Run(DictationState state, params DictationEvent[] events)
    {
        var all = new List<DictationEffect>();
        foreach (var e in events)
        {
            var (next, effects) = DictationController.Reduce(state, e);
            state = next;
            all.AddRange(effects);
        }
        return (state, all);
    }

    /// <summary>Ready engine, one recording started, recording, stopped and dispatched (InFlight = id).</summary>
    public static DictationState InFlightRecording(string id = "rec-1", TriggerMode mode = TriggerMode.Toggle,
        StopReason reason = StopReason.Tap, long startMs = 1000, long stopMs = 6000) =>
        Run(DictationState.Initial, Ready(), Start(startMs, id), new CaptureStarted(id, 40), Stop(stopMs, mode, reason),
            Stopped(id, TargetA)).State;

    /// <summary>Same as <see cref="InFlightRecording"/> plus a successful transcription (stage transcribed).</summary>
    public static DictationState TranscribedRecording(string id, string text) =>
        Step(InFlightRecording(id), new TranscriptionCompleted(id, TextResult(text), 7000)).State;

    public static List<DictationMetrics> Metrics(IEnumerable<DictationEffect> effects) =>
        effects.OfType<EmitMetrics>().Select(effect => effect.Metrics).ToList();
}
