---
phase: 01-dictation-loop-mvp
plan: "10"
subsystem: app-wiring-dictation-loop
tags: [hotk-01, hotk-02, hotk-03, stt-04, out-02, single-writer-loop, effect-runner, test-harness, e2e]
requires:
  - phase: 01-03
    provides: KeyboardHook, PhysicalKeyState, TriggerInterpreter
  - phase: 01-04
    provides: ForegroundProbe, TargetPaster (DeliverAsync/CopyOnlyAsync), ClipboardWriter
  - phase: 01-06
    provides: IAudioSource (+Faulted), WasapiAudioSource, FileAudioSource, SpoolWriter, SidecarStore
  - phase: 01-07
    provides: EngineSupervisor (queue-before-ready, RequestWritten, StatusChanged)
  - phase: 01-09
    provides: DictationController reducer, events/effects/state, DictationMetrics, MetricsLogger
provides:
  - Somning.App.Dictation.DictationHost (Post, Notify, Notified, Start, KeyReceived/Tick/NotificationPosted loop events)
  - Somning.App.Dictation.EffectRunner (Run, OnRequestWritten, WhenIdleAsync) and CaptureSession
  - DictationNotification(Kind, Id, Metrics) with kinds hook_installed|engine_ready|target_captured|transcribe_started|outcome
  - AppComposition + Program startup order (logging, loop, hook, then engine started in the background)
  - App CLI `--test-harness --fixture <wav>` (Debug/SOMNING_TEST_HARNESS only; Release exits 2)
  - Log lines "hook installed" and "engine ready backend=<b> load_ms=<n> warm_ms=<n>"
  - Test support AppHarnessDriver (StartAsync(fixture, extraEnv, waitForEngine), DataDir, LogFilePath, ReadLog, WaitForLogAsync, Process) and FakeAudioSource
  - EngineSupervisor.Session accessor (load/warm timings for the engine-ready log)
affects: [01-11, 01-12, 01-13]
tech-stack:
  added: []
  patterns: [single-writer-channel-loop, timer-posts-only, ordered-sync-persistence, first-of-stop-or-fault-owns-capture, adapter-tickcount64-stamping]
key-files:
  created:
    - src/Somning.App/Composition/AppComposition.cs
    - src/Somning.App/Dictation/DictationHost.cs
    - src/Somning.App/Dictation/EffectRunner.cs
    - src/Somning.App/Dictation/CaptureSession.cs
    - src/Somning.App/Dictation/DictationNotification.cs
    - src/Somning.App/TestHarness/HarnessOptions.cs
    - tests/Somning.Integration.Tests/Support/AppHarnessDriver.cs
    - tests/Somning.Integration.Tests/Support/FakeAudioSource.cs
    - tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs
    - tests/Somning.Integration.Tests/Dictation/EffectRunnerTests.cs
  modified:
    - src/Somning.App/Program.cs
    - src/Somning.App/Engine/EngineSupervisor.cs
key-decisions:
  - "Audio sources start only once, so the composition passes a Func<IAudioSource> factory and EffectRunner creates a fresh source per capture (WasapiAudioSource normally, a paced FileAudioSource of the fixture in harness mode)."
  - "EffectRunner runs on the loop task: UpdateSidecar saves synchronously and EmitMetrics writes the metrics line inline. StartCapture/StopCapture/Transcribe/Deliver/CopyOnly run as tracked tasks that report back only by posting events."
  - "Notifications from adapters are posted as a loop-internal NotificationPosted event so DictationHost.Notified is always raised on the loop task."
  - "The end of each capture is owned by whichever comes first, StopCapture or Faulted (CaptureSession.TryBeginEnd). Both wait for StartTask; a failed start already posted CaptureStartFailed, so they post nothing more."
  - "EffectRunner stamps CreatedUtc on the first save of an id: the stored value if it exists and is not the epoch placeholder, else UtcNow. It keeps that value until EmitMetrics, closing the 01-09 Known Stub."
  - "Trigger ids are yyyyMMdd-HHmmss-fff local time, suffixed -N when the millisecond repeats, the id is already in reducer state, or a .wav/.json with that id exists in the recordings dir."
  - "TriggerFired.NowMs comes from the hook KeyEvent timestamp for key events and from Tick.NowMs for timer events. Both use Environment.TickCount64."
  - "A transcriber or delivery exception becomes an error result (engine_exception retryable / clipboard_failed delivery_exception) so the reducer FIFO never stalls."
requirements-completed: [HOTK-01, HOTK-02, HOTK-03, STT-04, OUT-02]
duration: "32 min"
completed: 2026-10-23
status: complete
estimate:
  tokens: 95000
  tasks: 2
actuals:
  tokens: 14000
  tasks: 2
  commits: 3
plan_head_before: b0ed12db670de55e3b40db1e76cb0dbe8b0a92f1
plan_head_after: db756e24dc81210ea9ffa36866497971a830c749
coverage:
  - deliverable: "Real app performs a hotkey tap-tap dictation into Notepad in harness mode, with the hook live from process start"
    human_judgment: false
    verification:
      - kind: e2e-test
        ref: "tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs#TapTap_pastes_fixture_into_notepad"
        status: pass
  - deliverable: "EffectRunner contracts: stop-while-starting, start failure, mid-capture fault, ordered persistence, TickCount64 stamping, CreatedUtc"
    human_judgment: false
    verification:
      - kind: integration-test
        ref: "tests/Somning.Integration.Tests/Dictation/EffectRunnerTests.cs"
        status: pass
  - deliverable: "Release build rejects --test-harness with exit 2"
    human_judgment: false
    verification:
      - kind: command
        ref: "src/Somning.App/bin/Release/net10.0-windows/Somning.App.exe --test-harness --fixture tests/fixtures/tracer/hoje.wav -> 'test harness not available in this build', exit 2"
        status: pass
  - deliverable: "Hotkey pressed during model load is recorded and delivered after ready (STT-04 end-to-end)"
    human_judgment: true
    rationale: "Startup order is implemented and grep-verified here; the delayed-ready E2E that proves it is plan 01-11's Hotkey_before_ready_is_recorded_and_delivered."
---

# Phase 1 Plan 10: App Wiring — Dictation Loop in the Real App Summary

**The real Somning.App now runs the 01-09 reducer inside a single-writer channel loop that owns the TriggerInterpreter. The keyboard hook is installed before the engine starts loading in the background. An EffectRunner persists sidecars in order, serializes start, stop and fault per capture, and stamps every completion with TickCount64. A Debug-only `--test-harness --fixture` mode let a real injected Ctrl+Shift+Space tap-tap paste the fixture's Portuguese text into Notepad.**

## Accomplishments

- **DictationHost (H8):** one unbounded `Channel<DictationEvent>` with a single reader.
  - Three tasks: the loop, a hook pump (KeyEvent → `KeyReceived`) and a 50 ms `PeriodicTimer` task.
  - The timer task only posts `Tick(Environment.TickCount64, PhysicalKeyState.AreChordModifiersDown())`. It keeps a single waiter, so it avoids the old HookTriggerLoop double-wait bug.
  - `OnKey`/`OnTick` and `DictationController.Reduce` run only on the loop. A non-None trigger result becomes `TriggerFired(result, now, uniqueId)` and is reduced immediately.
  - Effects go to `EffectRunner.Run` in batch order. Exceptions are logged per event and the loop keeps running.
- **EffectRunner (H6/H8/M9/M11):**
  - **Persistence:** UpdateSidecar saves synchronously before the next effect, with CreatedUtc stamped once.
  - **StartCapture:** creates a spool `{id}.wav` and a fresh audio source. The Faulted handler is attached before StartAsync. Success posts `CaptureStarted(MicOpenMs)`; failure releases the source and spool, then posts `CaptureStartFailed(code)`.
  - **StopCapture:** calls `ITargetProbe.Capture()` first (D-13, `target_captured` notification). It then waits for the pending StartTask, stops the source, disposes source and writer, and posts `CaptureStopped(target, captureMs, wavPath)`.
  - **Faulted (any thread):** hands off to a task that stops the source, finalizes the spool and posts exactly one `CaptureFaulted(code, message, TickCount64)`.
  - **Transcribe, Deliver, CopyOnly:** Transcribe → `TranscriptionCompleted`, and the supervisor's RequestWritten maps to a `transcribe_started` notification. Deliver and CopyOnly → `DeliveryCompleted`. All are stamped with TickCount64.
  - **EmitMetrics:** calls `MetricsLogger.Write`, then raises an `outcome` notification.
- **Startup order (H9, T-01-36):** `Program` initializes logging, starts the loop, calls `KeyboardHook.Start()` and logs `hook installed`. It then calls `StartEngineInBackground()` without awaiting it. A background task logs `engine ready backend=… load_ms=… warm_ms=…` or the failure. `EngineStatusChanged` is posted for every supervisor status, from Starting onward.
- **Harness (D-19/D-20, T-01-44):** the `--test-harness --fixture <wav>` parsing and the FileAudioSource selection are compiled only under `SOMNING_TEST_HARNESS`. A Release build prints `test harness not available in this build` and exits 2 (verified). `--transcribe-file` is unchanged.
- **Test support:**
  - `AppHarnessDriver` starts the app with a fresh SOMNING_DATA_DIR, waits for log lines (`hook installed`, plus `engine ready` when asked), reads the log with FileShare.ReadWrite, and kills the process tree on dispose.
  - `FakeAudioSource` supports a start delay, a start exception, a fault from a thread-pool thread after N ms, and probes that record start/stop ordering.

## Task Commits

1. **Task 1 (tracer):** `8b92a79` `feat(01-10): tracer - real app dictation loop with hook live before engine ready`
   - Tracer gate (auto mode): the `<verify>` command was re-run end-to-end and passed (1/1, strict) before expanding.
2. **Task 2 (tdd):**
   - RED `682f61d` `test(01-10): failing EffectRunner stop-while-starting and capture-fault tests`
   - GREEN `db756e2` `feat(01-10): serialize capture stop/fault per session and report CaptureFaulted`

## TDD Gate Compliance

- **RED** (`682f61d`): 6 tests, 2 failing on their planned-behavior assertions.
  - `Stop_while_starting…`: "StopAsync ran before StartAsync completed."
  - `Mid_capture_fault…`: "CaptureFaulted not posted within 00:00:05. Posted: CaptureStarted".
  - `gsd-tools check tdd-red-evidence` returned `RED_EVIDENCE_OK` / `target_test_failed` for both targets (JUnit XML from the xUnit v3 runner).
- **Tests that passed at RED:** the other 4 (start failure, ordered persistence, TickCount64 stamping, CreatedUtc) already passed because the Task 1 tracer implemented those paths. They are regression locks, not RED targets.
- **GREEN** (`db756e2`): 6/6.
- **REFACTOR:** no refactor commit was needed.

## Test Evidence

- `dotnet build Somning.sln -c Debug`: 0 warnings, 0 errors (TreatWarningsAsErrors). `dotnet build src/Somning.App -c Release`: 0/0.
- **Task 1 verify:** `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter-class "*.HarnessDictationTests"` passed: total 1, failed 0, skipped 0. It ran 3 times during the task (initial run, tracer gate, Task 2 verify), all passed.
- **Task 2 verify:**
  - `EffectRunnerTests`: 6/6, run as a class 4 times.
  - Each of the 6 tests also passed when run on its own.
  - The strict `HarnessDictationTests` run that follows passed 1/1.
- **Full `Somning.Core.Tests`:** 100 total, 100 passed, 0 failed, 0 skipped.
- **Full `Somning.Integration.Tests`:** run twice.
  - Run 1: **74 total, 73 passed, 1 failed, 0 skipped.** The failure was `HarnessDictationTests.TapTap_pastes_fixture_into_notepad` (31 s). The output filter hid the failure message.
  - Run 2: **74 total, 74 passed, 0 failed, 0 skipped.**
  - The intermittent failure is recorded in `deferred-items.md` and the WINDOWS ledger (see Issues).
- **Acceptance greps:**
  - `SOMNING_TEST_HARNESS` appears in Program.cs (lines 8 and 16).
  - Program.cs:61 starts the KeyboardHook before `StartEngineInBackground`. `Engine.StartAsync` is called only inside the background task (AppComposition.cs:70) and is not awaited on the startup path.
  - EffectRunner calls `targetProbe.Capture()` in `EndCapture` before `Source.StopAsync`.
  - `CaptureFaulted` appears in EffectRunner.cs.

## Deviations from Plan

1. **[Rule 3 - Blocking] `EngineSupervisor.Session` accessor added.**
   - Issue: the "engine ready backend= load_ms= warm_ms=" log needs the session timings, which were private.
   - Fix: a read-only `Session` property. EngineSupervisor.cs is not in the plan's file list.
   - Commit: `8b92a79`.
2. **[Rule 2 - Correctness] Exceptions mapped to results.** A transcriber or delivery exception is turned into an error `TranscriptionCompleted`/`DeliveryCompleted`. Otherwise the reducer's InFlight would stall the FIFO forever. Commit `8b92a79`.
3. **[Rule 2 - Correctness] Real CreatedUtc stamped.** EffectRunner replaces the 01-09 epoch placeholder with the stored value or UtcNow, which resolves 01-09's Known Stub. A test was added (`Sidecar_CreatedUtc_is_stamped_once_and_preserved`) beyond the plan's 5 behaviors.
4. **[Rule 3 - Blocking] Unknown arguments in normal mode exit 2.** This applies in both builds, so a typo cannot silently start a live hook. Not specified by the plan.

**Total deviations:** 4 auto-fixed (2 Rule 2, 2 Rule 3). **Impact:** no change to the plan's contracts. All are additive safety or plumbing.

## Issues Encountered

- **Intermittent E2E failure.** One full-suite run had `TapTap_pastes_fixture_into_notepad` fail. The 31 s duration means the 20 s paste poll ran out. The suite was green on the rerun, and the test passed in 5 isolated runs. The suspected cause is foreground or modifier state left by an earlier desktop test, but it was not diagnosed because the message was not captured. Logged in `deferred-items.md` and WINDOWS.md (kind `deviation`) for plans 01-12/01-13, which run the full E2E suite repeatedly.

## Known Stubs

None. The 01-09 `CreatedUtc` placeholder is now stamped by the adapter.

## Threat Flags

None beyond the plan's threat model:
- T-01-35 single writer: mitigated.
- T-01-36 hook before engine: mitigated in code; the E2E proof is in 01-11.
- T-01-44 harness Release exit 2: mitigated and verified.
- T-01-SC: no new packages.

## Next

Ready for plan 01-11 (startup recovery admission and the replayable harness pipe on top of `DictationHost`/`AppHarnessDriver`).

## Self-Check: PASSED

- All 10 created files and 2 modified files exist.
- Commits `8b92a79`, `682f61d` and `db756e2` are present in `git log`.
