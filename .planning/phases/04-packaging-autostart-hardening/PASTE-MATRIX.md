# Somning Paste Matrix & Deployment Hardening Report

**Date**: 2026-10-29 14:46:11 UTC  
**Environment**: Windows 10 Pro (10.0.19045), win-x64  
**Overall**: FAIL  
**Counts**: PASS=23 FAIL=1 SKIPPED=3 MANUAL=4  

Requirements: **DIST-01** (per-user unattended install; installed binary passes the Phase 1 E2E), **DIST-02** (retained) and **DIST-03** (soak, hook watchdog, paste matrix incl. Chrome/Edge/VS Code/Word, elevated-target handling, sleep/lock resilience).

**Attempts per row**: 1 (no retries; first-run results). Soak iterations requested: 50.

Every row below was produced by an executed check in `tools/verify-matrix.ps1`. Status meanings: PASS = executed and passed; FAIL = executed and failed; SKIPPED = test exists but was skipped (behaviour not verified); MANUAL = no automated check exists, a human must verify.

| Category | Scenario | Check | Status | Evidence |
|----------|----------|-------|:------:|----------|
| Deployment | Unattended install, space + non-ASCII path | `tools/install.ps1 -Unattended` | **PASS** | App, engine, uninstall.ps1 and Start Menu shortcut present under 'C:\Users\Administrator\AppData\Local\Temp\Somning Teste 測試 ação ae4c936ede854594be1ccd858a947998\app' |
| Deployment | Start Menu shortcut target | `IShellLinkW.GetPath (Unicode)` | **PASS** | Shortcut points to the installed Somning.App.exe |
| Deployment | Autostart Run key (install -StartWithWindows) | `HKCU Run value` | **PASS** | Quoted exe path with --tray written |
| Deployment | Installed self-contained app transcribes | `installed Somning.App.exe --transcribe-file hoje.wav --backend cpu` | **PASS** | Exit 0; clipboard text equals tests/fixtures/tracer/hoje.txt (engine launched from installed engine\ dir) |
| Deployment | Installed app launches with --tray | `Start-Process installed Somning.App.exe --tray` | **PASS** | Process alive; Somning.Engine.exe started from installed engine\ directory |
| Deployment | Uninstall removes app, shortcut, Run key; keeps user data | `installed uninstall.ps1 -Quiet` | **PASS** | App dir, shortcut and Run value removed; sentinel file in %LOCALAPPDATA%\Somning\logs preserved |
| Deployment | Installed binary passes Phase 1 E2E into Notepad (tap-tap) | `Somning.Integration.Tests.exe *DictationE2ETests.TapTap_cmd_curto_delivers_to_notepad_without_layout_or_hotkey_leaks* (SOMNING_APP_EXE=installed)` | **PASS** | Real Notepad received the dictated text using installed app 'C:\Users\Administrator\AppData\Local\Temp\Somning Harness 測試 ação 3578957448494e248c3229e5f525521c\app\Somning.App.exe' [installed path: C:\Users\Administrator\AppData\Local\Temp\Somning Harness 測試 ação 3578957448494e248c3229e5f525521c\app\Somning.App.exe] |
| Deployment | Installed binary passes Phase 1 E2E into Notepad (hold-release) | `Somning.Integration.Tests.exe *DictationE2ETests.HoldRelease_longo_numeros_requires_full_hold_and_delivers* (SOMNING_APP_EXE=installed)` | **PASS** | Real Notepad received the dictated text using installed app 'C:\Users\Administrator\AppData\Local\Temp\Somning Harness 測試 ação 3578957448494e248c3229e5f525521c\app\Somning.App.exe' [installed path: C:\Users\Administrator\AppData\Local\Temp\Somning Harness 測試 ação 3578957448494e248c3229e5f525521c\app\Somning.App.exe] |
| Paste | Notepad (Win32 Edit): Ctrl+V paste, text stays on clipboard | `Somning.Integration.Tests.exe -method *Pastes_into_notepad_and_keeps_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real Notepad received the text; clipboard retained |
| Paste | Foreground window changed before paste: defers, no leakage | `Somning.Integration.Tests.exe -method *TargetPasterTests.Foreground_changed_defers_and_leaves_text_on_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. paste_deferred; text left on clipboard |
| Paste | Focus moved to another field in same window: defers | `Somning.Integration.Tests.exe -method *TargetPasterTests.Focus_change_inside_same_top_level_window_defers*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. paste_deferred; no keystrokes injected |
| Paste | Clipboard changed while waiting for modifiers: defers before injection | `Somning.Integration.Tests.exe -method *Clipboard_change_during_modifier_wait_defers_before_injection*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. No injection after external copy |
| Paste | Clipboard-only mode (auto-paste off): never pastes | `Somning.Integration.Tests.exe -method *Copy_only_never_pastes*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Clipboard set; zero keystrokes injected |
| Paste | Live app E2E: foreground change after capture defers | `Somning.Integration.Tests.exe -method *PasteDeferredE2ETests.Foreground_change_after_capture_defers_paste_and_keeps_transcript_on_clipboard*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real app + engine + Notepad; outcome paste_deferred / foreground_changed |
| Paste | Live app E2E: same-window focus change after capture defers | `Somning.Integration.Tests.exe -method *PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste*` | **PASS** | Total=1 Errors=0 Failed=0 Skipped=0. Real app + engine + Notepad; paste_deferred |
| Elevation | Process integrity level read (self vs. child Notepad, SYSTEM process) | `Somning.Integration.Tests.exe -class *IntegrityReaderTests*` | **PASS** | Total=2 Errors=0 Failed=0 Skipped=0. Integrity RID read for real processes; does NOT involve an elevated target window |
| Elevation | Elevated target defers (PasteGate policy) | `Somning.Core.Tests.exe -method *PasteGateTests.Target_elevated_defers_target_elevated* -method *PasteGateTests.Integrity_unknown_defers_integrity_unknown*` | **PASS** | Total=2 Errors=0 Failed=0 Skipped=0. Policy unit tests only (decision logic), not empirical UIPI proof |
| Elevation | Elevated target window, real UIPI behaviour | `Somning.Integration.Tests.exe -method *Elevated_target_defers_paste_when_a_real_integrity_pair_is_available*` | **SKIPPED** | Total=1 Errors=0 Failed=0 Skipped=1 - test skipped, behaviour NOT verified |
| Terminal | Windows Terminal / console hosts (conhost, WT) | `none` | **MANUAL** | No automated test exists for Windows Terminal or console paste; see MANUAL-CHECKLIST.md item 1 |
| Resilience | Sleep/resume and lock/unlock messages raise events; hook reinstalled | `Somning.Integration.Tests.exe -class *PowerSessionIntegrationTests*` | **PASS** | Total=3 Errors=0 Failed=0 Skipped=0. WM_POWERBROADCAST / WM_WTSSESSION_CHANGE are POSTED SYNTHETICALLY to the listener window; hook reinstall verified |
| Resilience | Real machine sleep -> wake and real workstation lock -> unlock | `none` | **MANUAL** | Not automated: a real suspend/lock would interrupt the agent session; see MANUAL-CHECKLIST.md items 3 and 4 |
| Elevation | Real UIPI against an elevated target window | `none` | **MANUAL** | No low/high integrity pair is provisioned unattended; see MANUAL-CHECKLIST.md item 2 |
| Deployment | Clean-machine install without .NET SDK, and logon autostart after reboot | `none` | **MANUAL** | Needs a clean profile/VM and a reboot; see MANUAL-CHECKLIST.md items 5 and 6 |
| Watchdog | Watchdog: silently removed keyboard hook is reinstalled and probe is seen again | `Somning.Integration.Tests.exe -class *HookWatchdogLivenessTests* :: Silently_removed_hook_is_reinstalled_and_probe_is_seen_again` | **PASS** | Real hook silently removed, watchdog reinstalled it, F24 probe seen again |
| Watchdog | Watchdog: blocked probe injection is inconclusive and causes no reinstall | `Somning.Integration.Tests.exe -class *HookWatchdogLivenessTests* :: Blocked_injection_is_inconclusive_and_causes_no_reinstall` | **SKIPPED** | skipped, behaviour NOT verified: SendInput can only be blocked when an elevated window is foreground; this session cannot demonstrate a blocked state |
| Watchdog | Watchdog: headless fake probe loop reinstalls only after threshold | `Somning.Integration.Tests.exe -class *HookWatchdogLivenessTests* :: Fake_probe_loop_reinstalls_only_after_threshold` | **PASS** | Headless fake loop honoured the reinstall threshold |
| Paste | Application target: Chrome | `PasteTargetMatrixTests.Pastes_into_target_application(chrome)` | **PASS** | Chrome received the pasted sentinel text via Ctrl+V and clipboard read-back matched |
| Paste | Application target: Edge | `PasteTargetMatrixTests.Pastes_into_target_application(edge)` | **SKIPPED** | not installed on this machine |
| Paste | Application target: VS Code | `PasteTargetMatrixTests.Pastes_into_target_application(vscode)` | **PASS** | VS Code received the pasted sentinel text via Ctrl+V and clipboard read-back matched |
| Paste | Application target: Word | `PasteTargetMatrixTests.Pastes_into_target_application(word)` | **FAIL** | failed: Assert.Equal() Failure: Strings differ            ↓ (pos 0) Expected: \"Olá, ação às 10h: coração, não é São João.\" Actual:   \"SOMNING-SENTINEL-NAO-COLAR\"            ↑ (pos 0) |
| Soak | Soak: 50 dictations + real-hotkey dictation, no leaks or failures | `SoakTests.Fifty_dictations_then_real_hotkey_dictation_stay_healthy; SOAK-REPORT.json` | **PASS** | requested=50 completed=50 failures=0 handleDelta=26 privateByteDelta=12746752 hookInstalls=1->1 watchdogReinstalls=0 finalRealHotkeyOk=True result=PASS |

## Known limits (not verified by this run)

Items below remain MANUAL (see `MANUAL-CHECKLIST.md`); SKIPPED rows above (for example an application that is not installed) are also NOT verified.
- Windows Terminal / console paste: no automated test (checklist item 1).
- Real UIPI against an elevated window: only the PasteGate decision policy is unit tested (checklist item 2).
- Real sleep/resume and lock/unlock: only synthetic window messages are tested (checklist items 3 and 4).
- Clean-machine install without the .NET SDK and logon autostart after reboot (checklist items 5 and 6).

*Generated by `tools/verify-matrix.ps1`.*
