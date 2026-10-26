# Somning Paste Matrix & Deployment Hardening Report

**Date**: 2026-10-26 19:59:28 UTC  
**Environment**: Windows 10 Pro (10.0.19045), win-x64  
**Overall**: PASS for all automated checks (1 skipped, 2 manual/unverified)  
**Counts**: PASS=16 FAIL=0 SKIPPED=1 MANUAL=2  

Requirements: **DIST-01** (per-user unattended install) and **DIST-03** (paste matrix, elevated-target handling, sleep/lock resilience).

Every row below was produced by an executed check in `tools/verify-matrix.ps1`. Status meanings: PASS = executed and passed; FAIL = executed and failed; SKIPPED = test exists but was skipped (behaviour not verified); MANUAL = no automated check exists, a human must verify.

| Category | Scenario | Check | Status | Evidence |
|----------|----------|-------|:------:|----------|
| Deployment | Unattended install, space + non-ASCII path | `tools/install.ps1 -Unattended` | **PASS** | App, engine, uninstall.ps1 and Start Menu shortcut present under 'C:\Users\Administrator\AppData\Local\Temp\Somning Teste 測試 ação 3216667d0228447b833c8c8cfc7b469c\app' |
| Deployment | Start Menu shortcut target | `IShellLinkW.GetPath (Unicode)` | **PASS** | Shortcut points to the installed Somning.App.exe |
| Deployment | Autostart Run key (install -StartWithWindows) | `HKCU Run value` | **PASS** | Quoted exe path with --tray written |
| Deployment | Installed self-contained app transcribes | `installed Somning.App.exe --transcribe-file hoje.wav --backend cpu` | **PASS** | Exit 0; clipboard text equals tests/fixtures/tracer/hoje.txt (engine launched from installed engine\ dir) |
| Deployment | Installed app launches with --tray | `Start-Process installed Somning.App.exe --tray` | **PASS** | Process alive; Somning.Engine.exe started from installed engine\ directory |
| Deployment | Uninstall removes app, shortcut, Run key; keeps user data | `installed uninstall.ps1 -Quiet` | **PASS** | App dir, shortcut and Run value removed; sentinel file in %LOCALAPPDATA%\Somning\logs preserved |
| Paste | Notepad (Win32 Edit): Ctrl+V paste, text stays on clipboard | `Somning.Integration.Tests.exe -method *Pastes_into_notepad_and_keeps_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real Notepad received the text; clipboard retained [attempt 1: PASS] |
| Paste | Foreground window changed before paste: defers, no leakage | `Somning.Integration.Tests.exe -method *TargetPasterTests.Foreground_changed_defers_and_leaves_text_on_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. paste_deferred; text left on clipboard [attempt 1: PASS] |
| Paste | Focus moved to another field in same window: defers | `Somning.Integration.Tests.exe -method *TargetPasterTests.Focus_change_inside_same_top_level_window_defers*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. paste_deferred; no keystrokes injected [attempt 1: PASS] |
| Paste | Clipboard changed while waiting for modifiers: defers before injection | `Somning.Integration.Tests.exe -method *Clipboard_change_during_modifier_wait_defers_before_injection*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. No injection after external copy [attempt 1: PASS] |
| Paste | Clipboard-only mode (auto-paste off): never pastes | `Somning.Integration.Tests.exe -method *Copy_only_never_pastes*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Clipboard set; zero keystrokes injected [attempt 1: PASS] |
| Paste | Live app E2E: foreground change after capture defers | `Somning.Integration.Tests.exe -method *PasteDeferredE2ETests.Foreground_change_after_capture_defers_paste_and_keeps_transcript_on_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real app + engine + Notepad; outcome paste_deferred / foreground_changed [attempt 1: PASS] |
| Paste | Live app E2E: same-window focus change after capture defers | `Somning.Integration.Tests.exe -method *PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real app + engine + Notepad; paste_deferred [attempt 1: PASS] |
| Elevation | Process integrity level read (self vs. child Notepad, SYSTEM process) | `Somning.Integration.Tests.exe -class *IntegrityReaderTests*` | **PASS** | Total=2 Errors=0 Failed=0 Skipped=0. Integrity RID read for real processes; does NOT involve an elevated target window [attempt 1: PASS] |
| Elevation | Elevated target defers (PasteGate policy) | `Somning.Core.Tests.exe -method *PasteGateTests.Target_elevated_defers_target_elevated* -method *PasteGateTests.Integrity_unknown_defers_integrity_unknown*` | **PASS** | Total=2 Errors=0 Failed=0 Skipped=0. Policy unit tests only (decision logic), not empirical UIPI proof |
| Elevation | Elevated target window, real UIPI behaviour | `Somning.Integration.Tests.exe -method *Elevated_target_defers_paste_when_a_real_integrity_pair_is_available*` | **SKIPPED** | Total=1 Errors=0 Failed=0 Skipped=1 - test skipped, behaviour NOT verified |
| Terminal | Windows Terminal / console hosts (conhost, WT) | `none` | **MANUAL** | No automated test exists for Windows Terminal or console paste; requires a human check (Ctrl+V into Windows Terminal) |
| Resilience | Sleep/resume and lock/unlock messages raise events; hook reinstalled | `Somning.Integration.Tests.exe -class *PowerSessionIntegrationTests*` | **PASS** | Total=3 Errors=0 Failed=0 Skipped=0. WM_POWERBROADCAST / WM_WTSSESSION_CHANGE are POSTED SYNTHETICALLY to the listener window; hook reinstall verified |
| Resilience | Real machine sleep -> wake and real workstation lock -> unlock | `none` | **MANUAL** | Not automated: a real suspend/lock would interrupt the agent session; synthetic-message tests above are the only coverage |

## Known limits (not verified by this run)

- Windows Terminal / console paste: no automated test; verify manually.
- Real UIPI against an elevated window: the integration test is skipped (no low/high integrity pair is provisioned unattended); only the PasteGate decision policy is unit tested.
- Real sleep/resume and lock/unlock: only synthetic window messages are tested.
- Clean-machine install (no .NET SDK installed) was not performed; the app is self-contained and was run from an installed copy on this development machine.

*Generated by `tools/verify-matrix.ps1`.*
