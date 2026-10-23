---
phase: 01-dictation-loop-mvp
plan: 12
status: complete
completed: 2026-10-23
requirements: [HOTK-01, HOTK-02, HOTK-03, AUD-04, STT-01, STT-02, STT-03, STT-08, OUT-01, OUT-02]
---

# Plan 01-12: App End-to-End Acceptance

Implemented desktop acceptance coverage through the real Debug App, keyboard hook, file-backed audio source, spool, worker, clipboard and owned Notepad. Added replay-journal based correlation by sequence and recording ID, clipboard-preservation checks for non-speech cases, and recovery/fallback tests through the App composition.

The hook now captures the foreground window's keyboard layout at chord start. If Ctrl+Shift+Space changed it, the hook thread posts `WM_INPUTLANGCHANGEREQUEST` with the original HKL after the chord events drain. This restores the exact saved layout even when the machine has more than two layouts. The test harness forwards `SOMNING_ENGINE_FAULT` only to its first worker so crash and GPU retry paths can be exercised while the supervisor continues stripping inherited fault variables.

## Task results

- **Task 1:** tap and long-hold dictations pasted accurate text into Notepad with matching clipboard text. Ten warm 15-second runs passed the configured budget. Evidence is in [01-12-latency.json](01-12-latency.json): selected `vulkan-q5_0-on`, p50 **1,562 ms**, p95 **1,657 ms**, budget **3,000 ms**. Layout restoration passed after detecting the original Ctrl+Shift layout toggle.
- **Task 2:** silence, noise, tone, sub-400 ms tap and blocklisted “obrigado” all produced empty outcomes while preserving Notepad and the user's clipboard text and sequence number.
- **Task 3:** killing the active worker during a request and an injected Vulkan inference fault both recovered on CPU and pasted successfully. Partial WAV recovery and completed capture with durably saved transcript both recovered clipboard-only after restarting the App; Notepad remained empty.

## Verification

Commands ran sequentially in Git Bash with `SOMNING_STRICT=1`; xUnit v3 uses the supported `--filter FullyQualifiedName~...` selector.

- `dotnet build Somning.sln -c Debug` — passed, 0 warnings and 0 errors.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter "FullyQualifiedName~DictationE2ETests"` — **3 passed**; includes 10 measured dictations.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter "FullyQualifiedName~KeyboardHookTests"` — **5 passed**.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter "FullyQualifiedName~HoldWatchdogTests"` — **2 passed**.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter "FullyQualifiedName~SilenceE2ETests"` — **5 passed**.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter "FullyQualifiedName~ResilienceE2ETests"` — **4 passed**.

All tests reported zero skips. No Somning.Engine or Somning.App processes remained after verification.

## Deviations

- Added `src/Somning.App/Composition/AppComposition.cs` to the plan's file list because `EngineSupervisor` intentionally strips `SOMNING_ENGINE_FAULT`; the harness now explicitly passes it to the first Debug worker.
- The layout guard found a real Ctrl+Shift layout toggle. A first posted restoration attempt ran before the remaining key-up events and a chord replay could not guarantee correctness with three or more layouts. The final implementation waits until input drains and requests the captured HKL directly.
- Added the JSON latency artifact so the measured raw samples and percentiles remain available to later verification.
