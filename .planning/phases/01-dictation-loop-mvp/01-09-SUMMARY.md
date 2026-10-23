---
phase: 01-dictation-loop-mvp
plan: "09"
subsystem: dictation-reducer-and-metrics
tags: [app-06, aud-04, hotk-02, hotk-03, stt-04, pure-reducer, overlapping-recordings, metrics, redaction]
requires:
  - phase: 01-03
    provides: TriggerResult/StopReason, RecordingSidecar/RecordingStage, RecoveryPlanner
  - phase: 01-04
    provides: DeliveryResult outcomes (pasted|paste_deferred|clipboard_failed|clipboard_set), PasteTarget
  - phase: 01-07
    provides: Core EngineTranscription/EngineStatus and supervisor queue-before-ready semantics
  - phase: 01-08
    provides: Selected engine defaults (backend/model strings carried into metrics)
provides:
  - Somning.Core.Dictation pure reducer DictationController.Reduce with per-id RecordingRecord, CaptureSlot, FIFO
  - DictationEvent / DictationEffect contracts consumed by plan 01-10 (EffectRunner, DictationHost)
  - Startup admission extension (RecoveryAdmissionPending, RecoveryAdmissionCompleted) consumed by plan 01-11
  - DictationMetrics with the single FieldNames schema (16 fields incl. load_ms, warm_ms, stop_to_paste_ms)
  - MetricsLogger (one structured line per dictation) and RedactionPolicy (key/token/authorization masked)
  - LoggingSetup.Initialize(string logsDir) overload; file template now includes {Properties:j}
affects: [01-10, 01-11, 01-12, 01-13]
tech-stack:
  added: []
  patterns: [pure-reducer-with-effects, capture-slot-separate-from-fifo, durable-before-deliver, stable-recovery-first-partition, single-schema-metrics]
key-files:
  created:
    - src/Somning.Core/Dictation/DictationState.cs
    - src/Somning.Core/Dictation/DictationEvent.cs
    - src/Somning.Core/Dictation/DictationEffect.cs
    - src/Somning.Core/Dictation/DictationController.cs
    - src/Somning.Core/Dictation/DictationMetrics.cs
    - src/Somning.App/Logging/MetricsLogger.cs
    - src/Somning.App/Logging/RedactionPolicy.cs
    - tests/Somning.Core.Tests/Dictation/DictationTestKit.cs
    - tests/Somning.Core.Tests/Dictation/DictationControllerTests.cs
    - tests/Somning.Core.Tests/Dictation/OverlapScenarioTests.cs
    - tests/Somning.Integration.Tests/Logging/MetricsLogTests.cs
    - .planning/phases/01-dictation-loop-mvp/deferred-items.md
  modified:
    - src/Somning.App/Logging/LoggingSetup.cs
key-decisions:
  - "Queue dispatch waits for initial engine startup only: EngineReady = status != Starting. Ready/Restarting dispatch (supervisor queues during restarts) and Failed dispatches too (supervisor fails fast with a retryable error, so speech stays captured instead of being stuck in the reducer)."
  - "Saved-text recovery items are copied (CopyOnly) without waiting for the engine; audio items wait for EngineReady; the FIFO is strict (a blocked head blocks later items)."
  - "The reducer never reads a wall clock, so RecordingRecord.ToSidecar sets CreatedUtc = DateTimeOffset.UnixEpoch; the sidecar adapter (01-10 EffectRunner/SidecarStore) must stamp the real time on first save and preserve the stored CreatedUtc afterwards."
  - "Mode is unknown at StartRecording (interpreter decides tap/hold at release), so the record mode starts as 'unknown' and is set from the StopRecording result (Toggle→tap, Hold→hold)."
  - "Metrics reason: outcome-specific reason first (error code, delivery reason, empty reason), else the abnormal stop reason (max_length | missed_key_up); normal tap/release stops carry no reason."
  - "A live Deliver that returns clipboard_set is stored as stage clipboard_set with metrics outcome paste_deferred (reason clipboard_set); any unknown delivery outcome is handled like clipboard_failed (kept transcribed, Attempts+1)."
  - "Records are removed from state once their single EmitMetrics is produced, keeping the state bounded."
requirements-completed: [APP-06, AUD-04, HOTK-02, HOTK-03, STT-04]
duration: "~45 min"
completed: 2026-10-23
status: complete
estimate:
  tokens: 85000
  tasks: 2
actuals:
  tokens: 16600
  tasks: 2
  commits: 4
plan_head_before: cdfdd384672f68cdc6536ce39a943006cb814694
plan_head_after: 5bdc865742223ecbec4125f1a76d0f0a34a1e758
---

# Phase 1 Plan 9: Dictation Reducer and Metrics Summary

**Pure `DictationController.Reduce` with per-recording records, a capture slot kept apart from the FIFO, and recovery admission ordering. Paired with a 16-field `DictationMetrics` schema that `MetricsLogger` writes as one redacted line per dictation that never includes the transcript.**

## Accomplishments

- `Somning.Core.Dictation` types match the plan's `<interfaces>`. `DictationState` gets the trailing `RecoveryAdmissionPending = false` flag, and there is a `RecoveryAdmissionCompleted` event. Every type is an immutable record, state changes go through `with` on `ImmutableDictionary`/`ImmutableQueue`, and the reducer does no I/O and reads no clock.
- Happy path: start → `[UpdateSidecar(captured), StartCapture]` → recording → `[StopCapture]` → `CaptureStopped` (slot cleared, id enqueued) → `[Transcribe]` → `[UpdateSidecar(transcribed, text), Deliver(own target)]` → `[UpdateSidecar(pasted|paste_deferred), EmitMetrics]` → next id is dequeued.
- Races (H8):
  - A stop that arrives while capture is Starting sets `StopRequested`, and `CaptureStarted` then emits `[StopCapture]`.
  - A start that arrives while the previous capture is Stopping sets `NextStart`. The old capture's `CaptureStopped` then emits `[UpdateSidecar(captured, new), StartCapture(new)]` plus the old id's `Transcribe`.
  - A stop for a pending `NextStart` is remembered and runs on its `CaptureStarted`.
- Stale or unknown ids (completion events for ids that are not `InFlight`, capture events for ids that are not in the slot) return the same state instance with no effects.
- Failure paths (H6, M11): none of them makes a recording terminal before its text is on the clipboard.
  - Empty transcription: sidecar becomes `empty`, nothing is delivered.
  - Retryable error: sidecar stays `captured` with Attempts+1 and LastError set.
  - Non-retryable error: sidecar becomes `failed`.
  - `clipboard_failed`: sidecar stays `transcribed` with the same text, Attempts+1 and LastError `clipboard_failed`.
  - `CaptureStartFailed`: sidecar becomes `failed`.
  - `CaptureFaulted`: sidecar stays `captured` and the metrics reason is `capture_failed:<code>`.
- Recovery (H9): `RecoveryRequested` items go into the same FIFO. Saved text goes straight to `CopyOnly`; items without text go `Transcribe` → `CopyOnly`. They are never sent through `Deliver`. `clipboard_set` marks the sidecar `recovered` with metrics outcome `recovered`.
- Startup admission: while it is pending, the capture lifecycle still reduces fully but Transcribe and CopyOnly are not dispatched. `RecoveryAdmissionCompleted` reorders the queue with recovery ids first and live ids second, keeping each group's order.
- `DictationMetrics.FieldNames` is the one schema (id … chars, including `load_ms`, `warm_ms`, `stop_to_paste_ms`), and `ToFields()` follows the same order. `stop_to_paste_ms` is the event's `CompletedMs` minus the stop `NowMs`.
- `MetricsLogger.Write` adds every `FieldNames` entry as a scalar property, writes `dictation {Id} {Mode} {Outcome}`, and appends compact JSON built with `Utf8JsonWriter`.
- `RedactionPolicy` masks destructured properties whose names contain key, token or authorization as `***`. It is registered through `Destructure.With` in `LoggingSetup`.

## Task Commits

1. **Task 1 (tracer): reducer happy path + metrics logger**
   - RED `f163664` `test(01-09): failing happy-path reducer and metrics log tests`
   - GREEN `2e0b0a4` `feat(01-09): pure dictation reducer happy path and privacy-safe metrics logger`
   - Tracer gate: `<verify>` re-run end-to-end passed before expanding.
2. **Task 2 (tdd): edge cases, overlap, recovery admission**
   - RED `5c22ce6` `test(01-09): failing reducer edge-case and overlap scenario tests`. With the admission type surface added, 15/24 controller tests and 2/4 overlap tests failed at runtime.
   - GREEN `5bdc865` `feat(01-09): reducer edge cases, overlapping recordings and recovery admission`

## TDD Gate Compliance

Each task has a `test(...)` RED commit followed by a `feat(...)` GREEN commit. No refactor commit was needed, and every handler is under 50 lines.

## Test Evidence

- `dotnet build Somning.sln`: 0 warnings, 0 errors (TreatWarningsAsErrors on).
- Plan verifies:
  - `DictationControllerTests`: 24/24 passed.
  - `OverlapScenarioTests`: 4/4 passed.
  - `MetricsLogTests`: 1/1 passed.
- Full `Somning.Core.Tests`: **100 total, 100 passed, 0 failed, 0 skipped.**
- Full `Somning.Integration.Tests`: **67 total, 65 passed, 2 failed, 0 skipped.** Both failures are in `HoldWatchdogTests` (`Held_chord_3500ms_is_not_cut`, `Lost_trigger_up_is_caught_by_modifier_watchdog`). They come from a `PeriodicTimer` double-wait bug in the test support file `HookTriggerLoop.cs`, which dates from plan 01-03. The same two tests fail the same way on base commit `cdfdd38` before any 01-09 change. They are out of scope here and logged in `deferred-items.md`. No existing test was weakened or removed.
- Acceptance greps:
  - `load_ms` appears in `DictationMetrics.cs`.
  - `FieldNames` appears in `MetricsLogger.cs`.
  - The Text/Transcript field grep prints 0.
  - The PInvoke/NAudio/Process/File./DateTime/TickCount grep on the controller prints 0.
  - `clipboard_failed` and `RecoveryRequested` both appear in the controller.
- The model files are not needed: the new tests are pure Core tests plus one file-logging test.

## Deviations from Plan

1. **[Rule 2 - Correctness] Engine-readiness gating.** The plan says to dispatch "once EngineReady". The reducer sets `EngineReady = status != Starting` instead of `== Ready`. With a strict `== Ready`, an engine in `Failed` (or a long `Restarting`) would leave captured speech stuck in the reducer for the whole session with no feedback. The 01-07 supervisor already queues during restarts and fails fast with a retryable error, so the speech stays recoverable. Documented in key-decisions.
2. **[Rule 2 - Correctness] Placeholder for sidecar `CreatedUtc`.** The `DateTime` grep ban and the pure-reducer rule keep the controller from getting a wall-clock creation time. `RecordingRecord.ToSidecar` uses `DateTimeOffset.UnixEpoch`; the adapter has to stamp the real time and preserve it (see Open Issues).
3. **[Rule 3 - Blocking] Test helper file.** Added `tests/Somning.Core.Tests/Dictation/DictationTestKit.cs`, which is not in the plan's file list, to share the event builders and the fold helper between the two test classes.
4. **[Rule 3 - Blocking] `LoggingSetup.Initialize(string logsDir)` overload.** Needed so `MetricsLogTests` can log into a temp directory. `Initialize()` delegates to it with `SomningPaths.LogsDir`.

## Known Stubs

- `src/Somning.Core/Dictation/DictationState.cs` `RecordingRecord.ToSidecar`: `CreatedUtc = DateTimeOffset.UnixEpoch` is intentional because the pure reducer has no clock. Plan 01-10's EffectRunner/SidecarStore must replace it. This does not block this plan's goal.

## Open Issues for Plan 01-10

- **Preserve `CreatedUtc`.** EffectRunner must stamp `CreatedUtc` on the first `UpdateSidecar` for an id and preserve the stored value on later saves, including saves for recovered ids. `RecoveryPlanner` orders by `CreatedUtc` and then by Id, so if the epoch placeholder is persisted, ordering falls back to the id timestamp format.
- **Feed `EngineStatusChanged` from the start.** DictationHost must post `EngineStatusChanged` for every supervisor status, including the first `Ready`. Until then, captured audio waits in `Pending`; saved-text recovery is not blocked.
- **Order the event loop.** `TriggerFired.NewId` must be unique per start. The reducer ignores a start whose id already exists. Feed `CaptureStarted`/`CaptureStopped` only after the matching effect has run; the slot checks drop anything else as stale.
- **Avoid the `PeriodicTimer` bug.** DictationHost's 50 ms Tick task must not reuse the double-wait pattern from `HookTriggerLoop` (see `deferred-items.md`).
- **Fix the watchdog tests.** The two pre-existing `HoldWatchdogTests` failures should be fixed in a small follow-up so the integration suite is green.

## Threat Flags

None. No new network endpoints, auth paths or packages (T-01-SC). The log file surface was already covered by T-01-19 and is mitigated by tests.

## Self-Check: PASSED

- All 11 files listed under key-files.created exist, and `LoggingSetup.cs` is modified.
- Commits `f163664`, `2e0b0a4`, `5c22ce6` and `5bdc865` are present in `git log`.
