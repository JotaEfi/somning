# Plan 04-01: Start-with-Windows Autostart, Registry HKCU & Tray/Settings Toggle Summary

**Phase**: 04 - Packaging, Autostart & Hardening  
**Wave**: 1  
**Status**: Completed  
**Requirements Satisfied**: DIST-02, APP-01, APP-02, APP-05  

## Achievements
1. **Core Settings & Abstraction (`Somning.Core.Settings`)**:
   - `UserSettings`: Added `StartWithWindows` boolean property (default `false`), preserved across restarts and backwards compatible with legacy JSON configs.
   - `IAutostartManager`: Contract defining `IsEnabled()` and `SetEnabled(bool enabled, string? executablePath = null, string arguments = "--tray")`.
   - `SettingsViewModel`: Propagates `StartWithWindows` with change tracking.
2. **Platform Registry Autostart Manager (`Somning.Platform.Windows.Autostart`)**:
   - `RegistryAutostartManager`: Interacts with `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` without requiring admin rights.
   - Formats quoted commands `"{executablePath}" --tray` supporting spaces and non-ASCII Unicode paths cleanly.
   - Idempotent delete on disable; isolated constructor for testing.
3. **App Startup CLI Flags (`Program.cs`)**:
   - Filters `--tray` and `--minimized` arguments so that autostart launches start minimized directly into the system tray without returning unknown-argument exit codes.
4. **Settings Dialog pt-BR UI (`SettingsWindowManager`)**:
   - Added native Win32 checkbox `"Iniciar com o Windows"` (`IdStartWithWindowsCheck = 1009`).
   - Sourced from `settings.StartWithWindows` on load, saved to `UserSettings` on OK.
   - Synchronizes live registry key on save only when the state diverges (`ApplySettings`), avoiding unnecessary registry writes.
5. **Verification**:
   - `Somning.Core.Tests`: 296/296 passed.
   - `Somning.Integration.Tests` (Autostart & Settings): 10/10 passed.
   - Solution builds with 0 warnings and 0 errors.
