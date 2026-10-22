# Project Research Summary

**Project:** Somning
**Domain:** Windows 10 push-to-talk voice dictation utility (global hotkey → mic → Whisper pt-BR → clipboard + auto-paste, pill overlay, tray)
**Researched:** 2026-10-22
**Confidence:** MEDIUM-HIGH (language/architecture/pitfalls HIGH; local latency on RX 7600 / 5600X and Whisper.net Windows 10 support are unmeasured and gate Phase 1)

> Inputs: STACK.md, FEATURES.md, ARCHITECTURE.md, PITFALLS.md, plus CROSS-REVIEW.md (independent Codex review). Cross-review corrections are folded in below and take precedence over STACK.md where they conflict.

## Executive Summary

Somning is a single-user, Windows-only dictation tool in the Wispr Flow / Superwhisper / Handy family. Products in this class converge on one shape: a headless dictation core (single-writer state machine) wrapped by thin platform adapters — low-level hotkey hook, mic capture, STT engine, clipboard + SendInput paste, a no-focus overlay, and a tray. The UI is a dumb subscriber to the state machine and every side-effecting adapter sits behind an interface, which makes agent-only development viable: most logic is verifiable with no desktop, mic or model.

**Recommended approach:** C# / .NET 10 WPF shell. Local STT = Whisper.net 1.9.1 (whisper.cpp) with ggml large-v3-turbo, pt fixed, **running in a separate restartable inference worker process** (`Somning.Engine`) with explicit GPU (Vulkan) / CPU startup modes — because Whisper.net selects and caches its native runtime at load time (no in-process Vulkan→CPU fallback) and a native/driver crash must not kill the UI or lose a recording. faster-whisper/CTranslate2 has no AMD GPU path on Windows, so whisper.cpp Vulkan is the only practical way to use the RX 7600. Hotkey = own `WH_KEYBOARD_LL` hook (RegisterHotKey gives no key-up) with minimal synchronous chord recognition/suppression in the callback and everything else enqueued to a pure `TriggerInterpreter`. Cloud (Groq whisper-large-v3-turbo, OpenAI gpt-4o-transcribe) is an opt-in `ITranscriber`. Text stays in the clipboard (no restore).

**Key risks** — all fail silently with wrong output: Whisper hallucinating on silence/taps ("Obrigado.", "Amara.org") and pasting it; paste landing as Ctrl+Shift+V or in the wrong window; overlay stealing focus; Windows silently removing a slow hook; Whisper.net native runtime officially requiring Windows 11 (unverified on 19045); unknown AMD latency. Mitigations are design decisions with agent-runnable tests.

## Key Findings

### Recommended Stack (see STACK.md, corrected by CROSS-REVIEW.md)
- **.NET 10 + WPF (`net10.0-windows`)** — shell, overlay, settings. Per-pixel alpha, GPU animation. (Tauri/Rust is viable — Handy proves it — but adds boundaries without improving pt-BR quality; WPF is simpler here.)
- **Whisper.net 1.9.1 + Whisper.net.Runtime 1.9.1 + Whisper.net.Runtime.Vulkan 1.9.1** — pinned together, hosted in the `Somning.Engine` worker process. **Gate:** README lists Windows 11/Server 2022+ and VC++ 2022 redist; Windows 10 19045 must be proven with a published build first. Fallbacks if it fails: build whisper.cpp natives with a Win10-compatible toolchain, or a validated whisper.cpp worker binary.
- **Whisper.net VAD** — `WhisperVadFactory.FromPath(silero).CreateBuilder()...DetectSpeechAsync()` (Silero v6.2.0 ggml). It is a separate stage; the app must apply detected segments. No ONNX dependency needed. RMS gate is only a heuristic.
- **ggml large-v3-turbo** (f16 ~1.6 GB / q8 / q5_0 ~574 MB) — choose by benchmark on natural pt-BR. `Language="pt"`, pt-BR initial prompt.
- **NAudio 3.3.0** (`WaveIn` / `WasapiRecorder` new APIs — or deliberately pin 2.x) — 16 kHz mono capture + RMS level.
- **Microsoft.Windows.CsWin32 0.3.367 (stable)** — hook, SendInput, window styles via `NativeMethods.txt`.
- **Small pieces:** WinForms `NotifyIcon`, DPAPI (API key), System.Text.Json (`%APPDATA%\Somning`), Serilog file logs, HKCU Run autostart.
- **Cloud:** HttpClient multipart, OpenAI-compatible. Groq `whisper-large-v3-turbo` ($0.04/h, 10 s min billing); OpenAI `gpt-4o-transcribe` (~$0.006/min, verify price).
- **Testing/packaging:** xUnit + WAV fixtures (FlaUI optional). **Velopack 1.2.161** with custom `[STAThread] Main` calling `VelopackApp.Build().Run()`; ship a self-contained *directory* publish (preserve native runtime paths) inside the installer — not single-file EXE first. No NativeAOT/trimming.

### Expected Features (see FEATURES.md)
**Must have:** hybrid hotkey tap=toggle / hold=PTT (Ctrl+Shift+Space, ~250–300 ms threshold, ignore auto-repeat); 16 kHz mic capture with device choice; non-focus-stealing click-through pill with live waveform + listening/transcribing/error states; Esc cancel; min-duration + silence/VAD gate; local Whisper pt-BR behind engine abstraction; clipboard write + auto Ctrl+V keeping text in clipboard; tray; persisted settings; graceful errors; never lose a recording; installer + start-with-Windows.
**Should have:** cloud engine; custom vocabulary / initial prompt; start/stop sounds; hallucination blocklist; paste-last-transcript action.
**Defer (v2+):** other languages (language stays a config value); spoken punctuation; LLM post-processing; clipboard restore mode; history UI.
**Anti-features:** live partial text, clipboard restore in v1, auto language detect, always-on listening, keystroke typing simulation, telemetry/auto-update.

### Architecture Approach (see ARCHITECTURE.md)
- Projects: `Somning.Core` (pure, no Win32/UI), `Somning.Platform.Windows` (adapters), `Somning.Engine` (inference worker exe, Whisper.net), `Somning.Engines.Cloud`, `Somning.App` (composition root + WPF UI), tests.
- Single-writer `DictationController`: serialized event queue + pure reducer `(state, event) → (state, effects)`. States: Idle → Arming → Recording → Transcribing → Pasting → Idle, plus Cancel and transient Error.
- Ports with fakes: `IHotkeySource`, `IAudioSource`, `ITranscriber`, `ITextOutput`, `IClock`, `IForeground`, `ISettingsStore`.
- Engine worker: started at app launch, model preloaded + dry-run warm-up, stays resident; health/readiness, timeout, cancellation, auto-restart in CPU mode if GPU mode crashes; hotkey before ready queues behind load; missing model never crashes.
- Durable recordings: capture persisted incrementally to a spool WAV before inference; original untrimmed audio kept; pending recordings recovered on restart; completion tracked separately for transcription, clipboard, paste; cleanup only after the result is secured.
- Target-aware output: record target window at stop; before paste verify focus unchanged and wait (bounded) for physical modifier release — no synthetic modifier releases; one atomic SendInput batch tagged with `dwExtraInfo`; check inserted count; elevated/changed target → clipboard-only + overlay notice.

### Critical Pitfalls (reconciled to .NET)
1. **Hallucination on silence/taps (Phase 1)** — min-duration (~0.4 s) + VAD gate before engine/upload; `no_speech`/logprob/compression thresholds; no conditioning on previous text; whole-output blocklist; fixtures asserting nothing is pasted.
2. **Paste failures (Phase 1)** — target-aware paste as above; clipboard `OpenClipboard` retry + sequence-number verify; strip trailing newline; Ctrl+V only in v1; UIPI = documented limitation with clear notice.
3. **Hook dropped/leaks Space (Phase 1 design, Phase 4 soak)** — synchronous minimal suppression in callback, dedicated message-loop thread, delegate kept alive, <1 ms work; reinstall on resume/unlock + watchdog; force-stop on missed key-up; max recording length.
4. **Focus-stealing overlay (Phase 2)** — `WS_EX_NOACTIVATE|TOOLWINDOW|TOPMOST|TRANSPARENT`, `ShowActivated=false`; assert `GetForegroundWindow()` unchanged; Per-Monitor V2 DPI; pill on foreground window's monitor.
5. **Native runtime / AMD / cold load (Phase 1)** — Win10 published-build inference gate; Vulkan vs CPU and f16/q8/q5 benchmark with p50/p95 stop-to-paste; worker-process isolation; preload + warm-up; 6 threads; log load/warm/infer/RTF; test with Vulkan overlay/capture layers (Handy disables implicit layers).
Also: IME/Ctrl+Shift layout toggle conflicts (1–2); mic native-rate resample, pre-roll, dead-input detection (1–2); model download resume + SHA-256 + non-ASCII path (2,4); cloud error matrix (3); AV/SmartScreen on unsigned hooking exe (4); .NET 10 support matrix doesn't list Win10 22H2 — validate directly (4).

## Implications for Roadmap

### Phase 1: Dictation Loop MVP (headless end-to-end, local engine)
**Rationale:** Retires the biggest risks first (native runtime on Win10, local quality/latency on AMD, paste reliability) and fixes the interfaces everything plugs into.
**Delivers:** solution skeleton, ports + fakes, reducer, TriggerInterpreter, LL hook; NAudio capture + RMS + pre-roll + durable spool; VAD/min-duration gate; `Somning.Engine` worker (Vulkan/CPU modes, restart) + minimal model fetch; post-processor + blocklist; target-aware clipboard+SendInput output; Serilog metrics; `--test-harness`; pt-BR TTS fixture generator + WER scorer; Win10 published-build gate + benchmark deciding defaults.
**Verification:** reducer/trigger tables with fake clock; controller scenarios with fakes; engine contract tests on WAV fixtures (WER threshold, latency budget, silence → empty); worker-crash recovery test; injected-hotkey test; E2E into Notepad via WM_GETTEXT/UIA asserting text + clipboard.

### Phase 2: Overlay, Tray, Settings & Cancel (daily-usable shell)
**Delivers:** pill overlay (DPI-correct, click-through, ~60 fps waveform, listening/transcribing/error/"nada detectado"), Esc cancel, tray (Settings, Open logs, Paste last, Quit, TaskbarCreated), single-instance mutex, settings window (hotkey recorder w/ conflict warning, mic, engine, API key, auto-paste), atomic JSON + DPAPI, mic dead-input detection, first-run model download with progress/resume/checksum, pt-BR messages.
**Verification:** window-style flags + foreground-unchanged assertion; screenshot pixel checks; settings round-trip/corruption tests; local HTTP server for download cases; E2E rerun with overlay.

### Phase 3: Cloud Engine (opt-in)
**Delivers:** shared OpenAI-compatible client for Groq/OpenAI (`language=pt`, prompt), timeouts + jittered retry, 25 MB guard, mapped overlay errors, recording kept on failure, explicit opt-in (no silent fallback), key redaction.
**Verification:** same contract suite against a mock HTTP server with full error matrix; real APIs only when key env var present (skip, never fail). Promote earlier only if Phase 1 shows local latency misses budget.

### Phase 4: Packaging, Autostart & Hardening
**Delivers:** self-contained directory publish in a Velopack per-user installer; native layout verified; HKCU Run autostart (`--minimized`); clean-user install test incl. non-ASCII/space paths; frozen-build `--selftest wav`; AV/SmartScreen mitigations; hook reinstall on resume + soak; paste matrix (Notepad, Chrome/Edge, VS Code editor+terminal, Windows Terminal, Word, Slack/Discord, elevated Notepad → graceful notice); startup timing budget; harness channel compiled out of release.
**Verification:** E2E against installed binary; scripted soak/resume; paste-matrix report.

### Phase Ordering Rationale
- Risk-first + value-first; the `ITranscriber` port lets the benchmark change the default without touching other code.
- Overlay after paste so the focus pitfall is asserted against a working E2E.
- Cloud late but not last: reuses contract suite and settings UI; contingency if local latency fails.
- UI/E2E tests need an interactive console session (not Session 0 / disconnected RDP) — state in Phase 1 plan.

### Research Flags
- **Needs phase research:** Phase 1 (highest — Whisper.net 1.9.1 API/VAD/abort/prompt, Win10 native compatibility, worker protocol, real latency, pt-BR TTS voice availability, hook injected-event handling); Phase 2 (WPF click-through overlay + per-monitor DPI + screenshot verification, hotkey recorder); Phase 4 (Velopack WPF integration, native layout, AV heuristics).
- **Standard patterns:** Phase 3 (just confirm current pricing/limits).

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM-HIGH | Language choice HIGH; Whisper.net Win10 support and latency unverified (Phase 1 gate) |
| Features | MEDIUM | Owner requirements settle open questions |
| Architecture | MEDIUM-HIGH | Converges across OSS apps; worker-process isolation added by cross-review |
| Pitfalls | MEDIUM-HIGH | Reconciled to .NET; Win32 specifics to verify per phase |

**Overall confidence:** MEDIUM-HIGH

### Gaps to Address
- Whisper.net native runtime on Windows 10 19045 (published build, no dev SDKs) — first Phase 1 task.
- Local latency/quality: Vulkan vs CPU, f16/q8/q5 on natural pt-BR; p50/p95 stop-to-paste; decides defaults and whether cloud is promoted.
- Tap/hold threshold — start 250–300 ms, configurable.
- pt-BR TTS fixtures — SAPI voice may be absent; fall back to Piper/edge-tts. Synthetic WER is a regression guard, not true accuracy.
- Real-mic path — virtual audio cable may not install unattended; keep that smoke test optional.
- Elevated windows — v1 limitation by design.
- OpenAI pricing — confirm in Phase 3.
- Owner-only checks — keep a tiny real-hardware checklist (speak into real mic, confirm feel of waveform/latency).

## Sources

### Primary (HIGH confidence)
- Whisper.net 1.9.1 README, NativeLibraryLoader, WhisperVadProcessorBuilder, VAD example (github.com/sandrohanea/whisper.net)
- NuGet: Whisper.net*, NAudio 3.3.0, CsWin32 0.3.367, Velopack 1.2.161
- Microsoft docs: LowLevelKeyboardProc, SendInput; .NET 10 supported-OS matrix
- Groq model docs/pricing; Wispr Flow hotkey docs

### Secondary (MEDIUM confidence)
- whisper.cpp Vulkan & VAD; Superwhisper docs; OpenWhispr docs; Handy (incl. Vulkan implicit-layer guidance); faster-whisper requirements; OpenAI turbo model card

### Tertiary (LOW confidence)
- Wispr <0.5 s discard figure; OpenAI price aggregators; all Vulkan/CPU latency estimates until benchmarked

---
*Research completed: 2026-10-22*
*Ready for roadmap: yes*
