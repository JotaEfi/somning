using System.Collections.Immutable;
using Somning.Core.Engine;
using Somning.Core.Recording;
using Somning.Core.Trigger;

namespace Somning.Core.Dictation;

/// <summary>
/// Pure reducer (state, event) → (state, effects). No I/O and no clock reads: every timestamp arrives in an event.
/// Capture lifecycle (one CaptureSlot) is separate from the FIFO processing queue; every recording has its own
/// immutable record, and completion events only touch their own id (D-07, D-11, review H8).
/// </summary>
public static class DictationController
{
    private const string ClipboardFailed = "clipboard_failed";
    private static readonly ImmutableArray<DictationEffect> None = ImmutableArray<DictationEffect>.Empty;

    public static (DictationState State, ImmutableArray<DictationEffect> Effects) Reduce(DictationState state, DictationEvent e)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(e);
        return e switch
        {
            TriggerFired fired => OnTrigger(state, fired),
            CaptureStarted started => OnCaptureStarted(state, started),
            CaptureStartFailed failed => OnCaptureStartFailed(state, failed),
            CaptureStopped stopped => OnCaptureStopped(state, stopped),
            CaptureFaulted faulted => OnCaptureFaulted(state, faulted),
            TranscriptionCompleted completed => OnTranscription(state, completed),
            DeliveryCompleted delivered => OnDelivery(state, delivered),
            EngineStatusChanged status => OnEngineStatus(state, status),
            RecoveryRequested recovery => OnRecoveryRequested(state, recovery),
            RecoveryAdmissionCompleted => OnRecoveryAdmissionCompleted(state),
            _ => (state, None)
        };
    }

    // ---- Trigger ------------------------------------------------------------------------------------------

    private static (DictationState, ImmutableArray<DictationEffect>) OnTrigger(DictationState state, TriggerFired fired) =>
        fired.Result.Command switch
        {
            TriggerCommand.StartRecording => OnStart(state, fired),
            TriggerCommand.StopRecording => OnStop(state, fired),
            _ => (state, None)
        };

    private static (DictationState, ImmutableArray<DictationEffect>) OnStart(DictationState state, TriggerFired fired)
    {
        if (state.Records.ContainsKey(fired.NewId)) return (state, None);
        var mode = ModeName(fired.Result.Mode);
        var record = new RecordingRecord(fired.NewId, mode, false, fired.NowMs, null, null, null, null, 0, 0,
            RecordingStage.Captured, null, 0, null);
        var withRecord = state with { Records = state.Records.SetItem(fired.NewId, record) };

        if (state.Capture is null) return BeginCapture(withRecord, fired.NewId, None);
        if (state.Capture.Phase == CapturePhase.Stopping && state.NextStart is null)
            return (withRecord with { NextStart = new PendingStart(fired.NewId, mode, fired.NowMs) }, None);
        return (state, None);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnStop(DictationState state, TriggerFired fired)
    {
        switch (state.Capture)
        {
            case { Phase: CapturePhase.Starting } starting:
                return (WithStopInfo(state, starting.Id, fired) with { Capture = starting with { StopRequested = true } }, None);
            case { Phase: CapturePhase.Recording } recording:
                var next = WithStopInfo(state, recording.Id, fired) with { Capture = recording with { Phase = CapturePhase.Stopping } };
                return (next, [new StopCapture(recording.Id)]);
            case { Phase: CapturePhase.Stopping } when state.NextStart is { } pendingStart:
                return (WithStopInfo(state, pendingStart.Id, fired), None);
            default:
                return (state, None);
        }
    }

    private static DictationState WithStopInfo(DictationState state, string id, TriggerFired fired)
    {
        var records = Update(state, id, record => record with
        {
            StopMs = fired.NowMs,
            StopReason = StopReasonName(fired.Result.Reason),
            Mode = fired.Result.Mode == TriggerMode.Unknown ? record.Mode : ModeName(fired.Result.Mode)
        });
        return state with { Records = records };
    }

    // ---- Capture lifecycle --------------------------------------------------------------------------------

    /// <summary>Puts the id into the (empty) capture slot; a stop already recorded for it is remembered.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) BeginCapture(
        DictationState state, string id, ImmutableArray<DictationEffect> effects)
    {
        var record = state.Records[id];
        var next = state with { Capture = new CaptureSlot(id, CapturePhase.Starting, record.StopMs is not null), NextStart = null };
        return (next, effects.AddRange(new UpdateSidecar(record.ToSidecar(RecordingStage.Captured)), new StartCapture(id, record.Mode)));
    }

    /// <summary>Clears the capture slot and starts the start that was waiting for it, if any.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) ReleaseSlot(DictationState state, ImmutableArray<DictationEffect> effects)
    {
        var cleared = state with { Capture = null };
        return state.NextStart is { } pending && cleared.Records.ContainsKey(pending.Id)
            ? BeginCapture(cleared, pending.Id, effects)
            : (cleared with { NextStart = null }, effects);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnCaptureStarted(DictationState state, CaptureStarted started)
    {
        if (state.Capture is not { Phase: CapturePhase.Starting } slot || slot.Id != started.Id) return (state, None);
        var records = Update(state, started.Id, record => record with { MicOpenMs = started.MicOpenMs });
        if (!slot.StopRequested)
            return (state with { Records = records, Capture = slot with { Phase = CapturePhase.Recording } }, None);
        return (state with { Records = records, Capture = slot with { Phase = CapturePhase.Stopping } }, [new StopCapture(slot.Id)]);
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
        var queued = state with { Records = records, Pending = state.Pending.Enqueue(stopped.Id) };
        var (released, effects) = ReleaseSlot(queued, None);
        return TryDispatch(released, effects);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnCaptureStartFailed(DictationState state, CaptureStartFailed failed)
    {
        if (state.Capture is not { } slot || slot.Id != failed.Id || !state.Records.TryGetValue(failed.Id, out var record))
            return (state, None);
        var (released, effects) = ReleaseSlot(state, None);
        var sidecar = record.ToSidecar(RecordingStage.Failed, "error", failed.Reason, failed.Reason);
        var metrics = BuildMetrics(record, "error", "capture_start_failed:" + failed.Reason, 0, record.StopMs ?? 0);
        return CompleteAndAdvance(released, record.Id, effects.Add(new UpdateSidecar(sidecar)), metrics);
    }

    /// <summary>Device loss after start: the spool is finalized by the adapter, so the audio stays captured (recoverable).</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) OnCaptureFaulted(DictationState state, CaptureFaulted faulted)
    {
        if (state.Capture is not { } slot || slot.Id != faulted.Id || !state.Records.TryGetValue(faulted.Id, out var record))
            return (state, None);
        var stopped = record with { StopMs = record.StopMs ?? faulted.NowMs };
        var (released, effects) = ReleaseSlot(state, None);
        var sidecar = stopped.ToSidecar(RecordingStage.Captured, "error", faulted.Code, faulted.Code);
        var metrics = BuildMetrics(stopped, "error", "capture_failed:" + faulted.Code, 0, faulted.NowMs);
        return CompleteAndAdvance(released, record.Id, effects.Add(new UpdateSidecar(sidecar)), metrics);
    }

    // ---- Processing queue ---------------------------------------------------------------------------------

    private static (DictationState, ImmutableArray<DictationEffect>) OnTranscription(DictationState state, TranscriptionCompleted completed)
    {
        if (state.InFlight != completed.Id || !state.Records.TryGetValue(completed.Id, out var record)) return (state, None);
        var result = completed.Result;
        if (result.Outcome == "error" || result.ErrorCode is not null) return OnTranscriptionError(state, record, completed);
        if (result.Outcome == "empty" || string.IsNullOrWhiteSpace(result.Text))
        {
            var empty = record with { Text = null, Result = result };
            var metrics = BuildMetrics(empty, "empty", result.Reason, 0, completed.CompletedMs);
            return CompleteAndAdvance(state, record.Id, [new UpdateSidecar(empty.ToSidecar(RecordingStage.Empty, "empty", result.Reason))], metrics);
        }

        // The text is durable (sidecar transcribed) before any delivery is attempted (review H6).
        var transcribed = record with { Stage = RecordingStage.Transcribed, Text = result.Text, Result = result };
        var next = state with { Records = state.Records.SetItem(record.Id, transcribed) };
        return (next, [new UpdateSidecar(transcribed.ToSidecar(RecordingStage.Transcribed)), DeliveryFor(transcribed)]);
    }

    /// <summary>Retryable errors keep the audio captured (recoverable, Attempts+1); non-retryable ones mark it failed.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) OnTranscriptionError(
        DictationState state, RecordingRecord record, TranscriptionCompleted completed)
    {
        var result = completed.Result;
        var code = result.ErrorCode ?? result.Reason ?? "error";
        var stage = result.Retryable ? RecordingStage.Captured : RecordingStage.Failed;
        var failed = record with { Stage = stage, Attempts = record.Attempts + 1, Result = result };
        var metrics = BuildMetrics(failed, "error", code, 0, completed.CompletedMs);
        return CompleteAndAdvance(state, record.Id, [new UpdateSidecar(failed.ToSidecar(stage, "error", code, code))], metrics);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnDelivery(DictationState state, DeliveryCompleted delivered)
    {
        if (state.InFlight != delivered.Id || !state.Records.TryGetValue(delivered.Id, out var record) || record.Text is null)
            return (state, None);
        var result = delivered.Result;
        var (stage, outcome) = DeliveryStage(record, result.Outcome);
        if (stage is null)
        {
            // Never terminal before the text is secured on the clipboard: keep it transcribed and recoverable (H6, M11).
            var kept = record with { Attempts = record.Attempts + 1 };
            var error = result.Outcome == ClipboardFailed ? ClipboardFailed : result.Outcome;
            var failedMetrics = BuildMetrics(kept, "error", error, result.PasteMs, delivered.CompletedMs);
            var sidecar = kept.ToSidecar(RecordingStage.Transcribed, "error", result.Reason ?? error, error);
            return CompleteAndAdvance(state, record.Id, [new UpdateSidecar(sidecar)], failedMetrics);
        }

        var reason = result.Reason ?? (result.Outcome == "clipboard_set" && !record.IsRecovery ? "clipboard_set" : null);
        var metrics = BuildMetrics(record, outcome, reason, result.PasteMs, delivered.CompletedMs);
        return CompleteAndAdvance(state, record.Id, [new UpdateSidecar(record.ToSidecar(stage.Value, outcome, reason))], metrics);
    }

    /// <summary>Maps a delivery outcome to its durable stage and metrics outcome; null stage = not secured.</summary>
    private static (RecordingStage? Stage, string Outcome) DeliveryStage(RecordingRecord record, string outcome) => outcome switch
    {
        "pasted" or "paste_deferred" or "clipboard_set" when record.IsRecovery => (RecordingStage.Recovered, "recovered"),
        "pasted" => (RecordingStage.Pasted, "pasted"),
        "paste_deferred" => (RecordingStage.PasteDeferred, "paste_deferred"),
        "clipboard_set" => (RecordingStage.ClipboardSet, "paste_deferred"),
        _ => (null, "error")
    };

    /// <summary>Recovered text is placed on the clipboard only (D-11); live text goes to its own captured target.</summary>
    private static DictationEffect DeliveryFor(RecordingRecord record) =>
        !record.IsRecovery && record.Target is { } target
            ? new Deliver(record.Id, record.Text!, target)
            : new CopyOnly(record.Id, record.Text!);

    private static (DictationState, ImmutableArray<DictationEffect>) OnEngineStatus(DictationState state, EngineStatusChanged status)
    {
        // Anything past initial startup accepts dispatch: the supervisor queues during restarts and fails fast when
        // failed, so queued speech is never stuck nor dropped (STT-04).
        var next = state with { EngineReady = status.Status != EngineStatus.Starting };
        return TryDispatch(next, None);
    }

    private static (DictationState, ImmutableArray<DictationEffect>) OnRecoveryRequested(DictationState state, RecoveryRequested request)
    {
        if (state.Records.ContainsKey(request.Id)) return (state, None);
        var savedText = string.IsNullOrWhiteSpace(request.SavedText) ? null : request.SavedText;
        var record = new RecordingRecord(request.Id, request.Mode, true, 0, null, null, request.WavPath, null, 0, 0,
            savedText is null ? RecordingStage.Captured : RecordingStage.Transcribed, savedText, request.Attempts, null);
        var next = state with
        {
            Records = state.Records.SetItem(request.Id, record),
            Pending = state.Pending.Enqueue(request.Id)
        };
        return TryDispatch(next, None);
    }

    /// <summary>Stable partition: recovery ids first, then live ids, each group keeping its arrival order.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) OnRecoveryAdmissionCompleted(DictationState state)
    {
        var ids = state.Pending.ToArray();
        bool IsRecovery(string id) => state.Records.TryGetValue(id, out var record) && record.IsRecovery;
        var ordered = ids.Where(IsRecovery).Concat(ids.Where(id => !IsRecovery(id)));
        var next = state with { Pending = ImmutableQueue.CreateRange(ordered), RecoveryAdmissionPending = false };
        return TryDispatch(next, None);
    }

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

    /// <summary>Strict FIFO: saved text is copied without the engine; audio waits for the engine; nothing while admission is pending.</summary>
    private static (DictationState, ImmutableArray<DictationEffect>) TryDispatch(DictationState state, ImmutableArray<DictationEffect> effects)
    {
        if (state.InFlight is not null || state.RecoveryAdmissionPending) return (state, effects);
        var pending = state.Pending;
        while (!pending.IsEmpty)
        {
            var id = pending.Peek();
            if (!state.Records.TryGetValue(id, out var record))
            {
                pending = pending.Dequeue();
                continue;
            }

            if (record.Text is not null)
                return (state with { Pending = pending.Dequeue(), InFlight = id }, effects.Add(DeliveryFor(record)));
            if (!state.EngineReady || record.WavPath is null) break;
            return (state with { Pending = pending.Dequeue(), InFlight = id }, effects.Add(new Transcribe(id, record.WavPath)));
        }
        return (state with { Pending = pending }, effects);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

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
