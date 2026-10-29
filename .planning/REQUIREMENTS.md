# Requirements: Somning

**Defined:** 2026-10-22
**Core Value:** Press the hotkey, speak Portuguese, and accurate text lands in the focused field (and stays in the clipboard) — reliably, every time, in any app.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Hotkey (HOTK)

- [x] **HOTK-01**: User can start dictation from any app by pressing the global hotkey (default Ctrl+Shift+Space) without the key combo leaking into the focused app
- [x] **HOTK-02**: User can quick-tap the hotkey (< ~300 ms, configurable) to start recording and tap again to stop (toggle mode)
- [x] **HOTK-03**: User can hold the hotkey to record and release it to stop (push-to-talk mode); key auto-repeat is ignored
- [x] **HOTK-04**: User can press Esc while recording to cancel — nothing is transcribed or pasted
- [x] **HOTK-05**: User can change the hotkey in settings and gets a warning if it conflicts with a known shortcut (e.g. Ctrl+Shift layout toggle)

### Audio Capture (AUD)

- [x] **AUD-01**: User's speech is captured from the selected microphone (default device unless changed) and converted to 16 kHz mono
- [x] **AUD-02**: User can pick the input microphone in settings and the change applies without restart
- [x] **AUD-03**: User is told in the overlay when the mic is missing, blocked by Windows privacy settings, or delivering silence-only input
- [x] **AUD-04**: User's recording is persisted to disk before transcription and recovered after a crash/restart, so a recording is never silently lost

### Transcription (STT)

- [x] **STT-01**: User's speech is transcribed locally and offline in pt-BR using Whisper large-v3-turbo, with punctuation and capitalization
- [x] **STT-02**: Local engine uses the RX 7600 GPU (Vulkan) when available and automatically restarts in CPU mode if GPU mode fails, without crashing the app
- [x] **STT-03**: Accidental taps, very short (< ~0.4 s) or silence/noise-only recordings produce no pasted text (no Whisper hallucinations such as "Obrigado." / "Amara.org")
- [x] **STT-04**: Model is preloaded and warmed up at app start so the first dictation is not slowed by cold load; a hotkey press before ready is queued, not dropped
- [x] **STT-05**: User can choose a cloud engine (Groq whisper-large-v3-turbo or OpenAI gpt-4o-transcribe) by entering an API key; cloud is opt-in only
- [x] **STT-06**: Cloud failures (no network, invalid key, rate limit, timeout) show a clear message and keep the recording for retry; no silent fallback
- [x] **STT-07**: User can add custom vocabulary / initial prompt (names, technical terms) that biases transcription
- [x] **STT-08**: Typical 5–30 s utterance pastes within a few seconds of stopping on the owner's hardware (measured p50/p95 budget defined in Phase 1)

### Output (OUT)

- [x] **OUT-01**: Transcribed text is placed on the clipboard and remains there after dictation (no clipboard restore)
- [x] **OUT-02**: Transcribed text is auto-pasted (Ctrl+V) into the window that was focused when recording stopped, after the user physically releases the hotkey modifiers
- [x] **OUT-03**: If focus changed, the target is elevated (admin), or paste cannot be delivered, the text stays on the clipboard and the overlay says to paste manually
- [x] **OUT-04**: User can turn auto-paste off (clipboard-only mode)
- [x] **OUT-05**: User can re-paste/copy the last transcript from the tray

### Overlay (OVL)

- [x] **OVL-01**: While recording, a small pill appears at the bottom-center of the active monitor with a live animated waveform driven by mic level
- [x] **OVL-02**: The overlay never takes focus, is click-through, always on top, and is DPI-correct on any monitor
- [x] **OVL-03**: Overlay shows distinct states: listening, transcribing, error/notice (pt-BR text), and hides when done
- [x] **OVL-04**: User hears optional short start/stop sounds (toggle in settings)

### App Shell & Settings (APP)

- [x] **APP-01**: App runs in the system tray with a menu: Configurações, Colar último, Abrir logs, Sair
- [x] **APP-02**: Settings (hotkey, mic, engine, API key, auto-paste, sounds, vocabulary, tap threshold) persist across restarts; API key stored encrypted (DPAPI)
- [x] **APP-03**: Only one instance of the app can run at a time
- [x] **APP-04**: On first run the local model is downloaded with progress, resume and checksum verification; failure is reported and retryable
- [x] **APP-05**: All user-facing text is in pt-BR; language is stored as a config value for future expansion
- [x] **APP-06**: Every dictation logs timing metrics (capture, load, inference, paste) to a local log file, with API keys redacted

### Distribution (DIST)

- [ ] **DIST-01**: User can install Somning via a per-user Windows installer that works on a clean Windows 10 machine without dev tools
- [x] **DIST-02**: User can enable start-with-Windows (starts minimized to tray)
- [ ] **DIST-03**: App keeps working after sleep/resume, lock/unlock and long uptime (hook watchdog/reinstall)

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Languages

- **LANG-01**: User can choose other dictation languages (EN, ES…) or auto-detect

### Text processing

- **TXT-01**: Spoken punctuation commands ("vírgula", "ponto", "nova linha")
- **TXT-02**: Optional LLM post-processing (cleanup, formatting, tone)

### Output

- **OUT2-01**: Optional clipboard-restore mode
- **OUT2-02**: Per-app paste method (Ctrl+Shift+V / Shift+Insert for terminals)

### History

- **HIST-01**: Transcription history window with search and re-copy

## Out of Scope

| Feature | Reason |
|---------|--------|
| Live partial text on screen | Overlay shows waveform only; transcribe-on-stop gives best quality and simplicity |
| Clipboard restore in v1 | Owner wants text to remain in clipboard |
| Auto language detection in v1 | pt-BR fixed improves accuracy; owner asked pt-BR first |
| Always-on / wake-word listening | Privacy, CPU cost, false triggers |
| Typing via simulated keystrokes | Slow, breaks with accents/IME; clipboard paste is reliable |
| Telemetry / auto-update service | Personal tool; unnecessary complexity |
| macOS / Linux | Owner uses Windows 10 only |
| Pasting into elevated (admin) windows | Blocked by Windows UIPI for non-elevated apps; handled by clipboard-only notice |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| HOTK-01 | Phase 1 | Complete |
| HOTK-02 | Phase 1 | Complete |
| HOTK-03 | Phase 1 | Complete |
| HOTK-04 | Phase 2 | Complete |
| HOTK-05 | Phase 2 | Complete |
| AUD-01 | Phase 1 | Complete |
| AUD-02 | Phase 2 | Complete |
| AUD-03 | Phase 2 | Complete |
| AUD-04 | Phase 1 | Complete |
| STT-01 | Phase 1 | Complete |
| STT-02 | Phase 1 | Complete |
| STT-03 | Phase 1 | Complete |
| STT-04 | Phase 1 | Complete |
| STT-05 | Phase 3 | Complete |
| STT-06 | Phase 3 | Complete |
| STT-07 | Phase 2 | Complete |
| STT-08 | Phase 1 | Complete |
| OUT-01 | Phase 1 | Complete |
| OUT-02 | Phase 1 | Complete |
| OUT-03 | Phase 1 | Complete |
| OUT-04 | Phase 2 | Complete |
| OUT-05 | Phase 2 | Complete |
| OVL-01 | Phase 2 | Complete |
| OVL-02 | Phase 2 | Complete |
| OVL-03 | Phase 2 | Complete |
| OVL-04 | Phase 2 | Complete |
| APP-01 | Phase 2 | Complete |
| APP-02 | Phase 2 | Complete |
| APP-03 | Phase 2 | Complete |
| APP-04 | Phase 2 | Complete |
| APP-05 | Phase 2 | Complete |
| APP-06 | Phase 1 | Complete |
| DIST-01 | Phase 4 | Pending |
| DIST-02 | Phase 4 | Complete |
| DIST-03 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 35 total
- Mapped to phases: 35
- Unmapped: 0 ✓

---
*Requirements defined: 2026-10-22*
*Last updated: 2026-10-23 after Phase 2 completion*
