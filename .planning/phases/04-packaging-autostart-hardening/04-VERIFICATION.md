---
phase: 04-packaging-autostart-hardening
verified: 2026-10-29T15:00:00Z
status: gaps_found
score: 3/4 must-haves verified (1 partial, items accepted as manual pending owner confirmation)
re_verification: true
gaps:
  - truth: "Paste-matrix report covers Notepad, Chrome/Edge, VS Code, Windows Terminal, Word, elevated notice"
    status: partial
    reason: "Word row FAILS (read-back returns the sentinel; cause not found, deferred-items.md). Edge is not installed (SKIPPED). Windows Terminal and real UIPI are MANUAL. Chrome, VS Code and Notepad PASS."
    missing:
      - "Reliable Word paste (focus/read-back) or an explicit owner acceptance of the limitation"
human_verification:
  - test: "Windows Terminal Ctrl+V paste"
    expected: "Text pasted or documented manual-paste notice"
    why_human: "No automated test (MANUAL-CHECKLIST.md item 1)"
  - test: "Elevated Notepad, real UIPI; watchdog blocked-injection; UAC dialog"
    expected: "Paste deferred with notice; no reinstall storm"
    why_human: "Needs a real low/high integrity pair (SKIPPED/MANUAL)"
  - test: "Real sleep/wake and real lock/unlock, then dictate"
    expected: "Hotkey works after wake/unlock"
    why_human: "Only synthetic messages tested"
  - test: "Clean Windows 10 profile install without .NET SDK; logon autostart after reboot"
    expected: "Install + dictation work; tray icon and hotkey after logon"
    why_human: "Not performed (MANUAL-CHECKLIST.md items 5 and 6)"
---

# Phase 4: Packaging, Autostart & Hardening - Re-verification Report

**Goal:** User installs Somning on a clean Windows 10 machine and it keeps working across restarts, sleep and long uptime.
**Status:** gaps_found (1 partial gap). **Re-verification:** Yes, after gap-closure plans 04-04, 04-05 and 04-06.
**Evidence:** `PASTE-MATRIX.md` (final run 2026-10-29 14:46 UTC, one attempt per row: PASS=23 FAIL=1 SKIPPED=3 MANUAL=4) and `SOAK-REPORT.json`.

## Gap status

| # | Previous gap | Status now | Evidence |
|---|--------------|-----------|----------|
| 1 | No soak test | CLOSED | SoakTests (50 inject-tap dictations, clipboard-only, then one real-hotkey dictation): completed 50/50, 0 failures, handle delta 26, private bytes delta 12.7 MB, hook installs 1 to 1, 0 watchdog reinstalls, final real-hotkey dictation OK. |
| 2 | Installed binary not run through the Phase 1 Notepad E2E | CLOSED | Both installed-binary E2E rows PASS (tap-tap and hold-release, harness package installed to a space + non-ASCII path, uninstalled afterwards). |
| 3 | Matrix lacked app rows | PARTIAL | Chrome PASS, VS Code PASS, Edge SKIPPED (not installed), Word FAIL. The Word failure is shown in the matrix, not hidden. |
| 4 | No hook liveness watchdog | CLOSED (real silent-removal case) | Real hook silently removed, watchdog reinstalled it, F24 probe seen again: PASS. The blocked-injection case is SKIPPED (needs an elevated foreground window). |

Other results from the run: unattended install, shortcut, Run key, installed transcription, `--tray` launch and uninstall all PASS; same-window focus-change E2E (previously flaky) PASS on its single attempt (not a statistical proof); synthetic sleep/lock events PASS.

## Behavioral checks
- `dotnet build Somning.sln -warnaserror`: 0 warnings, 0 errors. Core.Tests: 307 passed.
- Defects found and fixed during evidence runs (details in 04-06-SUMMARY.md): harness controls not wired, VS Code welcome dialog, hook reinstall zeroing the chord tracker (a possible Space leak, never reproduced), orphaned processes hanging the matrix script.

## Requirements
| Req | Status | Note |
|-----|--------|------|
| DIST-01 | Evidence satisfied except manual items | Install works incl. odd paths; installed binary passes the Notepad E2E. Clean-machine install and logon autostart remain MANUAL. |
| DIST-02 | SATISFIED | Run key row PASS, `--tray` launch PASS (logon start is MANUAL). |
| DIST-03 | Evidence satisfied except manual/known-limit items | Soak and watchdog PASS; paste matrix has a Word FAIL, Edge SKIPPED, Windows Terminal/real UIPI/real sleep/lock MANUAL. |

## Owner acceptance (2026-10-29) - NEEDS CONFIRMATION
The phase orchestrator reported that the owner explicitly accepted closing the phase with these items left MANUAL or as known limitations, quoting the owner's message "aceito, segue para o fechamento da fase": Word, Edge, real UIPI, watchdog blocked-injection, Windows Terminal, real sleep/wake, real lock/unlock, UAC dialog, clean-machine install and logon autostart. The executor could not verify that message itself (it arrived through an agent, not directly), so REQUIREMENTS.md still shows DIST-01 and DIST-03 as Pending. Once the owner's acceptance is confirmed, mark both Complete with this caveat (see `MANUAL-CHECKLIST.md`, "Acceptance record").

## Anti-patterns
- The Word failure root cause is unproven; a fuller `.docx`, a read-back retry and an editor-class precondition were applied without a passing run (deferred-items.md).
- The Chromium editor check accepts `Chrome_WidgetWin_1`, which is weaker than a true editor-control check; window and process identity are asserted as well.
