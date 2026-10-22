# Roadmap: Somning

## Overview

Somning goes from a headless, agent-verifiable dictation loop (hotkey, mic, local Whisper pt-BR, clipboard and paste) to a daily-usable shell (overlay, tray, settings, cancel), then an opt-in cloud engine, then packaging and long-uptime hardening. Risk is retired first: Phase 1 opens with the Windows 10 published-build Whisper.net inference gate and a Vulkan/CPU benchmark. All verification is agent-runnable (unit tests, WAV fixtures, scripted E2E into Notepad, checks against published builds).

## Phases

- [ ] **Phase 1: Dictation Loop MVP** - Headless end-to-end: hotkey to mic to local Whisper pt-BR to clipboard and auto-paste, gated by a Win10 published-build inference benchmark
- [ ] **Phase 2: Overlay, Tray, Settings & Cancel** - Daily-usable shell: waveform pill, tray, persisted settings, Esc cancel, first-run model download
- [ ] **Phase 3: Cloud Engine** - Opt-in Groq/OpenAI transcription with explicit errors and recoverable recordings
- [ ] **Phase 4: Packaging, Autostart & Hardening** - Per-user installer, start-with-Windows, sleep/resume and soak resilience

## Phase Details

### Phase 1: Dictation Loop MVP
**Goal**: A scripted hotkey press, speech and release deliver accurate pt-BR text to the clipboard and the focused field, using the local engine, with no UI beyond logs
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: HOTK-01, HOTK-02, HOTK-03, AUD-01, AUD-04, STT-01, STT-02, STT-03, STT-04, STT-08, OUT-01, OUT-02, OUT-03, APP-06
**Success Criteria** (what must be TRUE):
  1. First task gate passes: a published (no dev SDK) build runs Whisper.net large-v3-turbo inference on Windows 10 19045 for a pt-BR WAV fixture, and a benchmark report (Vulkan vs CPU, f16/q8/q5, p50/p95 stop-to-paste) fixes the default engine settings and the STT-08 latency budget
  2. A scripted E2E injects the hotkey (tap-tap toggle and hold-release), plays a pt-BR fixture, and the expected text appears in Notepad (read via WM_GETTEXT/UIA) and on the clipboard, with no hotkey characters leaking into Notepad
  3. Silence-only, noise-only, and very short (< ~0.4 s) fixtures paste nothing and never produce hallucinations such as "Obrigado." or "Amara.org"
  4. Killing the engine worker or forcing GPU failure mid-run restarts it in CPU mode without crashing the app; a recording interrupted by a crash is recovered from its spool WAV on restart
  5. Pasting while the target window changed or is elevated leaves the text on the clipboard and logs a manual-paste outcome; every dictation logs capture, load, inference and paste timings with API keys redacted
**Plans:** 8 plans

Plans:
- [ ] 01-01-PLAN.md — Walking skeleton: solution + engine worker + WAV→clipboard tracer; Win10 published-build gate; model fetch (wave 1)
- [ ] 01-02-PLAN.md — Global hotkey: LL hook chord suppression + TriggerInterpreter tap/hold (wave 2)
- [ ] 01-03-PLAN.md — Audio: WASAPI/file capture → 16 kHz spool + sidecar, crash durability, recovery rules (wave 2)
- [ ] 01-04-PLAN.md — Output: clipboard + target-aware Ctrl+V, PasteGate/UIPI deferral (wave 2)
- [ ] 01-05-PLAN.md — Quality: pt-BR fixtures, VAD/hallucination gate, accuracy, benchmark → BENCHMARK.md defaults (wave 2)
- [ ] 01-06-PLAN.md — Engine resilience: restart-in-CPU + retry, restart cap, queue-before-ready (wave 2)
- [ ] 01-07-PLAN.md — Dictation loop: reducer + App wiring, metrics log line, startup recovery, harness pipe (wave 3)
- [ ] 01-08-PLAN.md — Phase acceptance: Notepad E2E for SC2–SC5, published App re-check, verify-phase1.ps1 (wave 4)

### Phase 2: Overlay, Tray, Settings & Cancel
**Goal**: User can live with Somning day to day: a visible non-intrusive pill, tray control, persisted settings, cancel, and a first-run model download
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: HOTK-04, HOTK-05, AUD-02, AUD-03, STT-07, OUT-04, OUT-05, OVL-01, OVL-02, OVL-03, OVL-04, APP-01, APP-02, APP-03, APP-04, APP-05
**Success Criteria** (what must be TRUE):
  1. While recording, a pill with a live mic-driven waveform appears bottom-center of the active monitor; automated checks assert window styles (no-activate, topmost, click-through), that GetForegroundWindow is unchanged, and screenshot pixels at 100% and 150% DPI
  2. The overlay shows distinct listening, transcribing and pt-BR error/notice states (missing mic, privacy-blocked, silence-only, nothing detected, model missing) and hides when done
  3. Pressing Esc while recording cancels it and nothing is transcribed or pasted; the tray menu offers Configurações, Colar último, Abrir logs and Sair, and re-pasting the last transcript works
  4. Settings (hotkey with conflict warning, mic applied without restart, auto-paste off giving clipboard-only, sounds, vocabulary prompt, tap threshold) round-trip across restarts, survive corrupted files, and the API key is stored DPAPI-encrypted; a second instance exits immediately
  5. First run with no model downloads it with progress, resume and SHA-256 verification against a local test server, reporting retryable failures
**Plans**: TBD
**UI hint**: yes

### Phase 3: Cloud Engine
**Goal**: User can opt into cloud transcription by entering a Groq or OpenAI key, with clear failures and no lost recordings
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: STT-05, STT-06
**Success Criteria** (what must be TRUE):
  1. With a key entered and cloud selected, the same pt-BR fixture suite passes against a mock HTTP server for both providers; real-API runs execute only when a key env var is present (skipped, never failed)
  2. No network, invalid key, rate limit and timeout each show a distinct pt-BR overlay message, the recording is kept for retry, and no silent fallback to local occurs
  3. Cloud is never used unless the user selected it and entered a key; logs and error output contain no API keys

**Plans**: TBD
**UI hint**: yes

### Phase 4: Packaging, Autostart & Hardening
**Goal**: User installs Somning on a clean Windows 10 machine and it keeps working across restarts, sleep and long uptime
**Mode:** mvp
**Depends on**: Phase 3
**Requirements**: DIST-01, DIST-02, DIST-03
**Success Criteria** (what must be TRUE):
  1. A per-user installer runs unattended on a clean Windows 10 profile (including non-ASCII and space paths) and the installed binary passes the Phase 1 E2E into Notepad
  2. Enabling start-with-Windows registers the HKCU Run entry and the app starts minimized to tray on logon
  3. After simulated sleep/resume and lock/unlock, and a scripted soak of many dictations, the hotkey hook still works (watchdog reinstall) and a paste-matrix report (Notepad, Chrome/Edge, VS Code, Windows Terminal, Word, elevated Notepad notice) is produced
**Plans**: TBD

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Dictation Loop MVP | 0/TBD | Not started | - |
| 2. Overlay, Tray, Settings & Cancel | 0/TBD | Not started | - |
| 3. Cloud Engine | 0/TBD | Not started | - |
| 4. Packaging, Autostart & Hardening | 0/TBD | Not started | - |
