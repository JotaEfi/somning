# Somning

## What This Is

Somning is a Windows 10 desktop voice-dictation utility (in the spirit of Claude Code's `/voice` mode and Wispr Flow). The user presses a global hotkey, a small pill overlay with an animated waveform (WhatsApp-voice-note style) appears at the bottom-center of the screen while it listens, and when recording stops the speech is transcribed with the highest possible quality in Brazilian Portuguese, copied to the clipboard and auto-pasted (Ctrl+V) into whatever field is focused. Built for a single user (the owner) for daily use across every app.

## Core Value

Press the hotkey, speak Portuguese, and accurate text lands in the focused field (and stays in the clipboard) — reliably, every time, in any app.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Global hotkey (default Ctrl+Shift+Space) works system-wide, even when other apps are focused
- [ ] Hybrid trigger: quick tap toggles recording on/off; holding the key acts as push-to-talk (release = stop)
- [ ] Bottom-center, always-on-top, non-focus-stealing pill overlay with live animated waveform driven by mic level while listening; shows a "transcribing" state after stop
- [ ] Microphone capture at 16 kHz mono suitable for Whisper
- [ ] Local transcription by default using Whisper large-v3-turbo (pt-BR fixed) — runs offline, no cost
- [ ] Optional cloud transcription engine (OpenAI gpt-4o-transcribe or Groq whisper-large-v3-turbo) when the user supplies an API key in settings
- [ ] Transcribed text placed on clipboard and auto-pasted into the previously focused window/field via Ctrl+V; text remains in clipboard afterwards
- [ ] System tray icon with menu: settings, open logs, quit
- [ ] Settings: hotkey, engine (local/cloud), API key, microphone device, auto-paste on/off; persisted to disk
- [ ] Packaged as a Windows executable/installer that runs without a dev environment; optional start-with-Windows
- [ ] Graceful error feedback in the overlay (no mic, model missing, API failure) — never silently loses a recording

### Out of Scope

- Languages other than pt-BR in v1 — user explicitly wants pt-BR only first; more languages later (v2)
- Live streaming partial text shown on screen — overlay shows only waveform; transcription happens on stop (better quality, simpler)
- macOS/Linux support — owner uses Windows 10 only
- LLM post-processing/rewriting of text (formatting, commands) — v2 idea, not needed for core dictation
- Transcription history UI — v2
- Multi-user/accounts/sync — personal tool

## Context

- **Hardware:** AMD Ryzen 5 5600X (6c/12t), 16 GB RAM, AMD Radeon RX 7600 (8 GB, no CUDA). Local inference must therefore use CPU (e.g. faster-whisper/CTranslate2 int8) or a Vulkan GPU backend (whisper.cpp Vulkan). Research must pick the best quality/latency trade-off on this hardware.
- **Toolchain available:** Python 3.13, uv, .NET 10 SDK, Node 24, git. AI CLIs available for agentic development: Claude Code, Codex (`codex`), OpenCode (`opencode`). Maestri canvas can spawn additional agent terminals.
- **Development model:** 100% agentic and autonomous — no human assistance during development. The owner only receives the finished product and later suggests improvements. Verification must therefore be automatable (unit tests, synthetic audio fixtures, scripted end-to-end checks) wherever possible.
- **Reference UX:** Claude Code `/voice` (hold-to-talk), Wispr Flow, WhatsApp voice-note waveform.

## Constraints

- **Platform**: Windows 10 Pro 10.0.19045 x64 — sole target
- **Language**: pt-BR only in v1 — owner's request; keep language as a config value so v2 can expand
- **Cost**: local engine must be free and offline-capable — cloud is optional opt-in
- **Simplicity**: "functional and simple" — minimal UI, no feature bloat
- **Latency**: text should paste within a few seconds after stop for typical 5–30 s utterances
- **Autonomy**: every phase must be verifiable by agents without the owner (automated tests; scripted runs)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Local engine by default + optional cloud via API key | Free/private/offline by default; best-in-class quality available on demand | — Pending |
| Hybrid hotkey (tap = toggle, hold = push-to-talk), default Ctrl+Shift+Space | Owner chose "both" | — Pending |
| pt-BR fixed in v1 | Owner request; fixed language improves Whisper accuracy | — Pending |
| Transcribe on stop (no live partial text) | Overlay only shows waveform; full-utterance decoding gives best quality | — Pending |
| Paste via clipboard + simulated Ctrl+V, keep text in clipboard | Owner wants both auto-paste and later manual paste | — Pending |
| C#/.NET 10 WPF shell + Whisper.net (whisper.cpp, Vulkan/CPU) in a separate restartable `Somning.Engine` worker process | Research + Codex cross-review: reliable LL hooks/overlay on Windows; only practical AMD GPU path; worker isolates native crashes and enables GPU→CPU restart | — Pending (gated on Win10 published-build inference test in Phase 1) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-10-22 after initialization*
