# Plan 02-01 Summary: Single Instance Mutex & Settings Core with DPAPI

**Phase**: 02 - Overlay, Tray, Settings & Cancel
**Wave**: 1
**Status**: Complete (PASS)
**Requirements Delivered**: APP-02, APP-03, APP-05, OUT-04, HOTK-05, STT-07, OVL-04

## Delivered Scope & Key Changes
1. **Single Instance Mutex (APP-03)**:
   - Implemented `SingleInstanceMutex` in `Somning.Platform.Windows.Processes` using a session-named Mutex (`Local\Somning_SingleInstance_App_Mutex`).
   - Wired into `Somning.App/Program.cs` before loop startup. A secondary instance exits immediately with return code 0 without popping windows or interfering with the primary app.
   - Verified by integration test `SingleInstanceAppTests.SecondInstance_WhenFirstInstanceRunning_ExitsImmediatelyWithZero`.
2. **Immutable UserSettings Domain Model (APP-02, APP-05, OUT-04, HOTK-05, STT-07, OVL-04)**:
   - Defined `KeyModifiers` ([Flags]), `HotkeyBinding` (default `Control | Shift + Space`) and `UserSettings` sealed record in `Somning.Core.Settings`.
   - Complete defaults aligned with roadmap requirements: `EngineBackend = "vulkan"`, `CloudProvider = "none"`, `AutoPaste = true`, `PlaySoundCues = false`, `TapThresholdMs = 300`, `Language = "pt-BR"`.
3. **Atomic Settings Persistence & Resilient Recovery (APP-02)**:
   - Implemented `ISettingsStore` and `JsonSettingsStore` targeting `%APPDATA%\Somning\settings.json`.
   - Atomic writes using temporary file (`.tmp`) followed by atomic replace (`File.Move(..., overwrite: true)`).
   - Self-healing recovery: corrupted JSON files are automatically archived as `settings.json.corrupt.<timestamp>` and safe defaults are restored without crash.
4. **Secret Protection via Windows DPAPI (APP-02)**:
   - Implemented `ISettingsProtector` and `DpapiSettingsProtector` in `Somning.Platform.Windows.Settings` using Windows Data Protection API (`ProtectedData.Protect` / `Unprotect` with `DataProtectionScope.CurrentUser` and domain entropy).

## Verification Evidence
- `Somning.Core.Tests`: 104/104 PASS (4 new Settings tests: defaults, round-trip, corrupt recovery, and missing file).
- `Somning.Integration.Tests`: 4/4 PASS on DPAPI encryption round-trip and Mutex lifecycle.
- `SingleInstanceAppTests`: 1/1 PASS on real `Somning.App.exe` secondary process execution exiting immediately with code 0.
- Zero compiler warnings, zero errors.
