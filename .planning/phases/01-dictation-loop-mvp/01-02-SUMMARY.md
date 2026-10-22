---
phase: 01-dictation-loop-mvp
plan: "02"
subsystem: app
tags: [windows, clipboard, sta, jsonl, serilog, dotnet10]
requires:
  - phase: 01-01
    provides: "Real local Whisper worker, shared protocol, Core utilities and verified model/runtime gate"
provides:
  - "App command transcribes a WAV through the worker and leaves Unicode text on the Windows clipboard"
  - "Core-owned ITranscriber and engine status/session/result contracts"
  - "Explicit clipboard failure exit code and metadata-only rolling logs"
affects: [01-03, 01-04, 01-06, 01-07, 01-10]
tech-stack:
  added: [Serilog 4.4.0, Serilog.Sinks.File 7.0.0]
  patterns: [dedicated STA clipboard owner, correlated worker responses, domain mapping boundary]
key-files:
  created: [src/Somning.App/Program.cs, src/Somning.App/Engine/EngineSupervisor.cs, src/Somning.App/Engine/ProtocolMapping.cs, src/Somning.Core/Ports/ITranscriber.cs, src/Somning.Platform.Windows/Clipboard/ClipboardWriter.cs, src/Somning.Platform.Windows/Clipboard/MessageOnlyWindow.cs]
  modified: [Somning.sln, tests/Somning.Integration.Tests/Somning.Integration.Tests.csproj]
key-decisions:
  - "Serialize window operation acceptance with disposal, fail pending work at loop termination and cancel unstarted timed-out operations."
  - "Keep load and warm timing on the Core result using the worker session serving that transcription."
requirements-completed: [STT-01, OUT-01]
actuals:
  tokens: 10825
  tasks: 2
  commits: 5
plan_head_before: 45c59e5f16502752eb61d5bfdf42dfed9707cea6
plan_head_after: 415bd861a26b61f6021d2100915b63109ba25821
duration: 11min
completed: 2026-10-22
status: complete
coverage:
  - id: D1
    description: "WAV to App to local engine to Unicode clipboard"
    requirement: OUT-01
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Skeleton/SkeletonRoundTripTests.cs#Transcribe_file_puts_text_on_clipboard"
        status: pass
    human_judgment: false
  - id: D2
    description: "Held clipboard returns exit 11 and clipboard_failed metadata"
    requirement: OUT-01
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Skeleton/SkeletonRoundTripTests.cs#Clipboard_held_exits_11"
        status: pass
    human_judgment: false
---

# Phase 1 Plan 2: App, supervisor and clipboard Summary

**A headless App command now transcribes real pt-BR audio through the separate local worker and writes Unicode clipboard text; a held clipboard returns exit 11 with an explicit logged outcome.**

## Accomplishments

- Seven-project solution with pure Core domain records and ITranscriber; Core and Core.Tests reference neither App nor Platform.
- Engine discovery supports explicit path, environment, packaged layout and development fallback. Supervisor correlates JSONL responses, tracks ready/session metadata, cancels requests and disposes its worker tree.
- Clipboard ownership uses a message-only HWND on a dedicated STA with a message loop. Movable UTF-16 memory transfers to Windows only after SetClipboardData succeeds. Ten bounded retries and sequence verification govern success.
- Transcription command copies audio into the recordings directory, normalizes output, reports 0 only after successful clipboard assignment, and records outcome, length, timings, backend and model without transcript text.
- Rolling logs use invariant formatting, daily files and seven-file retention.
- Window shutdown ordering, cancelled queued work, malformed worker result fields and startup/EOF transitions are guarded.

## Task Commits

1. Task 1 tracer: `dcfd129` — App, domain contracts, supervisor and clipboard round trip.
2. Task 2 contracts: `753cc1a` — Core purity, error mapping, held clipboard, Unicode and lifecycle tests.
3. Task 2 review corrections: `415bd86` — synchronized window shutdown and worker readiness.

The persisted ledger measured five commits before this summary: three implementation commits above plus the parent's two intervening documentation/state commits. `actuals.tokens` uses the required edit-size scale, 43,300 realized textual diff characters / 4; it is not measured model/context token consumption. Owned delivery files: 21.

## Executed checks

- Seven-project Debug solution build: zero warnings/errors.
- All Core tests: 19 passed, no skips. CoreBoundaryTests alone: one passed.
- ClipboardWriterTests: four passed, no skips (7.42 s): Unicode/sequence, ten-attempt held failure, 30 iterations of eight concurrent invocations racing disposal, and timed-out queued work never executing later.
- SkeletonRoundTripTests: two passed, no skips (8.42 s): real WAV/App/worker/clipboard round trip and held-clipboard exit 11 with clipboard_failed in logs.
- ProtocolMappingTests: four passed, no skips: invalid_path/bad_request are non-retryable, gpu_error is retryable, and load/warm session timing survives mapping.
- First strict tracer passed independently before expansion (5.83 s).
- C# review initially identified lifecycle and malformed-response/startup races; final inspection confirmed corrections. Generic initial and final delta reviews approved with no remaining actionable findings. Build resolver fixed a local switch-variable collision and xUnit cancellation-token analyzer requirements.
- Working MTP filters remain `--filter-class` and `--filter-method`.

## Test-first assessment

The exact planned Task 2 contract tests were written before Task 2 production changes and all passed initially: Task 1 had already implemented these contracts. That result was investigated and retained without manufacturing a failing RED report. Subsequent production changes address distinct concrete review findings, with added disposal and cancelled-queue regression tests. No intentional RED evidence is claimed for already-implemented behavior.

## Deviations from Plan

1. **[Rule 1 - Lifecycle]** The original Invoke/dispose sequence could close the HWND between accepting and posting work, leaving a caller waiting. A shared lifecycle gate orders acceptance and close, loop termination completes pending calls, waits are bounded and unstarted timed-out callbacks are cancelled.
2. **[Rule 1 - Input/lifecycle]** A malformed result with null id could end the stdout reader; readiness could overwrite a serialized EOF failure. Field guards and an output-ended lifecycle check prevent both.
3. **[Rule 3 - Build]** Resolved CS0136 by distinct result/error completion locals and honored xUnit1051 using the test context cancellation token in added concurrency tests.
4. **[Rule 3 - File uniqueness]** Copied recordings add a GUID suffix to the timestamp to avoid overwriting another command's recording.
5. Clipboard interop uses explicit raw Win32 handle declarations so clipboard-owned HGLOBAL and module handles cannot be accidentally freed by generated SafeHandle wrappers. The pre-generated CsWin32 surface remains available to later hook/paste work.

## Readiness

No deliverable stub or skipped check remains for this plan. Restart/fallback policy, recording retention, full metrics and foreground-aware paste stay assigned to their later plans. The clipboard retains the most recent transcript as required by D-14.

The parent orchestrator owns STATE.md, ROADMAP.md, requirement bookkeeping and the generated state.json.

## Self-Check: PASSED

All delivered files exist and all three task commits are ancestors of HEAD. The final specified tests and added lifecycle regressions passed without skips.

