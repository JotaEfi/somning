using System.Collections.Immutable;
using Somning.Core.Engine;
using Somning.Core.Recording;
using Somning.Core.Trigger;

namespace Somning.Core.Dictation;

/// <summary>
/// Pure reducer (state, event) → (state, effects). No I/O and no clock reads: every timestamp arrives in an event.
/// </summary>
public static class DictationController
{
    private static readonly ImmutableArray<DictationEffect> None = ImmutableArray<DictationEffect>.Empty;

    public static (DictationState State, ImmutableArray<DictationEffect> Effects) Reduce(DictationState state, DictationEvent e)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(e);
        return e switch
        {
            TriggerFired fired => OnTrigger(state, fired),
            CaptureStarted started => OnCaptureStarted(state, started),
            CaptureStopped stopped => OnCaptureStopped(state, stopped),
            TranscriptionCompleted completed => OnTranscription(state, completed),
            DeliveryCompleted delivered => OnDelivery(state, delivered),
            EngineStatusChanged status => (state with { EngineReady = status.Status == EngineStatus.Ready }, None),
            _ => (state, None)
        };
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnTrigger(DictationState state, TriggerFired fired) =>
        fired.Result.Command switch
        {
            TriggerCommand.StartRecording => OnStart(state, fired),
            TriggerCommand.StopRecording => OnStop(state, fired),
            _ => (state, None)
        };

    private static (DictationState, ImmutableArray<DictationEffect>) OnStart(DictationState state, TriggerFired fired)
    {
        if (state.Capture is not null || state.Records.ContainsKey(fired.NewId)) return (state, None);
        var mode = ModeName(fired.Result.Mode);
        var record = new RecordingRecord(fired.NewId, mode, false, fired.NowMs, null, null, null, null, 0, 0,
            RecordingStage.Captured, null, 0, null);
        var next = state with
        {
            Capture = new CaptureSlot(fired.NewId, CapturePhase.Starting, false),
            Records = state.Records.SetItem(fired.NewId, record)
        };
        return (next, [new UpdateSidecar(record.ToSidecar(RecordingStage.Captured)), new StartCapture(fired.NewId, mode)]);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnStop(DictationState state, TriggerFired fired)
    {
        if (state.Capture is not { Phase: CapturePhase.Recording } slot) return (state, None);
        var next = WithStopInfo(state, slot.Id, fired) with { Capture = slot with { Phase = CapturePhase.Stopping } };
        return (next, [new StopCapture(slot.Id)]);
    }

    private static DictationState WithStopInfo(DictationState state, string id, TriggerFired fired)
    {
        var record = state.Records[id];
        var mode = fired.Result.Mode == TriggerMode.Unknown ? record.Mode : ModeName(fired.Result.Mode);
        var updated = record with { StopMs = fired.NowMs, StopReason = StopReasonName(fired.Result.Reason), Mode = mode };
        return state with { Records = state.Records.SetItem(id, updated) };
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnCaptureStarted(DictationState state, CaptureStarted started)
    {
        if (state.Capture is not { Phase: CapturePhase.Starting } slot || slot.Id != started.Id) return (state, None);
        var records = Update(state, started.Id, record => record with { MicOpenMs = started.MicOpenMs });
        return (state with { Records = records, Capture = slot with { Phase = CapturePhase.Recording } }, None);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnCaptureStopped(DictationState state, CaptureStopped stopped)
    {
        if (state.Capture is not { } slot || slot.Id != stopped.Id) return (state, None);
        var records = Update(state, stopped.Id, record => record with
        {
            WavPath = stopped.WavPath,
            Target = stopped.Target,
            CaptureMs = stopped.CaptureMs
        });
        var next = state with { Capture = null, Records = records, Pending = state.Pending.Enqueue(stopped.Id) };
        return TryDispatch(next, None);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnTranscription(DictationState state, TranscriptionCompleted completed)
    {
        if (state.InFlight != completed.Id || !state.Records.TryGetValue(completed.Id, out var record)) return (state, None);
        var result = completed.Result;
        var transcribed = record with { Stage = RecordingStage.Transcribed, Text = result.Text, Result = result };
        var next = state with { Records = state.Records.SetItem(record.Id, transcribed) };
        return (next, [new UpdateSidecar(transcribed.ToSidecar(RecordingStage.Transcribed)), DeliveryFor(transcribed)]);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnDelivery(DictationState state, DeliveryCompleted delivered)
    {
        if (state.InFlight != delivered.Id || !state.Records.TryGetValue(delivered.Id, out var record)) return (state, None);
        var outcome = delivered.Result.Outcome;
        var stage = outcome == "pasted" ? RecordingStage.Pasted : RecordingStage.PasteDeferred;
        var metrics = BuildMetrics(record, outcome, delivered.Result.Reason, delivered.Result.PasteMs, delivered.CompletedMs);
        return CompleteAndAdvance(state, record.Id, [new UpdateSidecar(record.ToSidecar(stage, outcome, delivered.Result.Reason))], metrics);
    }

    private static DictationEffect DeliveryFor(RecordingRecord record) =>
        new Deliver(record.Id, record.Text!, record.Target!.Value);

    /// <summary>Emits the one metrics record, forgets the id, clears InFlight and dispatches the next pending id.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) CompleteAndAdvance(
        DictationState state, string id, ImmutableArray<DictationEffect> effects, DictationMetrics metrics)
    {
        var next = state with
        {
            Records = state.Records.Remove(id),
            InFlight = state.InFlight == id ? null : state.InFlight
        };
        return TryDispatch(next, effects.Add(new EmitMetrics(metrics)));
    }

    private static (DictationState, ImmutableArray<DictationEffect>) TryDispatch(DictationState state, ImmutableArray<DictationEffect> effects)
    {
        if (state.InFlight is not null || state.Pending.IsEmpty) return (state, effects);
        var pending = state.Pending.Dequeue(out var id);
        var record = state.Records[id];
        var next = state with { Pending = pending, InFlight = id };
        return (next, effects.Add(new Transcribe(id, record.WavPath!)));
    }

    private static DictationMetrics BuildMetrics(RecordingRecord record, string outcome, string? reason, long pasteMs, long completedMs)
    {
        var result = record.Result;
        var stopToPaste = record.StopMs is { } stopMs ? Math.Max(0, completedMs - stopMs) : 0;
        return new DictationMetrics(record.Id, record.Mode, record.CaptureMs, record.MicOpenMs,
            result?.VadMs ?? 0, result?.QueueMs ?? 0, result?.InferMs ?? 0, result?.LoadMs ?? 0, result?.WarmMs ?? 0,
            result?.Backend ?? "", result?.Model ?? "", pasteMs, stopToPaste, outcome,
            reason ?? record.StopReason, record.Text?.Length ?? 0);
    }

    private static ImmutableDictionary<string, RecordingRecord> Update(
        DictationState state, string id, Func<RecordingRecord, RecordingRecord> change) =>
        state.Records.TryGetValue(id, out var record) ? state.Records.SetItem(id, change(record)) : state.Records;

    private static string ModeName(TriggerMode mode) => mode switch
    {
        TriggerMode.Toggle => "tap",
        TriggerMode.Hold => "hold",
        _ => "unknown"
    };

    /// <summary>Normal stops (tap/release) carry no reason; abnormal stops surface in metrics.</summary>
    private static string? StopReasonName(StopReason? reason) => reason switch
    {
        StopReason.MaxLength => "max_length",
        StopReason.MissedKeyUp => "missed_key_up",
        _ => null
    };
}
