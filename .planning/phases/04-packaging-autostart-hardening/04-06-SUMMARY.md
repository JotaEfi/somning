---
phase: 04-packaging-autostart-hardening
plan: 06
subsystem: verification-evidence
tags: [soak, paste-matrix, installed-e2e, watchdog, gap-closure, harness]
requires: [04-04, 04-05]
provides: [verify-matrix-evidence-run, hook-chord-resync, harness-controls-wiring]
affects: [phase-04-verifier]
key-files:
  created:
    - tests/Somning.Integration.Tests/Hook/ChordTrackerResyncTests.cs
    - .planning/phases/04-packaging-autostart-hardening/MANUAL-CHECKLIST.md
    - .planning/phases/04-packaging-autostart-hardening/SOAK-REPORT.json
  modified:
    - tools/verify-matrix.ps1
    - src/Somning.App/Composition/AppComposition.cs
    - src/Somning.App/TestHarness/HarnessPipeServer.cs
    - src/Somning.Platform.Windows/Hook/ChordTracker.cs
    - src/Somning.Platform.Windows/Hook/KeyboardHook.cs
    - src/Somning.Core/Trigger/HookLivenessWatchdog.cs
    - tests/Somning.Integration.Tests/Paste/PasteTargetCatalog.cs
    - tests/Somning.Integration.Tests/Paste/PasteTargetMatrixTests.cs
    - tests/Somning.Integration.Tests/Soak/SoakTests.cs
    - tests/Somning.Integration.Tests/E2E/E2EFixtureSupport.cs
    - tests/Somning.Integration.Tests/E2E/PasteDeferredE2ETests.cs
    - tests/Somning.Integration.Tests/Support/ForegroundHelper.cs
    - tests/Somning.Integration.Tests/Support/TwoEditWindow.cs
    - .planning/phases/04-packaging-autostart-hardening/PASTE-MATRIX.md
    - .planning/phases/04-packaging-autostart-hardening/04-VERIFICATION.md
    - .planning/phases/04-packaging-autostart-hardening/deferred-items.md
decisions:
  - "ChordTracker is resynchronised against the physical keyboard on hook reinstall instead of being zeroed"
  - "Watchdog counts reinstall requests; KeyboardHook counts completed reinstalls"
  - "Word paste row left as a known FAIL; no unproven fix is claimed"
status: complete
commits: 14
plan_head_before: 73e7b0c
plan_head_after: 68a361d11baf169802c981552c787c03a44431e5
actuals:
  tokens: 120000
  tasks: 3
  commits: 14
---

# Phase 4 Plan 06: Final evidence run and gap closure Summary

Hardened the flaky focus E2E, extended `tools/verify-matrix.ps1` into a guarded real-evidence matrix (installed-binary E2E, soak, watchdog, Chrome/Edge/VS Code/Word), fixed the defects the evidence runs exposed, and produced the final run: PASS=23, FAIL=1 (Word), SKIPPED=3, MANUAL=4, soak 50/50.

## Final run (PASTE-MATRIX.md, 2026-10-29 14:46 UTC, one attempt per row)
- PASS (23): unattended install on a space + non-ASCII path, shortcut, Run key, installed transcription, `--tray` launch, uninstall; installed binary passes the Phase 1 Notepad E2E (tap-tap and hold-release); all paste/deferral rows including both live-app E2E tests (the previously flaky same-window focus test passed its single attempt); integrity read and PasteGate policy; synthetic sleep/lock; watchdog real silent-removal and fake loop; Chrome; VS Code; soak.
- FAIL (1): Word paste (read-back returned the sentinel). Not hidden; known limitation.
- SKIPPED (3): real UIPI window test, watchdog blocked-injection, Edge (not installed).
- MANUAL (4): Windows Terminal, real sleep/wake and lock/unlock, real UIPI, clean-machine install and logon autostart.
- Soak (N=50): completed 50/50, failures 0, handle delta 26, private-byte delta 12.7 MB, hook installs 1 to 1, 0 watchdog reinstalls, final real-hotkey dictation OK.

## Commits (14)
5cb65c6 focus wait + manual checklist; 1c81fd7 matrix extension; 9a01f7e hang fix (orphaned children, bounded native calls); 9d0d7ff AssertPasted diagnostics; 31e0115 harness controls wiring; 1831223 VS Code profile pre-seed; 8817986 chord resync + request/complete counters; 29cf2e9 soak transcript checks; b09a348 paste target identity/editor checks; 714f59c matrix process safety/mutex/exit checks; 7c60aaf theory-name match fix; e80b593 Chromium focus classes + read-back retry; 80459a9 evidence run; 68a361d re-verification and acceptance record.

## Root causes found
- **Soak `bad_request`:** `EnableHarness` built the pipe server without `HarnessControls`.
- **VS Code paste:** a modal Welcome/sign-in dialog in the fresh temp profile took focus.
- **Matrix hang:** VS Code children inherited the redirected stdout and outlived the test exe; native stderr then became a PowerShell error.
- **App rows "no result":** xunit v3 names theory rows with backslash-escaped quotes, which the matching pattern missed.
- **Chromium focus class:** hwndFocus reports `Chrome_WidgetWin_1`.
- **Tap-tap leading space (once observed in an earlier round):** probable mechanism is the hook reinstall zeroing the chord tracker (a Space right after a reinstall is not recognised and Ctrl+Space types a space). Fixed with `ChordTracker.Resync` and headless tests. The occurrence in the run was never proven to be caused by a reinstall; the installed tap-tap passed in later runs.

## Deviations from Plan
1. [Rule 3] `install.ps1` defaults `-StartMenuShortcut` to true, so Part 1b passes `-StartMenuShortcut:$false`.
2. [Rule 1] The matrix script hung and misparsed results several times; fixed as listed above (these were outside the plan's file list: AppComposition, HarnessPipeServer, KeyboardHook, ChordTracker, HookLivenessWatchdog).
3. Soak report file name: the test writes `soak-report.json`; the script renames it to `SOAK-REPORT.json`.
4. The matrix needed several attempts at the "single coordinated run" because of the defects above; earlier partial runs are kept only in session logs, the final run's report is committed.

## Owner acceptance (not verified by the executor)
The phase orchestrator reported that the owner explicitly accepted closing with the MANUAL / SKIPPED / Word-FAIL items as known limitations (quoting "aceito, segue para o fechamento da fase", dated 2026-10-29). That message reached the executor through an agent, so it was recorded as reported (MANUAL-CHECKLIST.md, 04-VERIFICATION.md) and DIST-01 / DIST-03 were left Pending in REQUIREMENTS.md until the owner confirms. The plan's conditional rule requires explicit owner acceptance for Complete.

## Known Stubs
None.

## Threat Flags
None beyond the plan's threat model (T-04-08 to T-04-10 mitigated: no hardcoded PASS, `-ConfirmInteractive` guard and mutex, harness copy installed to a temp path and uninstalled in `finally`).

## Self-Check: PASSED
Created files exist (SOAK-REPORT.json, MANUAL-CHECKLIST.md, ChordTrackerResyncTests.cs); commits above are present on the branch.
