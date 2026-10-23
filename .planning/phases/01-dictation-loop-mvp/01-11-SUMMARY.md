---
phase: 01-dictation-loop-mvp
plan: "11"
subsystem: startup-recovery-and-test-harness
tags: [aud-04, stt-04, app-06, d-11, d-20, recovery, retention, harness-pipe, journal, e2e]
requires:
  - phase: 01-06
    provides: SidecarStore, WavHeaderRepair, RecoveryPlanner, RetentionPolicy
  - phase: 01-09
    provides: RecoveryRequested, RecoveryAdmissionCompleted, DictationState.RecoveryAdmissionPending, DictationMetrics
  - phase: 01-10
    provides: DictationHost (Post/Notify/Notified), EffectRunner, AppComposition, AppHarnessDriver
provides:
  - Somning.App.Recovery.RecoveryService.RunAsync(CancellationToken) and RecoverySnapshot.Take (pre-hook ID snapshot)
  - DictationHost initialState ctor parameter (App starts with RecoveryAdmissionPending=true)
  - AppComposition.StartRecoveryInBackground (not awaited, runs after hook install)
  - Somning.App.TestHarness.HarnessJournal (seq-numbered, 512 retained), HarnessPipeServer (\\.\pipe\somning-test), HarnessGate, HarnessFixture (all Debug/SOMNING_TEST_HARNESS only)
  - "Pipe commands: status, wait-event (after_seq replay), set-fixture, arm-delivery-barrier, release-delivery-barrier, release-recovery-gate, shutdown; unknown -> unknown_command"
  - "Journal kinds: hook_installed, engine_ready, target_captured, transcribe_started, outcome, recovery_complete"
  - "AppHarnessDriver: ConnectAsync, SendAsync/SendRawAsync, StatusAsync, LastSeqAsync, WaitEventAsync, TryWaitEventAsync, WaitOutcomeAsync, EventsAsync, SetFixtureAsync, Arm/ReleaseDeliveryBarrierAsync, ReleaseRecoveryGateAsync, ShutdownAsync; StartAsync(fixture, extraEnv, waitForEngine, dataDir)"
  - "Harness env: SOMNING_TEST_HOLD_RECOVERY=1 (holds recovery processing until release-recovery-gate); SOMNING_ENGINE_READY_DELAY_MS forwarded to the first engine worker"
affects: [01-12, 01-13]
tech-stack:
  added: []
  patterns: [pre-hook-snapshot, admission-gated-dispatch, seq-cursor-replay-journal, one-shot-harness-gate, current-user-pipe-acl-plus-sid-check]
key-files:
  created:
    - src/Somning.App/Recovery/RecoveryService.cs
    - src/Somning.App/TestHarness/HarnessJournal.cs
    - src/Somning.App/TestHarness/HarnessPipeServer.cs
    - src/Somning.App/TestHarness/HarnessGate.cs
    - tests/Somning.Integration.Tests/Harness/HarnessPipeTests.cs
    - tests/Somning.Integration.Tests/Recovery/RecoveryServiceTests.cs
    - tests/Somning.Integration.Tests/Startup/StartupBeforeReadyTests.cs
  modified:
    - src/Somning.App/Dictation/DictationHost.cs
    - src/Somning.App/Dictation/EffectRunner.cs
    - src/Somning.App/Composition/AppComposition.cs
    - src/Somning.App/Program.cs
    - src/Somning.App/TestHarness/HarnessOptions.cs
    - tests/Somning.Integration.Tests/Support/AppHarnessDriver.cs
    - tests/Somning.Integration.Tests/Support/ForegroundHelper.cs
key-decisions:
  - "The harness pipe uses an explicit protected ACL that grants only the current user SID, checks the client token's user SID on every connection, and the client connects with CurrentUserOnly. Server-side PipeOptions.CurrentUserOnly throws when combined with a PipeSecurity in .NET 10, and its built-in ACL grants the token OWNER, which is the Administrators group for an elevated admin."
  - "AppComposition takes the recording snapshot (directory enumeration only) before the hook is installed. RecoveryService works only on snapshot IDs, so a live capture can never be adopted, repaired or recovered."
  - "RecoveryService always posts RecoveryAdmissionCompleted, in a finally block, so dispatch can never stay stuck. It waits for every requested ID's outcome notification before retention and before recovery_complete."
  - "An orphan WAV gets its CreatedUtc from its yyyyMMdd-HHmmss-fff ID (local time), falling back to the file creation time, so oldest-first ordering holds for sidecar-less recordings."
  - "Retention reuses RetentionPolicy.SelectForDeletion(keep 20) after the recovery outcomes, and also skips every ID in the reducer's Records, Capture or NextStart."
  - "The status reply adds pid and delivery_barrier_armed. The driver checks pid so it never talks to a stale App that holds the pipe name."
  - "A harness App forwards SOMNING_ENGINE_READY_DELAY_MS to the first worker through SupervisorOptions.FirstWorkerEnvironment, because the supervisor strips fault-injection variables from the inherited environment."
  - "The delivery barrier (HarnessGate) is consumed by the first Deliver after arming. Release also disarms it, and a client disconnect or server shutdown releases it."
requirements-completed: [AUD-04, STT-04, APP-06]
duration: "85 min"
completed: 2026-10-23
status: complete
estimate:
  tokens: 95000
  tasks: 3
actuals:
  tokens: 19900
  tasks: 3
  commits: 4
plan_head_before: a0031a3649b548ddc77843241df6ddef5d4ec92c
plan_head_after: 71d804f546a909a725024e52b4f2494ebebb5cd6
coverage:
  - deliverable: "Replayable seq-numbered harness journal and current-user pipe (status, wait-event after_seq, unknown_command, delivery barrier)"
    human_judgment: false
    verification:
      - kind: e2e-test
        ref: "tests/Somning.Integration.Tests/Harness/HarnessPipeTests.cs (5 tests)"
        status: pass
      - kind: e2e-test
        ref: "tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs#TapTap_pastes_fixture_into_notepad (pipe-based readiness)"
        status: pass
  - deliverable: "Startup recovery through the dictation FIFO: orphan adoption, header repair, saved-text reuse, clipboard-only, held-clipboard retry, retention to 20, early live captures admitted once after recovery"
    human_judgment: false
    verification:
      - kind: e2e-test
        ref: "tests/Somning.Integration.Tests/Recovery/RecoveryServiceTests.cs (3 tests)"
        status: pass
  - deliverable: "Hotkey pressed before engine ready is recorded, suppressed and pasted after ready (STT-04 in the running App)"
    human_judgment: false
    verification:
      - kind: e2e-test
        ref: "tests/Somning.Integration.Tests/Startup/StartupBeforeReadyTests.cs#Hotkey_before_ready_is_recorded_and_delivered"
        status: pass
  - deliverable: "Release Somning.App.dll contains no harness types"
    human_judgment: false
    verification:
      - kind: command
        ref: "dotnet build src/Somning.App -c Release && grep -c -a -E 'HarnessPipeServer|HarnessJournal' Somning.App.dll -> 0"
        status: pass
---

# Phase 1 Plan 11: Startup Recovery and Replayable Test Harness Summary

**Startup recovery now runs right after the hook is installed and never waits for the engine. It works only on a pre-hook snapshot and feeds oldest-first RecoveryRequested events into the same dictation FIFO, so live dictations queue behind it. Recovered text goes to the clipboard only, and retention keeps the newest 20 recordings. A Debug-only, current-user-only named pipe gives tests a seq-numbered event journal they can replay from any cursor. That journal is how the delayed-engine E2E proves that a hotkey pressed during model load is recorded and pasted after the engine is ready.**

## Accomplishments

- **HarnessJournal (H10, T-01-38):** entries are `{seq, kind, id, metrics}` with seq starting at 1 from process start, and the last 512 are retained.
  - It attaches to `DictationHost.Notified` in `EnableHarness`, which runs before `StartLoop`.
  - `WaitAsync(kind, afterSeq, id)` returns an already-retained match at once, or waits for the next one or the timeout.
- **HarnessPipeServer (D-20, T-01-18):** JSON lines over `\\.\pipe\somning-test`, one client at a time, listening again after each disconnect.
  - Access: explicit user-SID ACL, a per-connection client SID check, and a CurrentUserOnly client.
  - Commands: `status` (engine, phase Idle/Recording/Busy, hook, last_seq, recovery_complete, pid), `wait-event` with `after_seq` replay, `set-fixture` (an existing `.wav` only), the delivery barrier, `release-recovery-gate`, `shutdown`.
  - Bad JSON returns `bad_request`; an unknown command returns `unknown_command`.
  - All harness files start with `#if SOMNING_TEST_HARNESS`.
- **Delivery barrier:** `EffectRunner.BeforeDeliver` exists only under `#if SOMNING_TEST_HARNESS`.
  - It is awaited asynchronously before `TargetPaster.DeliverAsync`, so the owner loop and the pipe stay responsive.
  - A client disconnect or server dispose releases it.
- **Startup admission (D-05/D-11, H9):**
  - `AppComposition` takes `RecoverySnapshot` (directory enumeration only) and creates `DictationHost` with `RecoveryAdmissionPending=true`.
  - `Program` order: snapshot plus pipe, then loop, then hook, then `StartRecoveryInBackground`, then `StartEngineInBackground`. Neither background task is awaited.
- **RecoveryService (AUD-04, H6, M7):**
  - Normalizes snapshot IDs: an orphan WAV gets a captured sidecar; pending audio that is missing becomes `failed` with `missing_wav`.
  - Plans with `RecoveryPlanner`, logs exhausted IDs and keeps their files, and repairs only WAVs that still need inference.
  - Posts oldest-first `RecoveryRequested`, then always `RecoveryAdmissionCompleted`.
  - Waits for every requested ID's outcome, applies retention (newest 20, pending and live protected), then notifies `recovery_complete`.
  - It never calls DeliverAsync. Recovered text goes through the reducer's CopyOnly.
- **AppHarnessDriver:** all readiness waits now go through the pipe. Startup connects with retry for up to 30 s and verifies the pid. Caller-owned `dataDir` is supported (kept on dispose).

## Task Commits

1. **Task 1 (tracer):** `af0b9ad` `feat(01-11): tracer - replayable harness journal and current-user pipe`. Tracer gate (auto mode): the full `<verify>` was re-run (build 0/0, HarnessPipeTests 5/5, HarnessDictationTests 1/1) before expanding.
2. **Task 2:** `df536e6` `feat(01-11): startup recovery through the dictation FIFO with retention`
3. **Task 3:** `7f08266` `test(01-11): hotkey before engine ready is recorded and delivered (STT-04 E2E)`
4. **Follow-up:** `71d804f` `fix(01-11): retry foreground acquisition and add recovery E2E evidence`

## Test Evidence

- **Builds:**
  - `dotnet build Somning.sln -c Debug`: 0 warnings, 0 errors (TreatWarningsAsErrors).
  - `dotnet build src/Somning.App -c Release`: 0/0. `grep -c -a -E "HarnessPipeServer|HarnessJournal"` on the Release DLL printed `0` (the Debug DLL prints 2).
- **Task 1 verify (strict):** HarnessPipeTests 5/5 and HarnessDictationTests 1/1, run 3 times.
- **Task 2 verify (strict):** RecoveryServiceTests 3/3. It passed in 2 class runs and in 2 runs together with WasapiSmokeTests under seed 610002936.
- **Task 3 verify (strict):**
  - The first version failed: the engine was Ready after about 10 s because the delay variable never reached the worker. Fixed as deviation 1.
  - It then failed 1 run in 4 on the plan's `queue_ms > 0` (deviation 2).
  - Final version: 5/5 (3 consecutive runs plus 2 verify-chain runs).
- **Somning.Core.Tests:** total 100, passed 100, failed 0, skipped 0.
- **Somning.Integration.Tests full suite (strict), 4 runs:**
  - Runs 1 and 2 (`dotnet test`) and run 3 (native runner, seed 610002936): **83 total, 82 passed, 1 failed, 0 skipped.** The failure was `TargetPasterTests.Focus_change_inside_same_top_level_window_defers`: "could not acquire two-edit test foreground". It passed 3/3 alone. Fixed as deviation 3.
  - Run 4 (seed 610002936, after the fix): **83 total, 82 passed, 1 failed.** The failure was `RecoveryServiceTests.Early_captures_during_delayed_recovery_are_admitted_once`: "First live text: ''". Both live outcomes were `pasted`, but Notepad held only the second paste. See Issues.
  - Run 5 (seed 610002936, with failure diagnostics added): **83 total, 83 passed, 0 failed, 0 skipped.**
- **Deferred TapTap intermittent (01-10):** `HarnessDictationTests.TapTap_pastes_fixture_into_notepad` did not fail in any run here (5 full-suite runs and 4 class runs).
- **Acceptance greps:**
  - `CurrentUserOnly` matches HarnessPipeServer.cs, but in the doc comment explaining why the server cannot use it; see deviation 4.
  - `after_seq` matches. The first line of HarnessPipeServer.cs and HarnessJournal.cs is `#if SOMNING_TEST_HARNESS`.
  - `RecoveryRequested` matches RecoveryService.cs, and the `DeliverAsync` count there is `0`.

## Deviations from Plan

1. **[Rule 3 - Blocking] The engine-ready delay is forwarded to the worker explicitly.**
   - Found during: Task 3.
   - Issue: the plan says SOMNING_ENGINE_READY_DELAY_MS is "inherited by the Debug engine", but `EngineSupervisor` (01-07) strips it from the inherited environment. It only accepts the variable through `SupervisorOptions.FirstWorkerEnvironment`.
   - Fix: harness builds pass it through in `AppComposition.SupervisorOptionsForHost()`.
   - Commit: `7f08266`.
2. **[Rule 1 - Bug in test contract] `queue_ms > 0` replaced by a deterministic wait proof.**
   - Found during: Task 3.
   - Issue: `queue_ms` is the supervisor-internal time between `TranscribeAsync` and the request write. The reducer dispatches Transcribe only after EngineReady, so `queue_ms` is about 0 for a dictation that waited about 10 s in the App FIFO, and the assertion was a coin flip.
   - Fix: the test records the last moment after the stop chord when status still said `Starting`, and asserts both that this was over 1 s and that `stop_to_paste_ms` covers it. It also keeps `outcome.seq > engine_ready.seq`.
   - The metric-definition gap is logged in `deferred-items.md` and WINDOWS.md.
   - Commit: `7f08266`.
3. **[Rule 3 - Blocking] `ForegroundHelper.EnsureForeground` retries for up to 1 s (test support; the file is outside the plan's list).**
   - Found during: plan verification.
   - Issue: the new test classes changed xUnit's collection shuffle. In the new order, a pre-existing 01-04 test failed every time, right after the previous test closed two Notepads.
   - Fix: the acquisition sequence is retried for up to 1 s.
   - Verified by a green full suite with the same seed.
   - Commit: `71d804f`.
4. **[Rule 3 - Blocking] Pipe ACL mechanism differs from the plan's literal wording.**
   - Found during: Task 1.
   - Issue: `NamedPipeServerStreamAcl.Create` throws `'pipeSecurity' must be null when 'options' contains 'PipeOptions.CurrentUserOnly'`. CurrentUserOnly alone would grant the Administrators group (token owner) on this elevated account.
   - Fix: an explicit protected user-SID ACL, a per-connection client SID check, and a CurrentUserOnly client.
   - The intent of V4/T-01-18 (only the current user) is met more strictly. The `CurrentUserOnly` grep matches the explanatory comment.
   - Commit: `af0b9ad`.
5. **[Rule 2 - Additive] Small additions to the pipe protocol and harness.**
   - Additions: `pid` and `delivery_barrier_armed` in status, a `bad_request`/`invalid_path` error, `release-recovery-gate` plus `SOMNING_TEST_HOLD_RECOVERY` (the plan's "test-owned asynchronous gate"), a new `HarnessGate.cs` file, and driver `TryWaitEventAsync`/`EventsAsync`/`ConnectAsync`.
   - HarnessPipeTests has 5 tests instead of 3: two delivery-barrier tests (hold-until-release, release-on-disconnect), which the plan's action asked for.
   - Commits: `af0b9ad`, `df536e6`.

**Total deviations:** 5 auto-fixed (1 Rule 1, 1 Rule 2, 3 Rule 3). **Impact:** every binding name and contract in `<interfaces>` is kept. Deviation 2 changes one assertion, not the behavior under test.

## Issues Encountered

- **Intermittent lost first paste in `Early_captures_during_delayed_recovery_are_admitted_once`.** It happened once in 3 full-suite runs that included the test, and never in 6+ isolated or class runs.
  - What the failure showed: both live outcomes were `pasted` (the PasteGate checks passed), but Notepad contained only the second text.
  - It is not diagnosed. Suspects: Notepad's handling of the first injected Ctrl+V racing the next clipboard write, or desktop state left by earlier tests. It is the same family as the 01-10 TapTap intermittent.
  - The assertion now prints the Notepad text, clipboard, each outcome's metrics and the App log.
  - Recorded in `deferred-items.md` and WINDOWS.md (kind `deviation`) for 01-12/01-13, which run the full E2E suite repeatedly.

## Known Stubs

None.

## Threat Flags

None beyond the plan's threat model:
- T-01-18: mitigated (Debug-only, user-SID ACL and client SID check, Release DLL grep = 0).
- T-01-20: accepted.
- T-01-37: mitigated (recovery IDs ordered before live IDs by RecoveryAdmissionCompleted, proven by the early-captures test).
- T-01-38: mitigated (512 entries).
- T-01-SC: no new packages.

## Next

Ready for plan 01-12. Its E2E tests should use `WaitEventAsync`/`WaitOutcomeAsync` with seq cursors and the delivery barrier; no log polling is needed.

## Self-Check: PASSED

- All 7 created files and 7 modified files exist.
- Commits `af0b9ad`, `df536e6`, `7f08266` and `71d804f` are present in `git log`. The count from the ledger base `a0031a3` is 4.
