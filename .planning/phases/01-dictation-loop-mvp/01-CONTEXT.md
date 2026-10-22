# Phase 1: Dictation Loop MVP - Context

**Gathered:** 2026-10-22
**Status:** Ready for planning
**Mode:** --auto (all gray areas auto-resolved with recommended defaults; owner requested zero human assistance)

<domain>
## Phase Boundary

Headless end-to-end dictation loop on Windows 10: global hotkey (tap=toggle / hold=PTT) → mic capture (durable spool) → silence/VAD gate → local Whisper large-v3-turbo pt-BR in a separate `Somning.Engine` worker process (Vulkan with CPU fallback) → clipboard + target-aware Ctrl+V. No overlay, tray or settings UI (Phase 2); no cloud (Phase 3); no installer (Phase 4). Observability = log files only. Starts with the Windows 10 published-build inference gate + benchmark.

Requirements: HOTK-01, HOTK-02, HOTK-03, AUD-01, AUD-04, STT-01, STT-02, STT-03, STT-04, STT-08, OUT-01, OUT-02, OUT-03, APP-06.

</domain>

<decisions>
## Implementation Decisions

### Solution layout
- **D-01:** .NET 10 solution `Somning.sln` under `src/` + `tests/`: `Somning.Core` (pure: state machine/reducer, TriggerInterpreter, ports, post-processing — no Win32/UI refs), `Somning.Platform.Windows` (hook, NAudio capture, clipboard/SendInput, foreground/UIPI checks), `Somning.Engine.Protocol` (shared DTOs), `Somning.Engine` (worker exe hosting Whisper.net), `Somning.App` (composition root, WinExe, hidden message-loop host), `tests/Somning.Core.Tests`, `tests/Somning.Integration.Tests`, `tools/` (benchmark, fixture generation). — **Reversibility:** costly — project boundaries are referenced by every later phase.
- **D-02:** Pin package versions exactly (Directory.Packages.props central package management): Whisper.net / Whisper.net.Runtime / Whisper.net.Runtime.Vulkan 1.9.1 (aligned), NAudio 3.3.0 (use new non-obsolete APIs), Microsoft.Windows.CsWin32 0.3.367 stable, Serilog + Serilog.Sinks.File, xUnit. Researcher verifies at plan time; any change must keep Whisper packages aligned.

### Engine worker protocol
- **D-03:** `Somning.App` spawns `Somning.Engine.exe` as a child process and talks over **stdin/stdout JSON Lines** (one request/response per line; stderr = worker logs). Audio is handed off by **file path to the spool WAV** (no audio over the pipe). Messages: `hello/ready{backend,model,load_ms,warm_ms}`, `transcribe{id,wav_path,language:"pt",prompt}` → `result{id,text,segments,infer_ms,no_speech}` | `error{id,code,message}`, `cancel{id}`, `shutdown`. — **Reversibility:** costly — protocol contract shared by Phase 3 cloud adapter selection and Phase 4 packaging.
- **D-04:** Worker start modes `--backend vulkan|cpu`. Runtime order is set **before** native init (Whisper.net RuntimeOptions). App starts worker in the configured mode (default from benchmark); if the worker exits unexpectedly, fails readiness within timeout, or reports a GPU error, the app restarts it in `cpu` mode and retries the pending recording once. Restart backoff + max 3 restarts/10 min, then error state logged.
- **D-05:** Worker preloads model + runs a dry-run warm-up on 1 s of silence at start; app queues a dictation that finishes before `ready` (does not drop it).
- **D-06:** Inference params: `Language="pt"`, threads = 6 (physical cores) on CPU, no conditioning on previous text, temperature fallback enabled, initial prompt = short pt-BR punctuated sentence (configurable constant now, settings UI in Phase 2).

### Hotkey semantics
- **D-07:** Default chord Ctrl+Shift+Space via own `WH_KEYBOARD_LL` hook on a dedicated thread with its own message loop. Callback does **minimal synchronous chord recognition + suppression** (returns 1 for the trigger key events so Space never leaks) and enqueues timestamped events; all timing logic lives in pure `TriggerInterpreter`.
- **D-08:** Tap/hold threshold **300 ms** (constant now, setting in Phase 2). Press→release < 300 ms = tap: if idle → start toggle recording; if toggle-recording → stop. Press held ≥ 300 ms = push-to-talk: release stops. Auto-repeat key-downs ignored. Max recording length 5 min (auto-stop). Missed key-up safety: if hook reports trigger down but `GetAsyncKeyState` shows it up for >1 s during PTT, stop.
- **D-09:** Injected events: hook ignores events carrying Somning's own `dwExtraInfo` marker (our Ctrl+V); test harness injects hotkey events with a distinct test marker that IS processed (so SendInput-based E2E tests drive the real hook).

### Audio & recordings
- **D-10:** Capture at device native format via NAudio, resample to 16 kHz mono 16-bit, ~300 ms pre-roll ring buffer kept while idle-armed is NOT used (privacy) — instead start capture on hotkey down immediately (latency of mic open measured and logged). Recording streamed incrementally to `%LOCALAPPDATA%\Somning\recordings\{timestamp}.wav` (spool) — original untrimmed audio kept.
- **D-11:** Completion tracked per recording in a sidecar `{timestamp}.json` (`captured → transcribed(text) → clipboard_set → pasted|paste_deferred`). On app start, any recording not at `clipboard_set` or later is re-transcribed and its text placed on the clipboard (no auto-paste on recovery), logged as recovered. Retention: keep last 20 recordings + sidecars, delete older ones.

### Silence / hallucination gate (STT-03)
- **D-12:** Gate order: (1) duration < 0.4 s → discard; (2) Whisper.net Silero VAD (`WhisperVadFactory`, ggml Silero v6.2.0) finds no speech → discard; (3) transcribe full (untrimmed, padded) audio; (4) post-filter: whole-output blocklist (case/punctuation-insensitive: "obrigado", "obrigado por assistir", "legendas pela comunidade amara.org", "inscreva-se", etc.) + `no_speech` prob threshold → discard. Discard = nothing pasted, clipboard untouched, outcome logged as `empty`. RMS energy is logged only, never used as sole speech proof.

### Output / paste
- **D-13:** At stop, record target `HWND` (foreground window + focused control via `GetGUIThreadInfo`). Before paste: set clipboard (retry `OpenClipboard` up to ~10× with backoff, verify via sequence number), then wait up to 1.5 s for physical Ctrl/Shift/Alt/Win release (polled, **no synthetic key-up of user-held modifiers**), verify foreground unchanged and target not elevated (UIPI integrity check), then send one atomic `SendInput` batch (Ctrl down, V down, V up, Ctrl up) tagged with `dwExtraInfo`, and check inserted count == 4. Any failure → `paste_deferred` (text remains on clipboard) + log. Text normalized: trimmed, no trailing newline, no trailing space.
- **D-14:** Clipboard is never restored; text remains after paste (OUT-01).

### Model acquisition (Phase 1 scope)
- **D-15:** Models live in `%LOCALAPPDATA%\Somning\models\` (`ggml-large-v3-turbo{,-q8_0,-q5_0}.bin`, `ggml-silero-v6.2.0.bin`). Phase 1 provides a scripted fetch (tool/CLI `Somning.App --fetch-models` or `tools/fetch-models.ps1`) from the official Hugging Face `ggerganov/whisper.cpp` repo with SHA-256 verification; download UI with progress/resume is Phase 2 (APP-04). Models are NOT committed to git (.gitignore).

### Benchmark gate & latency budget (STT-08)
- **D-16:** First plan/wave = gate: `dotnet publish` self-contained **directory** (win-x64) of `Somning.Engine`, run from a clean directory with no SDK on PATH, transcribe a pt-BR fixture with CPU and with Vulkan. If Whisper.net natives fail on Win10 19045 → fallback path per research (build whisper.cpp natives with Win10-compatible toolchain / validated whisper.cpp worker binary) before continuing.
- **D-17:** Benchmark tool runs matrix {vulkan, cpu} × {f16, q8_0, q5_0} on fixture set, reporting WER vs expected text, load/warm/infer ms, p50/p95 over ≥10 runs, written to `.planning/phases/01-dictation-loop-mvp/BENCHMARK.md`. **Selection rule:** choose the most accurate config whose warm p95 stop-to-paste ≤ **3.0 s for a 15 s clip**; tie → faster. CPU-only configs that exceed it are "degraded mode" (still allowed as fallback). Selected config + budget recorded in BENCHMARK.md and as defaults in code.

### Test fixtures & verification
- **D-18:** pt-BR fixtures generated by `tools/make-fixtures` using **edge-tts** (Python, voices pt-BR-FranciscaNeural / pt-BR-AntonioNeural) with SAPI pt-BR as fallback; each fixture = WAV (16 kHz mono) + expected text; include short command, long sentence with numbers/names, quiet speech (−20 dB), speech with background noise, plus silence-only, noise-only and 0.2 s tap fixtures. Small fixtures (< ~5 MB total) committed under `tests/fixtures/`.
- **D-19:** Audio injection for tests: `IAudioSource` has a file-backed implementation selectable via `--test-harness` (feeds a WAV in real time). Real-mic path is exercised only by an optional manual smoke checklist.
- **D-20:** `--test-harness` control channel (named pipe `\\.\pipe\somning-test`) is compiled only in Debug / `SOMNING_TEST_HARNESS` builds, never in Release.
- **D-21:** E2E test: launch Notepad, launch App in harness mode with a fixture, inject hotkey via SendInput (tap-tap and hold-release variants), read Notepad edit text via `WM_GETTEXT`, assert WER ≤ threshold, clipboard == text, and no stray space/characters. Requires interactive desktop session — tests skip (not fail) with a clear message otherwise.
- **D-22:** Pass threshold on TTS fixtures: WER ≤ 10% for clean fixtures (synthetic regression guard, not real-world accuracy).

### Logging (APP-06)
- **D-23:** Serilog rolling file `%LOCALAPPDATA%\Somning\logs\somning-.log` (7-day retention); one structured line per dictation: `id, mode(tap|hold), capture_ms, mic_open_ms, vad_ms, queue_ms, infer_ms, backend, model, paste_ms, outcome(pasted|paste_deferred|empty|error), chars`. Destructuring policy redacts any property named like `*key*|*token*|Authorization`. Transcript text is NOT logged by default (privacy) — only length.

### Claude's Discretion
- Internal class names, folder structure inside projects, JSON field naming details, exact retry/backoff numbers not specified above, choice of UIA vs WM_GETTEXT per control type in tests.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project
- `.planning/PROJECT.md` — vision, constraints, key decisions
- `.planning/REQUIREMENTS.md` — v1 requirement IDs and wording
- `.planning/ROADMAP.md` §Phase 1 — goal and success criteria

### Research
- `.planning/research/SUMMARY.md` — consolidated stack/architecture/pitfalls (takes precedence)
- `.planning/research/CROSS-REVIEW.md` — independent Codex review: Win10 native support gate, no in-process runtime fallback, VAD API, target-aware paste, durable recordings, pinned versions
- `.planning/research/STACK.md` — library rationale (versions corrected by CROSS-REVIEW)
- `.planning/research/ARCHITECTURE.md` — ports/adapters, reducer, threading, test layers
- `.planning/research/PITFALLS.md` — hallucination, hook, paste, overlay, packaging pitfalls

### External
- Whisper.net 1.9.1 README + `examples/Vad/Program.cs` + `LibraryLoader/RuntimeOptions.cs` (github.com/sandrohanea/whisper.net, tag 1.9.1)
- Microsoft docs: LowLevelKeyboardProc, SendInput, GetGUIThreadInfo

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- None — greenfield repository (only `.planning/`).

### Established Patterns
- None yet; this phase establishes them (ports/adapters, pure reducer, fakes for every port).

### Integration Points
- Phase 2 attaches overlay/tray/settings to the controller's `UiState` snapshot stream and settings store; Phase 3 adds a cloud `ITranscriber`; Phase 4 packages `Somning.App` + `Somning.Engine` directory publish.

</code_context>

<specifics>
## Specific Ideas

- UX reference: Claude Code `/voice` (hold-to-talk) and Wispr Flow; WhatsApp-style waveform comes in Phase 2.
- Owner speaks Brazilian Portuguese; expect technical vocabulary (software/AI terms) mixed in.
- Handy (cjpais/Handy) disables Vulkan implicit layers on Windows to avoid crashes with overlay/capture tools — test GPU mode with Parsec virtual display adapter present (owner machine has one).

</specifics>

<deferred>
## Deferred Ideas

- Pre-roll ring buffer with always-open mic — rejected for privacy in v1; revisit if first words get clipped.
- Per-app paste method (Ctrl+Shift+V for terminals) — v2 (OUT2-02).
- Logging transcript text for debugging — opt-in setting, Phase 2+ if needed.

</deferred>

---

*Phase: 01-dictation-loop-mvp*
*Context gathered: 2026-10-22*
