# Phase 1 Discussion Log

**Mode:** --auto (owner requested fully autonomous development; all choices are recommended defaults)
**Date:** 2026-10-22

[auto] Engine worker protocol — Q: "In-process Whisper.net or worker process? Transport?" → Selected: "Separate Somning.Engine worker, stdin/stdout JSON Lines, WAV path handoff" (recommended by cross-review)
[auto] Hotkey semantics — Q: "Tap/hold threshold and leak prevention?" → Selected: "300 ms, synchronous suppression in LL hook, timing in pure interpreter"
[auto] Audio & recordings — Q: "Pre-roll vs open-on-press; durability?" → Selected: "Open mic on press, incremental spool WAV + state sidecar, recover on start, keep last 20"
[auto] Silence gate — Q: "How to stop hallucinations?" → Selected: "duration gate + Whisper.net Silero VAD + blocklist + no_speech threshold"
[auto] Paste policy — Q: "Changed/elevated target behavior?" → Selected: "Wait for physical modifier release, verify target, atomic SendInput, else clipboard-only"
[auto] Model acquisition — Q: "Download UI now or later?" → Selected: "Scripted fetch with SHA-256 now; UI in Phase 2"
[auto] Benchmark & budget — Q: "Latency budget and selection rule?" → Selected: "p95 ≤ 3.0 s for 15 s clip; most accurate config within budget"
[auto] Fixtures — Q: "Test audio source?" → Selected: "edge-tts pt-BR voices (SAPI fallback), committed small WAVs"
[auto] Logging — Q: "Log transcript text?" → Selected: "No, length only; timings per dictation; key redaction"

## Deferred
- Always-open pre-roll buffer; per-app paste method; transcript logging opt-in.
