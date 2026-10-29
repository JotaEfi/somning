# Roadmap: Somning

## Overview

Somning goes from a headless, agent-verifiable dictation loop (hotkey, mic, local Whisper pt-BR, clipboard and paste) to a daily-usable shell (overlay, tray, settings, cancel), then an opt-in cloud engine, then packaging and long-uptime hardening. Risk is retired first: Phase 1 opens with the Windows 10 published-build Whisper.net inference gate and a Vulkan/CPU benchmark. All verification is agent-runnable (unit tests, WAV fixtures, scripted E2E into Notepad, checks against published builds).

## Phases

- [x] **Phase 1: Dictation Loop MVP** - Headless end-to-end: hotkey to mic to local Whisper pt-BR to clipboard and auto-paste, gated by a Win10 published-build inference benchmark
- [x] **Phase 2: Overlay, Tray, Settings & Cancel** - Daily-usable shell: waveform pill, tray, persisted settings, Esc cancel, first-run model download
- [x] **Phase 3: Cloud Engine** - Opt-in Groq/OpenAI transcription with explicit errors and recoverable recordings
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

**Plans:** 13/13 plans executed

Plans (all execution and verification sequential):

- [x] 01-01-PLAN.md - Worker/protocol tracer, published gate and model fetch (wave 1)
- [x] 01-02-PLAN.md - App-worker-clipboard skeleton and Core contracts (wave 2)
- [x] 01-03-PLAN.md - Global hook, tap/hold and long-hold watchdog (wave 3)
- [x] 01-04-PLAN.md - Target/focus validation and safe paste (wave 3)
- [x] 01-05-PLAN.md - Fixtures, CPU VAD and whole-output filter (wave 3)
- [x] 01-06-PLAN.md - Durable capture, spool and recovery policy (wave 4)
- [x] 01-07-PLAN.md - CPU restart/retry and queued readiness (wave 4)
- [x] 01-08-PLAN.md - Isolated full benchmark and selected defaults (wave 5)
- [x] 01-09-PLAN.md - Pure reducer, admission and complete metrics (wave 6)
- [x] 01-10-PLAN.md - App owner loop and ordered effects (wave 7)
- [x] 01-11-PLAN.md - Startup recovery and replayable harness (wave 8)
- [x] 01-12-PLAN.md - Notepad/silence/crash and latency E2E (wave 9)
- [x] 01-13-PLAN.md - Focus/metrics, published App and final verifier (wave 10)

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

**Plans**: 6 plans

- [x] 02-01-PLAN.md - Single instance mutex, persisted settings with DPAPI, and corruption recovery (wave 1)
- [x] 02-02-PLAN.md - Esc cancellation and hotkey conflict validator (wave 2)
- [x] 02-03-PLAN.md - System tray menu with pt-BR actions and last transcript repaste (wave 3)
- [x] 02-04-PLAN.md - Overlay pill window with live waveform and non-intrusive styles (wave 4)
- [x] 02-05-PLAN.md - Settings dialog, dynamic audio device switching and sound cues (wave 5)
- [x] 02-06-PLAN.md - First-run model downloader with resume and SHA-256 verification (wave 6)

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

**Plans**: 2 plans

- [x] 03-01-PLAN.md - Cloud transcriber contracts, Groq & OpenAI clients, error classification & key masking (wave 1)
- [x] 03-02-PLAN.md - Mock HTTP fixture suite, recording retention & cloud adapter integration (wave 2)

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

**Plans**: 6/6 plans executed

- [x] 04-04-PLAN.md
- [x] 04-05-PLAN.md
- [x] 04-06-PLAN.md
- [x] 04-01-PLAN.md - Start-with-Windows autostart, registry HKCU & tray/settings toggle (wave 1)
- [x] 04-02-PLAN.md - Power & session resilience (sleep/resume, lock/unlock) & hook watchdog (wave 2)
- [x] 04-03-PLAN.md - Per-user installer, directory layout & soak/paste matrix verification (wave 3)

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Dictation Loop MVP | 13/13 | Complete | 2026-10-23 |
| 2. Overlay, Tray, Settings & Cancel | 6/6 | Complete | 2026-10-23 |
| 3. Cloud Engine | 2/2 | Complete | 2026-10-23 |
| 4. Packaging, Autostart & Hardening | 6/6 | In Progress|  |
