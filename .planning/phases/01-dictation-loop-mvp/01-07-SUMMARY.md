---
phase: 01-dictation-loop-mvp
plan: "07"
subsystem: engine-supervision
tags: [process-isolation, vulkan, cpu-fallback, restart-policy, job-object]
requires:
  - phase: 01-02
    provides: Engine worker protocol and ITranscriber contract
  - phase: 01-05
    provides: CPU worker and fault-injection harness
provides:
  - Isolated EngineProcess with stdout/stderr readers and Job Object KILL_ON_JOB_CLOSE
  - CPU fallback and one retry for worker failure and GPU/backend errors
  - Pure immutable RestartPolicy using injected IClock
  - Queued requests during worker readiness/recovery and bounded shutdown
affects: [01-08, 01-09, 01-10, 01-11, 01-12, 01-13]
tech-stack:
  added: []
  patterns: [one-engine-process-per-backend, kill-on-close-job-object, immutable-restart-budget, request-id-correlated-jsonl]
key-files:
  created:
    - src/Somning.App/Engine/EngineProcess.cs
    - src/Somning.App/Engine/SupervisorOptions.cs
    - src/Somning.App/Properties/AssemblyInfo.cs
    - src/Somning.Core/Engine/IClock.cs
    - src/Somning.Core/Engine/RestartPolicy.cs
    - src/Somning.Platform.Windows/Processes/JobObject.cs
    - tests/Somning.Core.Tests/Engine/RestartPolicyTests.cs
    - tests/Somning.Integration.Tests/Supervisor/EngineSupervisorTests.cs
  modified:
    - src/Somning.App/Engine/EngineSupervisor.cs
key-decisions:
  - "A worker is assigned to a KILL_ON_JOB_CLOSE Job Object immediately after process start; the job handle is owned by EngineProcess."
  - "Any worker failure enters the immutable three-restart/ten-minute policy and restarts in a fresh CPU process; each pending transcription is retried at most once."
  - "Restart recovery belongs to the supervisor lifetime, so caller cancellation cannot strand the supervisor without a worker."
  - "QueueMs ends after a successful JSONL write; responses are correlated by worker instance and request id."
  - "Fault-injection environment variables are cleared on replacement workers and applied only to the first worker in tests."
requirements-completed: [STT-02, STT-04]
metrics:
  duration: "not recorded"
  tasks: 2
  files: 9
  commits: 1
  completed: 2026-10-23
  status: complete
---

# Phase 1 Plan 7: Resilient Engine Supervision Summary

**Engine failures now recover on a fresh CPU worker without losing an in-flight dictation or orphaning the worker process.**

## Accomplishments

- Added `EngineProcess` to own one worker, its JSONL/stdout and diagnostics/stderr readers, process exit observation, and its kill-on-close Job Object.
- Hardened `EngineSupervisor` with CPU fallback, one retry per transcription, request identity checks, readiness/recovery queuing, automatic idle-exit recovery, bounded shutdown, and cancellation-safe recovery.
- Added immutable `RestartPolicy` decisions with a three-restart cap over ten minutes and 0/2/5-second backoff, driven by injected `IClock`/`SystemClock`.
- Added test-only supervisor options for readiness timing, model path, first-worker fault environment, and zero backoff. Replacement workers do not inherit `SOMNING_ENGINE_FAULT`.

## Task Commit

- **Tasks 1–2: Isolated resilient worker, restart policy, queued requests, bounded shutdown** — `f8192f5` (`feat(01-07): resilient isolated engine supervision`).

## Test Evidence

- `dotnet build tests/Somning.Integration.Tests/Somning.Integration.Tests.csproj --no-restore` — passed with no warnings or errors.
- `RestartPolicyTests` — 3 passed, 0 failed, 0 skipped.
- Final strict `EngineSupervisorTests` run with `FullyQualifiedName~EngineSupervisorTests` — 9 passed, 0 failed, 0 skipped.
- The nine integration cases cover GPU worker kill during inference, injected crash and GPU error, queueing before readiness and during background recovery, restart cap, bounded disposal during inference and backoff, and cancellation during recovery.
- Final process check found no active `Somning.Engine.exe` worker. The `RestartPolicy` purity scan found no wall-clock API references, and `JobObject.cs` contains `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
- Final code review approved the complete delta.

## Deviations

- Added three lifecycle regression cases beyond the six planned supervisor tests to close review findings around background recovery queueing, disposal during restart backoff, and caller cancellation during CPU restart.
- The implementation and test changes were committed together because both tasks modify the supervisor's same lifecycle state machine.

## Next Step

Plan 01-08 is ready. The complete application-level hotkey-before-ready path remains covered by later Phase 1 orchestration plans.

## Self-Check

- [x] Both plan tasks are implemented and committed.
- [x] Strict unit and integration tests passed with no skips.
- [x] No worker process remains; final code review approved.
- [x] The plan's process isolation, restart, queue, and shutdown contracts are covered by implementation and tests.

**Self-check: PASSED**
