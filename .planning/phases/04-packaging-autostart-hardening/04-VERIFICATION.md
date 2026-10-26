---
phase: 04-packaging-autostart-hardening
verified: 2026-10-26T21:00:00Z
status: gaps_found
score: 2/4 must-haves verified
gaps:
  - truth: "A scripted soak of many dictations leaves the hotkey hook working"
    status: failed
    reason: "No soak test or script exists (only mentioned in 04-03-PLAN.md). No code under src/tests/tools contains a soak run."
    missing:
      - "Scripted soak (N dictations, hook still active/InstallCount sane) in tests/ or tools/verify-matrix.ps1"
  - truth: "Installed binary passes the Phase 1 E2E into Notepad"
    status: partial
    reason: "Matrix only runs installed app with --transcribe-file (clipboard text compare) and --tray launch. Notepad E2E tests run against the build output, not the installed copy."
    missing:
      - "Run the Notepad E2E against the installed Somning.App.exe"
human_verification:
  - test: "Windows Terminal Ctrl+V paste"
    expected: "Text pasted or documented manual-paste notice"
    why_human: "No automated test (MANUAL in matrix)"
  - test: "Real sleep/resume and lock/unlock, then dictate"
    expected: "Hotkey works after wake/unlock (hook reinstalled)"
    why_human: "Only synthetic WM_POWERBROADCAST/WM_WTSSESSION_CHANGE tested"
  - test: "Elevated Notepad real UIPI behavior"
    expected: "Paste deferred with notice"
    why_human: "Integration test SKIPPED; only PasteGate policy unit-tested"
  - test: "Clean Windows 10 profile/machine install without .NET SDK"
    expected: "Install + dictation work"
    why_human: "Not performed; only run on dev machine"
---

# Phase 4: Packaging, Autostart & Hardening Verification Report

**Goal:** User installs Somning on a clean Windows 10 machine and it keeps working across restarts, sleep and long uptime.
**Status:** gaps_found. **Re-verification:** No.

## Success Criteria

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Per-user unattended installer on clean profile (space/non-ASCII path); installed binary passes Phase 1 E2E into Notepad | PARTIAL (FAILED on the Notepad clause) | tools/install.ps1, uninstall.ps1, package.ps1 exist; PASTE-MATRIX shows install to path with space + CJK + accent PASS, shortcut, uninstall PASS, installed app transcribes hoje.wav. Notepad E2E not run against installed binary; no clean-machine test. |
| 2 | Start-with-Windows writes HKCU Run entry; app starts minimized to tray | VERIFIED (code + test) | RegistryAutostartManager, settings checkbox, Program.cs accepts --tray/--minimized, Run key row PASS, installed `--tray` launch PASS. Actual logon start not tested (low risk). |
| 3a | After simulated sleep/resume and lock/unlock the hook is reinstalled | VERIFIED (synthetic) | PowerSessionListener -> AppComposition.ReinstallHookAfterWake -> KeyboardHook.Reinstall; PowerSessionIntegrationTests 3/3 PASS using synthetic posted messages. Event-driven only; no periodic liveness watchdog found despite plan title. |
| 3b | Scripted soak of many dictations | FAILED | No soak implementation exists anywhere in code or tools. |
| 3c | Paste-matrix report produced (Notepad, Chrome/Edge, VS Code, Windows Terminal, Word, elevated notice) | PARTIAL | PASTE-MATRIX.md exists (16 PASS/0 FAIL/1 SKIPPED/2 MANUAL) but covers only Notepad; no Chrome/Edge, VS Code, Word rows; Windows Terminal MANUAL; real UIPI SKIPPED. |

## Behavioral checks run
- `dotnet build`: 0 warnings, 0 errors.
- `dotnet test --project tests/Somning.Core.Tests`: 296 passed, 0 failed.
- Interactive/integration tests not rerun (per instructions); PASTE-MATRIX.md is the recorded evidence.

## Requirements
| Req | Status | Note |
|-----|--------|------|
| DIST-01 | PARTIAL | Installer works on dev machine incl. odd paths; clean-machine and installed Notepad E2E not verified. REQUIREMENTS.md marks Complete: premature. |
| DIST-02 | SATISFIED | See SC2. |
| DIST-03 | OPEN | Soak missing, real sleep/lock manual, matrix coverage incomplete. Keep Pending. |

## Anti-patterns
- deferred-items.md still states the flaky Phase 01 E2E and says to rerun on idle desktop; the later rerun passed 16/0, but flakiness (2 of 6 isolated failures) is not root-caused.
- Matrix honesty is good: no hardcoded PASS, SKIPPED/MANUAL labelled.

## Gaps Summary
1. No soak test (blocker for SC3).
2. Matrix lacks Chrome/Edge, VS Code, Word rows required by SC3 (add rows or amend the roadmap).
3. Installed-binary Notepad E2E not demonstrated.
4. Human items above remain; DIST-01 clean-machine and DIST-03 should not be marked complete yet.
