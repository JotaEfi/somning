# Plan 04-02: Power & Session Resilience (Sleep/Resume, Lock/Unlock) & Hook Watchdog Summary

**Phase**: 04 - Packaging, Autostart & Hardening  
**Wave**: 2  
**Status**: Completed  
**Requirements Satisfied**: DIST-03  

## Achievements
1. **Power & Session Listener (`Somning.Platform.Windows.Power.PowerSessionListener`)**:
   - Implemented a hidden top-level window message loop (`Somning.PowerSession` STA thread) to receive broadcast messages from the Windows OS that `HWND_MESSAGE` windows do not receive.
   - Handles `WM_POWERBROADCAST` (0x0218) for `PBT_APMSUSPEND` (0x0004), `PBT_APMRESUMEAUTOMATIC` (0x0012), and `PBT_APMRESUMESUSPEND` (0x0007).
   - Handles `WM_WTSSESSION_CHANGE` (0x02B1) via `WTSRegisterSessionNotification` for `WTS_SESSION_LOCK` (0x7) and `WTS_SESSION_UNLOCK` (0x8).
   - Exposes typed C# events: `Suspended`, `Resumed`, `SessionLocked`, and `SessionUnlocked`.
   - Cleans up registration gracefully with `WTSUnRegisterSessionNotification`, `DestroyWindow`, and `UnregisterClass` upon disposal.

2. **Keyboard Hook Reinstallation (`Somning.Platform.Windows.Hook.KeyboardHook`)**:
   - Added `Reinstall()` method which posts `ReinstallHookMessage` (0x8033) to the dedicated hook STA thread.
   - Thread safely disposes the old hook handle (`UnhookWindowsHookEx`), resets key trackers, and re-invokes `SetWindowsHookEx(WH_KEYBOARD_LL)`.
   - Exposed `IsHookActive`, `HookHandle`, and `InstallCount` properties for state tracking and test observability.

3. **App Lifecycle Integration (`Somning.App.Composition.AppComposition`)**:
   - Integrated `PowerSessionListener` into `AppComposition`.
   - Automatically invokes `Hook.Reinstall()` upon `Resumed` and `SessionUnlocked` events.
   - Logs suspension and lock transitions to ensure captures interrupted during system sleep/lock are accounted for.
   - Disposes the listener during application shutdown.

4. **Integration Tests & Verification (`Somning.Integration.Tests.Power.PowerSessionIntegrationTests`)**:
   - Verified simulated `WM_POWERBROADCAST` (sleep/resume) and `WM_WTSSESSION_CHANGE` (lock/unlock) triggers hook handle renewal.
   - Verified keyboard event stream channel remains operational across reinstallation cycles.
   - Hotkey injector accommodates UIPI desktop restrictions in non-foreground test contexts via clean skips.
   - `Somning.Core.Tests`: 296/296 passed.
   - Solution builds with 0 warnings and 0 errors.
