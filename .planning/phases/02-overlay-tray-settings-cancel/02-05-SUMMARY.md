# Plan 02-05 Summary: Settings Dialog & Dynamic Audio Device Switching

## Requirements Covered
- **APP-01**: User-facing Settings dialog opened from tray context menu ("Configurações...").
- **AUD-02**: Microphone selection applied without application restart:
  - `AudioDeviceEnumerator` lists active WASAPI endpoints with friendly names and default detection.
  - `DynamicAudioSourceFactory` safely updates `CurrentDeviceId` so subsequent captures use the new device without restarting the app.
- **OUT-04**: Auto-paste toggle setting:
  - When AutoPaste is false, text delivery executes the clipboard-only (`CopyOnly`) path and suppresses `Ctrl+V`.
- **OVL-04**: Sound cues toggle (`PlaySoundCues`):
  - `SoundCuePlayer` / `ISoundCuePlayer` plays distinct audible cues on capture start, stop, and cancel without blocking audio or UI.
- **APP-05**: pt-BR localized labels and conflict validation messages.

## Work Delivered

### Task 1: Core Settings Integration & Dynamic Audio Device Provider (OpenCode)
- `Somning.Platform.Windows/Audio/AudioDeviceEnumerator.cs`: queries `MMDeviceEnumerator` for active capture devices, identifies default endpoint, and handles COM/platform exceptions safely.
- `Somning.Platform.Windows/Audio/WasapiAudioSource.cs`: added constructor accepting optional `deviceId` chained into `WasapiRecorderBuilder.WithDevice()`.
- `Somning.Platform.Windows/Audio/DynamicAudioSourceFactory.cs`: thread-safe dynamic audio factory for zero-restart device swapping.
- `Somning.Platform.Windows/Audio/SoundCuePlayer.cs`: background task audio beeps for capture lifecycle feedback.
- 9 unit/integration tests in `Somning.Integration.Tests/Audio/`.

### Task 2: Win32/STA Native Settings Dialog (Antigravity)
- `Somning.Core/Settings/SettingsViewModel.cs`: pure model supporting device selection, hotkey conflict validation (`HotkeyConflictValidator`), auto-paste, sound cues, custom vocabulary, and DPAPI protected API key.
- 3 unit tests in `Somning.Core.Tests/Settings/SettingsViewModelTests.cs`.
- `Somning.Platform.Windows/Settings/ISettingsWindow.cs` & `SettingsWindowManager.cs`:
  - STA native window hosting Win32 controls (Device ComboBox, Hotkey label, AutoPaste Checkbox, SoundCues Checkbox, Vocabulary Edit, Cloud Provider ComboBox, API key masked Edit, Save and Cancel buttons).
  - Single-instance activation: brings existing window to front via `SetForegroundWindow` if already open.
  - Applies Segoe UI font and pt-BR text table.
  - Encrypts cloud API key with Windows DPAPI on save.

### Task 3: App Wiring & End-to-End Tests (Antigravity)
- `Somning.App/Composition/AppComposition.cs`:
  - Loaded settings from `JsonSettingsStore` on startup.
  - Wired `EffectRunner` with `DynamicAudioFactory`, `AutoPasteEnabled`, and `SoundPlayer`.
  - Wired `TrayIconManager.OpenSettingsRequested` to `OpenSettings()`.
  - Propagates dynamic device changes immediately to `DynamicAudioFactory`.
- `Somning.Integration.Tests/Settings/SettingsIntegrationTests.cs`:
  - Verified dynamic microphone switching without restart (AUD-02).
  - Verified AutoPaste toggle switching delivery to clipboard-only (OUT-04).
  - Verified sound cues toggle (OVL-04).
  - Verified settings window lifecycle.

## Verification
- `Somning.Core.Tests`: 199/199 PASS (0 failures, 0 warnings).
- `Somning.Integration.Tests`: all Phase 2 tests PASS.
