---
phase: 01-dictation-loop-mvp
plan: "06"
subsystem: audio
tags: [NAudio, WASAPI, PCM16, RIFF, recovery, retention]
requires:
  - phase: 01-02
    provides: Engine worker protocol and transcriber implementation
  - phase: 01-05
    provides: CPU engine and integration-test support
provides:
  - IAudioSource and IPcmSink audio contracts
  - WAV harness and on-demand WASAPI capture converted to 16 kHz mono PCM16
  - Durable spool WAVs, atomic sidecars, RIFF repair, recovery planning, and bounded retention
affects: [01-09, 01-10, 01-11, 01-12, 01-13]
actuals:
  tokens: 19404
  tasks: 3
  commits: 5
  plan_head_before: f445971313ce91f118e6e4f2799b3ba862f40c5f
  plan_head_after: 19b13024fea30880352bfadee9817d04c5910085
tech-stack:
  added: []
  patterns: ["Span-based 16 kHz mono PCM sink", "on-demand WASAPI capture", "atomic sidecar replacement", "RIFF chunk-aware crash repair"]
key-files:
  created:
    - src/Somning.Core/Ports/IAudioSource.cs
    - src/Somning.Core/Ports/IPcmSink.cs
    - src/Somning.Core/Recording/RecordingSidecar.cs
    - src/Somning.Core/Recording/RecordingStage.cs
    - src/Somning.Core/Recording/RecoveryPlanner.cs
    - src/Somning.Core/Recording/RetentionPolicy.cs
    - src/Somning.Platform.Windows/Audio/CaptureConverter.cs
    - src/Somning.Platform.Windows/Audio/FileAudioSource.cs
    - src/Somning.Platform.Windows/Audio/FirstDataGuard.cs
    - src/Somning.Platform.Windows/Audio/WasapiAudioSource.cs
    - src/Somning.Platform.Windows/Recording/SidecarStore.cs
    - src/Somning.Platform.Windows/Recording/SpoolWriter.cs
    - src/Somning.Platform.Windows/Recording/WavHeaderRepair.cs
    - tests/Somning.Core.Tests/Recording/RecoveryPlannerTests.cs
    - tests/Somning.Core.Tests/Recording/RetentionPolicyTests.cs
    - tests/Somning.Integration.Tests/Audio/SpoolDurabilityTests.cs
  modified:
    - tests/Somning.Integration.Tests/NativeMethods.json
    - tests/Somning.Integration.Tests/Support/ForegroundHelper.cs
    - tests/Somning.Integration.Tests/Support/HotkeyInjector.cs
    - tests/Somning.Integration.Tests/Support/ObserverHook.cs
key-decisions:
  - "Capture is opened only for an active recording; the device mix format is averaged to mono and resampled with WDL to 16 kHz PCM16."
  - "Spool writes flush every 32,000 bytes; recovery uses saved text for transcribed recordings and never auto-pastes."
  - "Retention ranks all recordings by creation time, keeps the newest 20, and only selects delivered or permanently failed older recordings for deletion."
patterns-established:
  - "Audio-source callbacks write PCM spans to IPcmSink and surface one structured fault per capture."
  - "Sidecar updates use unique temporary files and an atomic final move."
requirements-completed: [AUD-01, AUD-04]
coverage:
  - id: D1
    description: "Harness and microphone audio produce 16 kHz mono PCM16, with bounded startup and surfaced capture faults."
    requirement: AUD-01
    verification:
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/SpoolPipelineTests.cs
        status: pass
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/CaptureConverterTests.cs
        status: pass
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/AudioFaultTests.cs
        status: pass
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/WasapiSmokeTests.cs
        status: pass
    human_judgment: false
  - id: D2
    description: "Spool files and sidecars remain usable after interruption, and valid RIFF metadata is preserved during repair."
    requirement: AUD-04
    verification:
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/SpoolDurabilityTests.cs
        status: pass
      - kind: integration
        ref: tests/Somning.Integration.Tests/Audio/SidecarStoreTests.cs
        status: pass
    human_judgment: false
  - id: D3
    description: "Recovery retries only captured/transcribed recordings; retention never deletes undelivered speech."
    requirement: AUD-04
    verification:
      - kind: unit
        ref: tests/Somning.Core.Tests/Recording/RecoveryPlannerTests.cs
        status: pass
      - kind: unit
        ref: tests/Somning.Core.Tests/Recording/RetentionPolicyTests.cs
        status: pass
    human_judgment: false
metrics:
  duration: "about 25 minutes; exact start time was not recorded"
  completed: 2026-10-23
  status: complete
---

# Phase 1 Plan 6: Audio Capture and Durable Recording Summary

**On-demand WASAPI and WAV harness audio now feed the same durable 16 kHz mono PCM spool, with crash recovery and safe retention.**

## Performance

- **Duration:** About 25 minutes; the exact start time was not recorded.
- **Started:** Not recorded.
- **Completed:** 2026-10-23
- **Tasks:** 3
- **Files created or modified:** 26 across the task commits.

## Accomplishments

- Defined the `IAudioSource` and `IPcmSink` contracts and implemented a finite WAV-backed source plus on-demand WASAPI capture. Native channels are averaged to mono, resampled with `WdlResamplingSampleProvider`, and emitted as 16 kHz PCM16.
- Added first-buffer timeout and at-most-once capture faults, while preserving silent device packets as zero samples.
- Added a `SpoolWriter` that owns and releases its writer and file stream and flushes at roughly one-second intervals, plus atomic sidecar storage and state serialization.
- Added RIFF-aware header repair, pure recovery and retention policies, and tests that protect undelivered recordings and preserve valid chunks and complete PCM frames.

## Task Commits

1. **Task 1: WAV harness source, spool and sidecar tracer** - `24400c3` (feat).
2. **Task 2: WASAPI capture and conversion** - `84a11bc` (test, RED), `245671e` (feat, GREEN).
3. **Task 3: Crash repair, recovery and retention** - `88790bd` (test, RED), `19b1302` (feat, GREEN).

**Plan metadata:** The SUMMARY commit is separate from the five task commits.

## Files Created or Modified

- `src/Somning.Core/Ports/` and `src/Somning.Core/Recording/` - Audio contracts, recording stages, sidecar model, recovery plan and retention selection.
- `src/Somning.Platform.Windows/Audio/` - WAV source, WASAPI source, audio converter and first-data guard.
- `src/Somning.Platform.Windows/Recording/` - Incremental spool writer, atomic sidecar store and RIFF header repair.
- `tests/Somning.Core.Tests/Recording/` and `tests/Somning.Integration.Tests/Audio/` - Recovery, retention, conversion, fault, spool, durability and microphone coverage.
- `tests/Somning.Integration.Tests/NativeMethods.json` and `Support/` - Test-only CsWin32 class aliasing to avoid duplicate generated P/Invoke types.

## Test Evidence

The final sequential Git Bash verification passed without failures or skips:

- `RecoveryPlannerTests`: 1 passed; `RetentionPolicyTests`: 4 passed.
- `CaptureConverterTests`: 4 passed; `AudioFaultTests`: 2 passed.
- `SpoolPipelineTests`: 2 passed; `SpoolDurabilityTests`: 6 passed.
- `WasapiSmokeTests`: 1 passed with a capture device available.
- The Core policy scan found zero file, directory or wall-clock access in `RecoveryPlanner` and `RetentionPolicy`.

Both C# and generic read-only reviews approved the final Task 2 and Task 3 changes. The reviewers' recorder-lifecycle, RIFF trailing-chunk and partial-frame findings were fixed before final verification.

## Decisions Made

- Capture remains on-demand and shared-mode; the first device buffer must arrive within three seconds.
- Recovery returns captured recordings without saved text and transcribed recordings with their saved text. It reports retry-exhausted ids without deleting them or triggering paste.
- Retention keeps the newest 20 overall and selects older rows for deletion only when their stage is eligible under the plan's delivered/permanently failed rules.
- Header repair parses RIFF chunks before and after `data`, preserves valid trailing chunks, and truncates data only to complete sample frames.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed duplicate generated P/Invoke types in the integration test project**
- **Found during:** Task 1 validation.
- **Issue:** CsWin32 generated the same `Windows.Win32.PInvoke` name as the library, producing CS0436 warnings treated as errors; xUnit also reported a missing cancellation token and the new test namespace needed the `Wer` import.
- **Fix:** Gave the test generator a distinct `IntegrationPInvoke` class and aliased it in the three support files; used `TestContext.Current.CancellationToken` and imported the Core text namespace.
- **Files modified:** `tests/Somning.Integration.Tests/NativeMethods.json`, `tests/Somning.Integration.Tests/Support/ForegroundHelper.cs`, `HotkeyInjector.cs`, `ObserverHook.cs`, and the spool pipeline test.
- **Verification:** The strict engine-backed integration test built cleanly and passed.
- **Committed in:** `24400c3`.

**2. [Rule 1 - Bug] Serialized concurrent atomic sidecar replacement**
- **Found during:** Task 1 sidecar concurrency test.
- **Issue:** Concurrent saves to the same sidecar could fail on Windows while replacing the destination file.
- **Fix:** Kept unique temporary files per save and serialized the final atomic move.
- **Files modified:** `src/Somning.Platform.Windows/Recording/SidecarStore.cs`.
- **Verification:** `SidecarStoreTests.Concurrent_saves_use_independent_atomic_temporary_files` passed.
- **Committed in:** `24400c3`.

**3. [Rule 1 - Bug] Closed WASAPI setup and stop/start races**
- **Found during:** Task 2 C# and generic review.
- **Issue:** Converter setup could leak a constructed recorder, and `StopAsync` or `DisposeAsync` racing with `StartAsync` could leave capture active.
- **Fix:** Dispose the recorder on setup failure and serialize publication/start against stop/dispose; discard it when stop wins during setup.
- **Files modified:** `src/Somning.Platform.Windows/Audio/WasapiAudioSource.cs`.
- **Verification:** Final converter, fault and microphone smoke tests passed; both reviewers approved the final delta.
- **Committed in:** `245671e`.

**4. [Rule 1 - Bug] Preserved trailing RIFF chunks and complete PCM frames during repair**
- **Found during:** Task 3 review.
- **Issue:** Treating every byte after `data` as audio could absorb valid trailing metadata; RIFF padding could also be mistaken for a partial PCM sample.
- **Fix:** Validate complete RIFF chunk sequences, repair stale RIFF lengths without changing valid metadata, and shift later chunks only when truncating an incomplete sample frame.
- **Files modified:** `src/Somning.Platform.Windows/Recording/WavHeaderRepair.cs`, `tests/Somning.Integration.Tests/Audio/SpoolDurabilityTests.cs`.
- **Verification:** Six strict durability tests passed, including LIST chunks before and after data, stale RIFF size and partial-frame padding.
- **Committed in:** `19b1302`.

---

**Total deviations:** 4 auto-fixed issues (3 Rule 1 bugs, 1 Rule 3 blocking validation issue).
**Impact on plan:** All fixes were required for Windows correctness or strict validation; the plan's audio and storage contracts remain intact.

## Issues Encountered

- The WDL resampler rings briefly at the edge of a constant test signal. The six-channel average test measures the stable middle second while retaining the plan's ±5% amplitude tolerance; frequency and duration checks remain unchanged.
- A first durability test sent 2.5 seconds in one large write, causing that write to flush its entire length. The fixture was corrected to model three source chunks; it now verifies that only the last partial second needs header repair.

## User Setup Required

None - no external service configuration is required.

## Next Phase Readiness

The audio source, durable spool, sidecar, recovery and retention contracts are ready for the orchestration and recovery wiring in plans 01-09 through 01-13. Startup recovery remains intentionally owned by plan 01-11.

## Self-Check: PASSED

- All three task outputs and all five task commits exist on the current branch.
- Final sequential verification passed: all 20 tests passed, none were skipped.
- No placeholder implementation or skipped verification remains in the files changed by this plan.

---
*Phase: 01-dictation-loop-mvp*
*Completed: 2026-10-23*
