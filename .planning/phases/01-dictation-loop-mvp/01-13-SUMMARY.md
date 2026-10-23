---
phase: 01-dictation-loop-mvp
plan: 13
status: complete
completed: 2026-10-23
requirements: [HOTK-01, HOTK-02, HOTK-03, AUD-01, AUD-02, AUD-03, AUD-04, STT-01, STT-02, STT-03, STT-04, STT-05, STT-06, STT-07, STT-08, OUT-01, OUT-02, OUT-03, REL-01, REL-02, REL-03, REL-04, SEC-01, SEC-02]
---

# Plan 01-13: Phase 1 Milestone Closeout & Acceptance Verification

Formally validated and closed Phase 1 (`Dictation Loop MVP`) across the full dictation loop: hotkey capture, audio conversion, whisper.cpp Vulkan inference with CPU fallback, safe paste, spool durability, crash resilience, published standalone application layout, and empirical benchmark consistency.

The milestone acceptance evidence was generated sequentially by the official runner `tools/verify-phase1.ps1` under PowerShell 5.1 with `SOMNING_STRICT=1`, producing passing artifacts in [E2E-RESULT.md](E2E-RESULT.md), [GATE-RESULT.md](GATE-RESULT.md), and [01-12-latency.json](01-12-latency.json).

## Task Results

### Task 1: Deferred paste and private metrics verification
- Implemented and verified `PasteDeferredE2ETests`:
  - `Foreground_change_defers_paste`: switching foreground windows during inference safely defers paste, preserves the exact transcription on the clipboard, and keeps both test editors empty (reason `foreground_changed`).
  - `Focus_change_inside_same_window_defers`: switching control focus inside a multi-field window defers paste (reason `focus_changed`), preserving clipboard text without corrupting inactive fields.
  - `Multiple_defers_retain_audio_and_clipboard`: back-to-back focus mismatches retain both the spoken audio in spool and the transcript in clipboard history.
  - `Elevated_target_defers_paste_when_a_real_integrity_pair_is_available`: policy path verified in `PasteGateTests` (10/10 PASS); explicit desktop skip handled cleanly when a noninteractive low/high integrity pair is not pre-provisioned.
- Verified `MetricsLogTests` confirming 100% compliance with private schema (UUID session/dictation IDs, durations, WER/p50/p95, and zero raw transcripts in telemetry).

### Task 2: Self-contained published App layout gate
- Refined `tools/gate.ps1 -App` verifying that `Somning.App` publishes self-contained to `publish\app\` and executes under clean-room conditions:
  - Sanitized PATH without .NET SDK (`C:\Windows\system32;C:\Windows`), `DOTNET_ROOT` unset, and `DOTNET_MULTILEVEL_LOOKUP=0`.
  - Directory layout including paths with spaces and non-ASCII characters (`Somning App Téste`).
  - Native engine worker resolved at `app\engine\Somning.Engine.exe` with clipboard WER 0.00%.
  - Release build excludes test harness and faults; invocation with `--test-harness` rejects with exit code 2.

### Task 3: Sequential final verifier & SC1–SC5 measured evidence
- Executed `tools/verify-phase1.ps1` running all 33 sequential checks:
  - Debug build: PASS (1.8s)
  - Core tests: 100/100 PASS (1.4s)
  - 28 Integration test suites: 100% PASS (zero regressions, zero hung workers, and zero unauthorized skips)
  - Hardware WASAPI capture: `WasapiSmokeTests` passed 1/1 against the physical default audio device
  - Engine published gate: `GATE PASS vulkan` (infer 1,583 ms, WER 0.00%) and `GATE PASS cpu` (infer 12,258 ms, WER 0.00%)
  - App published gate: `GATE PASS published App` (WER 0.00%)
  - Benchmark checker: all 12 configurations validated in `bench-results.jsonl` matching `EngineDefaults`
  - Generated [E2E-RESULT.md](E2E-RESULT.md) with `Execution status: **PASS**`.

---

## Roadmap Success Criteria Verification (SC1–SC5)

| Criterion | Target | Actual Verified Result | Status |
|---|---|---|---|
| **SC1** | Benchmark <= 3.0s p95 on target GPU, CPU fallback, clean-room self-contained publish | Vulkan p95 = 1,664.98 ms (WER 0.00%); CPU p95 = 12,369.81 ms (WER 0.00%); clean-room publish verified in `publish\engine` and `publish\app` with PATH sanitized. | **PASS** |
| **SC2** | Hotkey-to-paste text insertion with <= 3000 ms p95 warm stop-to-paste, keyboard layout preservation | 10 warm samples: `1719, 1547, 1532, 1562, 1593, 1500, 1578, 1547, 1563, 1531` ms. **p50 = 1547 ms, p95 = 1719 ms** (well below 3000 ms budget). Win32 `WM_INPUTLANGCHANGEREQUEST` layout guard confirmed. | **PASS** |
| **SC3** | Non-speech audio (silence, noise, sub-400ms taps, blocklist) generates zero text and preserves existing clipboard | `SilenceE2ETests` 5/5 passed. Existing clipboard contents, sequence numbers, and Notepad remain completely untouched. | **PASS** |
| **SC4** | Crash during inference or GPU failure automatically retries and falls back to CPU; spool retains undelivered audio across restarts | `ResilienceE2ETests` 4/4 and `EngineSupervisorTests` 9/9 passed. Process job object `KILL_ON_JOB_CLOSE` prevents worker leaks. Partial WAV recovery and durable spool retention verified. | **PASS** |
| **SC5** | Focus change during inference defers paste, preserves transcript on clipboard, and logs privacy-safe telemetry | `PasteDeferredE2ETests` 3/3 passed (1 authorized desktop UIPI skip). `PasteGateTests` 10/10 passed. `MetricsLogTests` 1/1 passed with zero private transcripts logged. | **PASS** |

---

## Allowed Environment Skips

- `PasteDeferredE2ETests: optional UIPI environment`:
  - `Elevated_target_defers_paste_when_a_real_integrity_pair_is_available`: Skipped non-destructively because no non-interactive low-integrity App / high-integrity target pair was provisioned in this desktop session. Policy logic is fully covered by `PasteGateTests`.
- `WasapiSmokeTests`:
  - Zero skips; physical default capture hardware was present and captured successfully for 1.0s.

---

## Verification Artifacts Summary

- ** acceptance report:** [.planning/phases/01-dictation-loop-mvp/E2E-RESULT.md](E2E-RESULT.md)
- **Engine & App publish gate report:** [.planning/phases/01-dictation-loop-mvp/GATE-RESULT.md](GATE-RESULT.md)
- **10 warm stop-to-paste samples:** [.planning/phases/01-dictation-loop-mvp/01-12-latency.json](01-12-latency.json)
- **Model benchmark matrix:** [.planning/phases/01-dictation-loop-mvp/BENCHMARK.md](BENCHMARK.md) and `bench-results.jsonl`
