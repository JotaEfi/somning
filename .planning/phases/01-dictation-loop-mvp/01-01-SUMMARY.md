---
phase: 01-dictation-loop-mvp
plan: "01"
subsystem: engine
tags: [dotnet10, whisper-net, windows10, vulkan, jsonl, xunit]
requires: []
provides:
  - Six-project pinned .NET solution and local pt-BR engine worker
  - Responsive cancellation, bounded JSON input, and retained-handle audio boundary
  - Hash-verified model acquisition and SDK-free CPU/Vulkan deployment evidence
affects: [01-02, 01-05, 01-07, 01-08]
tech-stack:
  added: [Whisper.net 1.9.1, NAudio.Core 3.3.0, NAudio.Wasapi 3.3.0, CsWin32 0.3.367, xunit.v3 4.0.2]
  patterns: [single-inference worker, independent control reader, source-generated JSON, retained validated file handles]
key-files:
  created: [Somning.sln, src/Somning.Engine/Program.cs, src/Somning.Engine/InferenceExecutor.cs, src/Somning.Engine/PathGuard.cs, src/Somning.Engine.Protocol/EngineMessages.cs, tools/gate.ps1, tools/fetch-models.ps1]
  modified: [.gitignore]
key-decisions:
  - "Canonicalize Windows DOS aliases before comparing allowed roots, requested paths and final opened handles."
  - "Accept JSON type discriminators regardless of property order; retain the 64 KiB line bound."
  - "Result segments remain optional for older callers; start/end are milliseconds and no_speech is the native segment probability."
requirements-completed: [STT-01, STT-02]
actuals:
  tokens: 18765
  tasks: 3
  commits: 6
plan_head_before: 830c0c7efc8c7d2ad3e953ef68707402e1ba2aa0
plan_head_after: 8cba95bcdfc361ce31137f2f45fda5513d5a392c
duration: 23min
completed: 2026-10-22
status: complete
coverage:
  - id: D1
    description: "Local pt-BR fixture transcription over real worker JSONL"
    requirement: STT-01
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Engine/EngineWorkerTests.cs#Transcribes_tracer_fixture"
        status: pass
    human_judgment: false
  - id: D2
    description: "Published Windows 10 worker uses both CPU and Vulkan without an SDK on PATH"
    requirement: STT-02
    verification:
      - kind: other
        ref: "powershell -NoProfile -ExecutionPolicy Bypass -File tools/gate.ps1"
        status: pass
    human_judgment: false
---

# Phase 1 Plan 1: Local Whisper worker and published-build gate Summary

**Real large-v3-turbo pt-BR transcription over UTF-8 JSONL, responsive control and validated audio handles, with both published Windows 10 backends passing at 0% fixture WER.**

## Performance

- Recorded scaffold start: 2026-10-22T18:18:39.487Z
- Duration: 23 minutes from the recorded scaffold start.
- Tasks complete: 3; owned files: 45 before this summary.
- Actual tokens use realized textual diff characters / 4 (75,059 / 4, rounded up); fixture binary contents are represented by Git's binary diff marker.
- Commit count is measured from the persisted plan ledger. It includes the orchestrator's intervening planning commit `7afe142`; the five implementation commits below belong to this executor.

## Accomplishments

- Six pinned projects build with zero warnings/errors on SDK 10.0.203; Core has no project references.
- Exactly one CPU/Vulkan runtime is selected before Whisper initialization. Ready follows a full one-second silence warm-up. pt-BR, six threads, no prior context, prompt and flash attention are configured.
- Control input stays independent of serialized inference. Active and queued cancellation and shutdown pass bounded tests; cancellation also reclaims queue slots.
- Audio validation rejects sibling prefixes, traversal and reparse points, then retains a read handle whose canonical final path is checked. Both DOS short-root/long-file and long-root/short-file spellings pass real transcription.
- Protocol preserves accents and invariant numbers, rejects malformed/oversized input, accepts late type discriminators, and returns optional native segments.
- Model fetch checks pinned file sizes and SHA-256 hashes before promotion. Existing f16/Silero models verified without downloading; unknown names and corrupt cached content fail with no target/.part retained.

## Task Commits

1. Task 1 tracer: `25f2293` — scaffold, protocol, worker and CPU process tracer.
2. Task 2 RED: `0b663f0` — pure contracts and failing queued-cancel test.
3. Task 2 GREEN: `1c54ccd` — immediate queued cancellation, retained audio handles, Debug fault guards and stronger tests.
4. Follow-up correctness fixes: `29a3df5` — DOS canonicalization, property-order parsing and native segment output.
5. Task 3: `8cba95b` — verified model fetch, published CPU/Vulkan gate and committed evidence.

## Verification

- `dotnet build Somning.sln -c Debug`: zero warnings, zero errors.
- `dotnet test --project tests/Somning.Core.Tests`: 18 passed, zero failures/skips.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter-class "*.EngineWorkerTests"`: original five methods passed, zero failures/skips (86.50 s).
- Added explicit DOS-alias regression: `--filter-method "*.Dos_short_paths_are_canonicalized"` passed both mixed spellings in one test (52.96 s), zero skips.
- Final focused tracer recheck confirms returned segments on the final protocol.
- Working MTP selectors are `--filter-class` and `--filter-method`, without a second `--` separator. A help invocation with the extra separator triggered an SDK help-channel error; its owned stale process was stopped before subsequent builds.
- Debug default and explicit Release constants checked through `dotnet msbuild -getProperty:DefineConstants`. Release contains neither fault environment switch.
- All requested NativeMethods.txt names generated successfully; none removed.
- C# review blocked queued cancellation initially, then confirmed fixes; generic code review approved. Both identified DOS mixed-spelling edge cases, resolved with canonicalization and the strict regression. Build resolver independently confirmed the xUnit API and Debug constants adjustments.

## TDD Evidence

The named queued cancellation test failed while waiting five seconds for B's response behind active A, proving the intended control-path gap. The real xUnit JUnit report is retained in ignored `TestResults/task2-red.xml`, with its command/exit/semantic record in `TestResults/task2-red.json`. The GSD classifier returned `RED_EVIDENCE_OK` before production GREEN edits. Initial record serialization errors were corrected without altering the test report. Pure contracts already passed because Task 1 implemented their behavior.

## Published Gate

Final evidence: [GATE-RESULT.md](GATE-RESULT.md).

| Backend | load_ms | warm_ms | infer_ms | WER | Result |
|---|---:|---:|---:|---:|---|
| vulkan | 1993.87 | 2159.39 | 465.89 | 0.00% | PASS, GPU in use |
| cpu | 1529.94 | 12399.40 | 13150.74 | 0.00% | PASS |

The self-contained Release directory ran from TEMP with only Windows directories on PATH, nonexistent DOTNET_ROOT paths and multilevel lookup disabled. System VC++ redistributables and Vulkan driver dependencies remain deployment prerequisites. These are one synthetic fixture's functional timings, not a latency benchmark.

## Deviations from Plan

1. **[Rule 3 - Blocking API]** xUnit 4.0.2 marks `CollectionBehavior.DisableTestParallelization` hard-obsolete. Used `Xunit.v3.Parallelization(Mode = ParallelMode.None)`, confirmed by the installed package's XML documentation.
2. **[Rule 1 - Bug]** Default `dotnet test` evaluated Directory.Build.props before Configuration was populated, excluding Debug fault code. Default Configuration is now set before the guarded constants.
3. **[Rule 2 - Security]** Validation before enqueue alone allowed path replacement before inference. Retained, final-path-validated file handles now cross the native boundary.
4. **[Rule 1 - Bug]** Initial deployment attempts exposed request parsing with a late type discriminator; PowerShell's unordered hashtable made this visible. Allowed out-of-order metadata and added an explicit protocol test. Also normalized DOS aliases at every boundary comparison rather than weakening containment.
5. **[Rule 2 - Contract]** Reconciled D-03's segment requirement with the earlier plan omission by adding optional, backward-compatible segment transport. Native start/end are milliseconds.
6. **[Rule 3 - Isolation]** Gate staging uses a unique child beneath TEMP/somning-gate to prevent stale deployed files from contaminating a run.

No native compatibility failure occurred; readiness succeeded in the initial failed gate attempts, and final real transcription passed on both backends. The initial failures were worker/input integration bugs, fixed before proceeding.

## Scope and Readiness

VAD execution and its timing remain assigned to plan 01-05; `vad_ms=0` accurately represents that no VAD stage runs in this worker slice. No goal-blocking stub or skipped verification remains. App/supervisor/clipboard work can now consume the stable protocol in plan 01-02.

STATE.md, ROADMAP.md and requirements updates are owned by the parent orchestrator to avoid shared-state edits.

## Self-Check: PASSED

All six key delivered files exist; all five task commits are ancestors of HEAD. Final focused tracer passed 1/1, zero failures/skips (28.80 s).

