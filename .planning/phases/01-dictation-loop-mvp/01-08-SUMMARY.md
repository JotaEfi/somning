---
phase: 01-dictation-loop-mvp
plan: "08"
subsystem: benchmark-and-budget
tags: [stt-08, latency-budget, matrix-benchmark, vulkan, cpu-fallback, paste-verification]
requires:
  - phase: 01-04
    provides: TargetPaster and atomic keyboard injection
  - phase: 01-05
    provides: Model quantizations and CPU/Vulkan engine builds
  - phase: 01-07
    provides: Engine supervision and process lifecycle
provides:
  - Benchmark CLI tools (Somning.Bench run, report, check)
  - Full 12-configuration matrix empirical evaluation across {vulkan,cpu} x {f16,q8_0,q5_0} x {on,off}
  - Official BENCHMARK.md report with latency, WER, FA comparison, and deployment prerequisites
  - Selected defaults in EngineDefaults.cs matching D-17 selection algorithm and STT-08 budget
affects: [01-09, 01-10, 01-11, 01-12, 01-13]
tech-stack:
  added: []
  patterns: [benchmark-owned-editor-delivery, nearest-rank-percentiles, exclusive-bench-interval, automated-consistency-check]
key-files:
  created:
    - tools/Somning.Bench/Somning.Bench.csproj
    - tools/Somning.Bench/Program.cs
    - tools/Somning.Bench/BenchRunner.cs
    - tools/Somning.Bench/BenchEngineClient.cs
    - tools/Somning.Bench/OwnedEditor.cs
    - tools/Somning.Bench/ReportWriter.cs
    - tools/Somning.Bench/ResultsCheck.cs
    - .planning/phases/01-dictation-loop-mvp/BENCHMARK.md
    - .planning/phases/01-dictation-loop-mvp/bench-results.jsonl
  modified:
    - Somning.sln
    - src/Somning.Core/Engine/EngineDefaults.cs
key-decisions:
  - "Stop-to-paste latency is measured exclusively through real Win32 paste delivery into an active benchmark-owned editor (D-17); clipboard-only surrogates are rejected."
  - "The benchmark-owned editor thread switches to the Win32 input desktop before creating WinForms UI, avoiding CLR pre-STA desktop binding (ERROR_BUSY 170)."
  - "Selected default is vulkan-q5_0-on (WER 0.00%, stop-to-paste p50=1615.41 ms, p95=1664.98 ms) which meets the STT-08 budget (p95 <= 3000 ms) with lowest p95 latency."
  - "Selected CPU fallback is cpu-q8_0-on (WER 0.00%, stop-to-paste p50=11580.00 ms, p95=12369.81 ms), labeled as degraded mode (fallback only)."
  - "Flash Attention yields a ~2.0x speedup on Vulkan and ~1.2x on CPU while preserving 0.00% clean WER."
requirements-completed: [STT-08]
metrics:
  duration: "not recorded"
  tasks: 2
  files: 11
  commits: 2
  completed: 2026-10-23
  status: complete
---

# Phase 1 Plan 8: Benchmark Slice & STT-08 Budget Summary

**The published Somning engine has been empirically evaluated across the full 12-configuration matrix with real paste delivery; defaults and the STT-08 budget are now fixed.**

## Accomplishments

- Implemented `tools/Somning.Bench` with subcommands `run`, `report`, and `check`, integrated into `Somning.sln`.
- Solved the paste delivery verification challenge on headless/automated Windows sessions by ensuring the UI thread binds to `OpenInputDesktop` before COM STA window initialization and activating the owned editor window on the input desktop.
- Ran all 12 configurations ({vulkan, cpu} x {f16, q8_0, q5_0} x {flash attention on, off}) sequentially in exclusive intervals with >= 10 measured runs of the 15 s Portuguese audio clip (`tests/fixtures/speech/longo-numeros.wav`) and 6 clean accuracy fixtures.
- Generated `.planning/phases/01-dictation-loop-mvp/BENCHMARK.md` reporting full latency percentiles (p50/p95), WER, Flash Attention speedups (2.0x on Vulkan), and deployment prerequisites.
- Implemented `ResultsCheck` to independently verify the 12-configuration contract, D-17 selection criteria, CPU fallback, and consistency with `EngineDefaults.cs`.
- Updated `src/Somning.Core/Engine/EngineDefaults.cs` to set `vulkan` with `ggml-large-v3-turbo-q5_0.bin`, `FlashAttention = true`, and CPU fallback `ggml-large-v3-turbo-q8_0.bin`, citing `BENCHMARK.md`.

## Task Commits

1. **Task 1: Tracer — Somning.Bench runs one config against the published engine with real paste delivery** — `ede5587` (`feat(01-08): benchmark harness with paste delivery`).
2. **Task 2: Full matrix → BENCHMARK.md + check + selected defaults (D-17, STT-08)** — `2ec29a0` (`feat(01-08): full benchmark matrix, STT-08 budget and selected defaults`).

## Test Evidence

- `tools/Somning.Bench report` successfully generated `BENCHMARK.md`.
- `tools/Somning.Bench check` independently evaluated all 12 configurations and verified consistency with exit code 0.
- `grep -n "BENCHMARK.md" src/Somning.Core/Engine/EngineDefaults.cs` verified line citation.
- `dotnet test --project tests/Somning.Core.Tests` passed 72/72 tests (72 passing, 0 skips).
- Selected Default: `vulkan-q5_0-on`
  - Stop-to-paste p50: **1615.41 ms**
  - Stop-to-paste p95: **1664.98 ms** (comfortably under the 3000 ms STT-08 budget)
  - Mean Clean WER: **0.00%**
- CPU Fallback: `cpu-q8_0-on`
  - Stop-to-paste p50: **11580.00 ms**
  - Stop-to-paste p95: **12369.81 ms** (degraded mode fallback)
  - Mean Clean WER: **0.00%**

## Next Step

Plan 01-08 is complete. The dictation loop MVP is ready for Plan 01-09.

## Self-Check

- [x] Both plan tasks are implemented and committed.
- [x] All 12 configurations measured with >= 10 runs each on the 15 s clip.
- [x] BENCHMARK.md contains the full matrix, Selected section, and STT-08 budget.
- [x] EngineDefaults.cs updated to match Selected and cites BENCHMARK.md.
- [x] `Somning.Bench check` exits 0.

**Self-check: PASSED**
