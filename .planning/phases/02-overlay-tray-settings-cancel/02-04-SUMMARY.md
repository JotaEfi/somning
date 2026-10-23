# Plan 02-04 Summary: Overlay Pill Window & Live Waveform

## Requirements Covered
- **OVL-01**: Floating pill overlay positioned bottom-center of active monitor.
- **OVL-02**: Non-intrusive window styles (`WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_LAYERED`). Automated tests verify all window styles, verify `GetForegroundWindow` is unchanged during all state transitions, and test DPI rendering at 100% and 150% scale.
- **OVL-03**: Distinct visual states in pt-BR:
  - *Ouvindo...* (Listening with live waveform bars)
  - *Transcrevendo...* (Transcribing with gentle indicator)
  - *Avisos/Erros* (Microfone não encontrado, Microfone bloqueado, Apenas silêncio detectado, Nenhuma fala detectada, Modelo de transcrição ausente)
  - *Hidden* when idle.
- **AUD-03**: Live audio level (RMS and peak) computation from 16-bit PCM audio buffer (`AudioLevelCalculator`, `AudioLevelTracker`) fed into overlay waveform bars.

## Work Delivered

### Task 1: Core Audio Level Tracker & Overlay State Machine (OpenCode - DeepSeek V4.1 Flash Free)
- `Somning.Core/Overlay/AudioLevelCalculator.cs`: pure, normalized [0.0 .. 1.0] RMS and peak amplitude calculation over `ReadOnlySpan<byte>` and `ReadOnlySpan<short>`.
- `Somning.Core/Overlay/AudioLevelTracker.cs`: attack/decay smoothing (attack 0.6, decay 0.12) and 5-bar harmonic waveform height generator with 0.15 baseline.
- `Somning.Core/Overlay/OverlayState.cs` & `OverlayViewModel.cs`: immutable state model with complete pt-BR string table (APP-05).
- 60 unit tests in `Somning.Core.Tests/Overlay/`.

### Task 2 & 3: Win32 Layered Window & Integration (Antigravity)
- `Somning.Platform.Windows/Overlay/IOverlayView.cs` & `OverlayWindowManager.cs`:
  - Dedicated STA thread with Win32 message pump.
  - Native layered window with styles:
    - `WS_EX_TOPMOST` (always on top)
    - `WS_EX_TOOLWINDOW` (hidden from taskbar and Alt-Tab)
    - `WS_EX_TRANSPARENT` (click-through to underlying apps)
    - `WS_EX_NOACTIVATE` (never takes keyboard or mouse focus)
    - `WS_EX_LAYERED` (per-pixel alpha compositing via `UpdateLayeredWindow`)
  - Direct 32-bit BGRA buffer rendering:
    - Dark pill background `#18181B` (alpha ~230) with anti-aliased border `#3F3F46`.
    - Status indicator dot (red for listening `#EF4444`, cyan for transcribing `#38BDF8`, amber for notices `#F59E0B`).
    - Dynamic waveform bars rendered from live audio level.
    - Crisp pt-BR label with Segoe UI and alpha fixup.
  - Positioned bottom-center of active monitor work area (`MonitorFromWindow`), scaled with Per-Monitor DPI.
- `Somning.App/Composition/AppComposition.cs`:
  - Added `Overlay`, `AudioLevelTracker`, `EnableOverlay()`.
  - Wired live audio feed through `OverlayAudioSink` so recording chunks automatically update the overlay waveform in real time.
- Automated tests:
  - `OverlayWindowManagerTests`: asserts all 5 window styles, tests that `GetForegroundWindow` is unchanged across all states, asserts pt-BR state transitions, and tests pixel rendering at 100% (220x44) and 150% (330x66) DPI.
  - `OverlayAppIntegrationTests`: tests complete pipeline integration with `AppComposition`.

## Verification Results
- `Somning.Core.Tests`: 196/196 PASS (0 failures, 0 warnings).
- `Somning.Integration.Tests`: 13/13 Phase 2 tests PASS (0 failures, 0 warnings).
- Clean compilation across all 8 projects in `Somning.sln`.
