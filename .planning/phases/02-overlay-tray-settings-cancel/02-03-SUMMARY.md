# Plan 02-03 Summary: System Tray Menu & Last Transcript Repaste

## Implementation Summary
- **ITranscriptHistory & TranscriptHistory**: Added thread-safe in-memory transcript history tracking `Record(text)`, `LastTranscript`, and `HasTranscript`. Added unit tests in `Somning.Core.Tests`.
- **Win32 Native Tray Icon & Menu**:
  - Implemented `TrayIconManager` in `Somning.Platform.Windows.Tray` hosted on a dedicated STA thread with a message pump and hidden Win32 HWND.
  - Registers `Shell_NotifyIconW` with tooltip "Somning".
  - Intercepts `WM_TRAYICON` context menu clicks (`WM_RBUTTONUP` / `WM_CONTEXTMENU`) to show a native Win32 popup menu (`CreatePopupMenu`, `AppendMenuW`, `TrackPopupMenuEx`).
  - Menu options fully localized in pt-BR (APP-05):
    - "Configurações..."
    - "Colar último" (enabled only when a transcript has been recorded)
    - "Abrir pasta de logs"
    - Separator
    - "Sair"
- **App Wiring & Integration**:
  - Connected `EffectRunner` to record transcripts upon `Deliver` and `CopyOnly`.
  - Added `EnableTray()` to `AppComposition` mapping tray actions:
    - "Colar último" -> calls `RepasteLastAsync()`, placing the text into clipboard and safely pasting via `TargetPaster` / `PasteGate`.
    - "Abrir pasta de logs" -> opens `SomningPaths.LogsDir` in Explorer.
    - "Sair" -> invokes clean application shutdown.
- **Verification**:
  - `Somning.Core.Tests`: 136/136 tests passing (including 9/9 `TranscriptHistoryTests`).
  - `Somning.Integration.Tests`: `TrayIconManagerTests` (2/2 passing) and `RepasteLastTranscriptTests` (2/2 passing).
  - Total Phase 2 integration suite: 11/11 passing.
