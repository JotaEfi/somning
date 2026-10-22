# Architecture Research

**Domain:** Windows desktop push-to-talk voice dictation (global hotkey, mic capture, Whisper STT local/cloud, clipboard + auto-paste, waveform pill overlay, tray, settings)
**Researched:** 2026-10-22
**Confidence:** MEDIUM-HIGH. The structure is converged across the open-source apps I know (OpenWhispr, Whispering/Epicenter, Handy, VoiceInk, whisper-writer, Wispr-style clones). I wrote this from knowledge of those designs and Win32 behavior, not from fresh source reading in this session, so verify the specific Win32 and library claims in the phase that builds each component. The testability section is my own design for the agentic-development constraint.

This document is language-agnostic on purpose, because the stack is decided in STACK.md. The shapes below map directly to C#/.NET (WPF or WinForms plus whisper.cpp bindings), Python, or Rust/Tauri. Code snippets use C#-style pseudocode only for clarity.

## Standard Architecture

### What the reference apps have in common

Every app in this class has the same core shape: a headless "dictation core" with a small state machine, plus thin platform adapters around it (hotkey, mic, STT engine, clipboard/paste, overlay, tray). Handy (Tauri/Rust) calls the core a "transcription manager" with a recorder, a VAD filter and a model manager. whisper-writer (Python) has a `ResultThread` that owns record then transcribe then type. OpenWhispr and Whispering put the engine behind a provider interface (local whisper.cpp vs OpenAI/Groq). VoiceInk keeps a `Recorder`, a `WhisperState` machine and a `PasteManager`.

The pattern worth copying is the one the better ones converge on: **the UI is a dumb subscriber to the state machine, and every side-effecting adapter sits behind an interface.** The weak ones (older whisper-writer) tangle UI, audio and inference in one thread class. That is hard to test and tends to hang.

### System Overview

```
 ┌───────────────────────────── Presentation (UI thread / STA) ────────────────────────────┐
 │  ┌──────────────┐   ┌──────────────────┐   ┌──────────────┐   ┌─────────────────────┐  │
 │  │ PillOverlay  │   │ TrayIcon + Menu  │   │ SettingsView │   │ Toasts / error text │  │
 │  │ (waveform)   │   │                  │   │              │   │ (inside the pill)   │  │
 │  └──────▲───────┘   └────────┬─────────┘   └──────┬───────┘   └──────────▲──────────┘  │
 └─────────┼────────────────────┼────────────────────┼──────────────────────┼─────────────┘
           │ UiState snapshots  │ commands           │ config edits         │
 ┌─────────┴────────────────────▼────────────────────▼──────────────────────┴─────────────┐
 │                        Application Core  (pure logic, no Win32, no UI types)            │
 │  ┌──────────────────────────────────────────────────────────────────────────────────┐  │
 │  │  DictationController  =  state machine + orchestration (single writer of state)  │  │
 │  │  Idle → Arming → Recording → Transcribing → Pasting → Idle   (+ Cancelled/Error) │  │
 │  └───┬───────────────┬───────────────┬───────────────┬──────────────────┬────────────┘  │
 │      │ ports         │               │               │                  │               │
 │  IHotkeySource   IAudioSource   ITranscriber     ITextOutput       IClock / IConfig     │
 └──────┼───────────────┼───────────────┼───────────────┼──────────────────┼───────────────┘
        │               │               │               │                  │
 ┌──────┴───────┐ ┌─────┴──────┐ ┌──────┴────────────┐ ┌┴─────────────┐ ┌──┴─────────────┐
 │ Win32 hotkey │ │ WASAPI     │ │ LocalWhisper      │ │ Clipboard +  │ │ SettingsStore  │
 │ service      │ │ capture    │ │ CloudOpenAI/Groq  │ │ SendInput    │ │ (JSON on disk) │
 │ (LL hook)    │ │ + resample │ │ ModelManager      │ │ Ctrl+V       │ │ SecretStore    │
 └──────────────┘ └────────────┘ └───────────────────┘ └──────────────┘ └────────────────┘
      Platform adapters (each has a Fake in tests; real impls are thin and integration-tested)
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **HotkeyService** | Detect global hotkey down/up, system-wide. Emit `KeyDown`/`KeyUp` events and, via a small `TriggerInterpreter`, turn them into `Toggle`, `PushStart`, `PushStop`. | Low-level keyboard hook (`WH_KEYBOARD_LL` via `SetWindowsHookEx`) on a dedicated thread with its own message loop. `RegisterHotKey` is simpler but gives no key-up event, so hold-to-talk cannot be detected with it alone. |
| **TriggerInterpreter** (pure) | The hybrid rule: a tap shorter than ~250-300 ms toggles recording; holding longer acts as push-to-talk and release stops. Pure function of (event, timestamp), so it unit-tests with fake time. | Small state machine inside the hotkey adapter's core half. |
| **AudioRecorder** | Open the selected mic, deliver 16 kHz mono float/int16 frames, compute level (RMS/peak) for the waveform, accumulate PCM in memory. | WASAPI capture (NAudio/WASAPI shared mode, `sounddevice`/PortAudio, or `cpal`). Capture at the device's native format, then resample to 16 kHz mono in a non-callback stage. |
| **LevelMeter** | Convert frames to ~30-60 Hz level values for the waveform. | Pure function over PCM windows. |
| **ITranscriber** (engine port) | `Task<TranscriptResult> TranscribeAsync(PcmBuffer, TranscribeOptions, CancellationToken)`. The one seam between "audio in" and "text out". | `LocalWhisperTranscriber` (whisper.cpp Vulkan or faster-whisper/CTranslate2 int8 on CPU), `CloudTranscriber` (OpenAI gpt-4o-transcribe or Groq), `FakeTranscriber` (tests). |
| **ModelManager** | Locate/download/verify the Whisper model file(s), report progress, own the model's on-disk path and checksum. Does not run inference. | HTTPS download with resume, SHA-256 check, atomic rename from a `.partial` file. Stored under `%LOCALAPPDATA%\Somning\models`. |
| **EngineHost / WarmCache** | Own the loaded model instance: lazy-load or preload at startup, keep it resident, unload on engine change. | Singleton behind `LocalWhisperTranscriber`; see "Model loading and warm-up". |
| **PreProcessor** | Trim leading/trailing silence, optional VAD, normalize gain, reject too-short or silent clips before they reach the engine. | Energy gate or Silero VAD. Pure and testable on WAV fixtures. |
| **TextPostProcessor** (thin) | Trim, collapse whitespace, optional trailing-space rule. No LLM rewriting in v1. | Pure function. |
| **TextOutput** | Put text on the clipboard and send Ctrl+V to the focused window; verify the clipboard write before pasting. | `SetClipboardData(CF_UNICODETEXT)` and `SendInput`. Optionally remember the foreground HWND at record-start and refocus if it changed. |
| **DictationController** | The state machine. The only component that sequences the others. Single-threaded via a serialized event queue (mailbox), so no locks on state. | Plain class, no UI or Win32 references. Takes all ports by constructor. |
| **OverlayWindow** | Bottom-center pill, topmost, never takes focus, animated waveform, "transcribing" spinner, error text. Renders `UiState`, nothing else. | Borderless layered window with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` (add `WS_EX_TRANSPARENT` if it should be click-through). Show with `SW_SHOWNOACTIVATE`. |
| **TrayIcon** | Notify icon with menu (Settings, Open logs, Quit) and state tint. | `NotifyIcon` (WinForms) or `Shell_NotifyIcon`. |
| **SettingsStore** | Load/save/validate config; raise change events; migrate versions. API key stored protected. | JSON file plus DPAPI (`ProtectedData`, CurrentUser scope) for the key. |
| **Logger** | Rolling file log plus in-memory ring for diagnostics; never logs transcript text unless a debug flag is set. | Serilog / `logging` / `tracing` to `%LOCALAPPDATA%\Somning\logs`. |
| **RecordingSpool** | Write each recording to disk (WAV) as soon as capture stops, before transcription starts, and delete it only after a successful paste. This satisfies the "never silently lose a recording" requirement. | Temp folder with a small retention count (for example the last 5). |

## Recommended Project Structure

Organized by role, with a hard rule that `Core` has no dependency on `Platform` or `Ui`. This is what makes headless testing cheap.

```
src/
├── Somning.Core/                 # pure logic, zero Win32/UI refs, fully unit-testable
│   ├── Dictation/
│   │   ├── DictationController.cs     # state machine + orchestration
│   │   ├── DictationState.cs          # enum + transition table
│   │   ├── DictationEvent.cs          # events into the machine
│   │   └── UiState.cs                 # immutable snapshot the UI renders
│   ├── Input/
│   │   └── TriggerInterpreter.cs      # tap-vs-hold logic, fake-clock friendly
│   ├── Audio/
│   │   ├── PcmBuffer.cs               # immutable 16k mono sample container
│   │   ├── LevelMeter.cs
│   │   ├── SilenceTrimmer.cs / Vad.cs
│   │   └── WavIo.cs                   # read/write WAV (fixtures + spool)
│   ├── Stt/
│   │   ├── ITranscriber.cs            # THE engine port
│   │   ├── TranscribeOptions.cs       # language=pt, prompt, etc.
│   │   └── TextPostProcessor.cs
│   ├── Ports/                         # IHotkeySource, IAudioSource, ITextOutput, IClock, IForeground
│   └── Config/
│       ├── AppSettings.cs             # immutable record, versioned
│       └── ISettingsStore.cs
├── Somning.Platform.Windows/     # thin adapters; each small enough to integration-test
│   ├── Hotkey/LowLevelKeyboardHook.cs
│   ├── Audio/WasapiAudioSource.cs
│   ├── Output/ClipboardPasteOutput.cs
│   ├── Output/ForegroundWindow.cs
│   └── Secrets/DpapiSecretStore.cs
├── Somning.Engines/              # ITranscriber implementations
│   ├── Local/LocalWhisperTranscriber.cs   # + EngineHost/warm cache
│   ├── Local/ModelManager.cs
│   └── Cloud/OpenAiTranscriber.cs, GroqTranscriber.cs
├── Somning.App/                  # composition root, UI, tray, packaging
│   ├── Program.cs                     # DI wiring, single-instance mutex
│   ├── Overlay/PillWindow + WaveformRenderer
│   ├── Tray/TrayHost
│   ├── Settings/SettingsWindow
│   └── Diagnostics/TestHarnessHost.cs # headless/test-control entry (see Testability)
tests/
├── Somning.Core.Tests/           # fast, no devices: state machine, trigger, VAD, post-proc
├── Somning.Engines.Tests/        # contract tests run against Fake AND real engine on WAV fixtures
├── Somning.Platform.Tests/       # Win32 integration (clipboard, SendInput into a test window)
├── Somning.E2E/                  # scripted: real app process + Notepad + injected audio
└── fixtures/
    ├── audio/                    # generated pt-BR WAVs + expected-text .txt + silence/noise clips
    └── tools/gen_fixtures.*      # TTS generation script (reproducible, checked in)
```

### Structure Rationale

- **Core vs Platform split:** Core holds everything with logic (state machine, tap/hold rule, VAD, post-processing). It compiles and tests with no audio device, no desktop and no Windows APIs. This is what lets an unattended agent verify most of the product in seconds.
- **Engines as their own assembly:** swapping or adding engines (local, OpenAI, Groq, and later others) touches only this assembly plus config. Local and cloud share one contract test suite.
- **App is the only place that knows about UI and DI:** the composition root builds real adapters in production and fakes in the harness, so the same binary can run a deterministic scenario.
- If the stack ends up as Python, keep the same boundaries as packages (`core/`, `platform_win/`, `engines/`, `app/`). If it is Tauri/Rust, they become crates and the overlay and settings live in the web layer.

## Architectural Patterns

### Pattern 1: Single-writer state machine with a serialized event queue

**What:** All inputs (hotkey events, audio-level ticks, transcription completion, errors, cancel, settings changes) become events posted to one queue. One worker loop processes them in order and is the only code that mutates state. Everything else is a subscriber to `UiState` snapshots.
**When to use:** Always here. Hotkey hook thread, audio thread, inference worker and UI thread all produce events concurrently, and this removes the race conditions that bite whisper-writer-style designs (double-start, stop-during-load, paste-after-cancel).
**Trade-offs:** A little ceremony versus ad-hoc flags, in return for determinism and exact replay in tests.

**State machine:**

```
                 Toggle/PushStart
   ┌──────┐  ─────────────────────►  ┌───────────┐  mic opened OK   ┌───────────┐
   │ Idle │                          │  Arming   │ ───────────────► │ Recording │
   └──▲───┘  ◄─────────────────────  └─────┬─────┘                  └─────┬─────┘
      │        mic error → Error(auto-clear)│                       Toggle/PushStop
      │                                     │                              │ (or max-duration)
      │   Cancel (Esc / settings change)    │                              ▼
      │ ◄───────────────────────────────────┴──────────────────── ┌──────────────┐
      │                                   Cancel ◄────────────────│ Transcribing │
      │                                                           └──────┬───────┘
      │   (empty/too-short/silent clip → Idle with brief "nothing heard")   │ text ready
      │                                                                    ▼
      │            paste done / paste failed (text still on clipboard)  ┌─────────┐
      └◄──────────────────────────────────────────────────────────────── │ Pasting │
                                                                         └─────────┘
   Error(msg): transient overlay state, auto-returns to Idle after ~2-3 s. The recording
   is already spooled to disk, so it is never lost.
```

Rules that matter:

1. **Idle → Arming → Recording.** `Arming` covers mic-open latency and shows the pill immediately, so the overlay feels instant. Pre-buffer a few hundred ms so the first syllable is not clipped.
2. **Hotkey during Transcribing/Pasting:** ignore, or (design decision) queue one pending "start" to run after Idle. Do not allow concurrent pipelines. The recommended v1 behavior is to ignore and flash the pill, which is simplest and predictable.
3. **Cancel is valid from Arming, Recording and Transcribing** (Esc or a second quick tap in the cancel window). It passes a `CancellationToken` to the engine and discards the result. It is not valid in Pasting, which is milliseconds long.
4. **Max recording length:** hard cap (for example 5 minutes) that auto-stops, to bound memory and cloud upload size.
5. **Min recording length / silence gate:** under ~300 ms or below an energy threshold, go straight to Idle without calling the engine. This also prevents Whisper hallucinations on silence ("Obrigado por assistir", "Legendas pela comunidade...").

```csharp
// Sketch: single-writer loop
while (await events.Reader.WaitToReadAsync(ct))
  while (events.Reader.TryRead(out var e)) {
    var (next, effects) = Reducer.Reduce(state, e, settings);   // pure, unit-tested
    state = next;
    foreach (var fx in effects) await Dispatch(fx);             // StartMic, StopMic, StartTranscribe, Paste, ...
    ui.Publish(UiState.From(state, level));                     // UI subscribes
  }
```

Making `Reduce` a pure function (state, event → state, effects) gives you table-driven tests for every transition including the ugly ones (stop while Arming, cancel while Transcribing, error during Pasting).

### Pattern 2: Ports and adapters with a Fake for every port

**What:** `IHotkeySource`, `IAudioSource`, `ITranscriber`, `ITextOutput`, `IClock`, `IForegroundWindow`, `ISettingsStore`. Production wires Win32/WASAPI/Whisper; tests wire fakes that are scriptable.
**When to use:** Always. It is the single biggest factor in whether agents can verify work unattended.
**Trade-offs:** More interfaces, but there are only about 7, each tiny.

```csharp
public interface ITranscriber {
  Task<TranscriptResult> TranscribeAsync(PcmBuffer audio, TranscribeOptions opts, CancellationToken ct);
  Task WarmUpAsync(CancellationToken ct);          // no-op for cloud/fake
  EngineStatus Status { get; }                      // NotLoaded | Loading | Ready | Failed(reason)
}
public interface IAudioSource {                     // fake = plays a WAV at real or accelerated rate
  IAsyncEnumerable<AudioFrame> StartAsync(DeviceId? dev, CancellationToken ct);
}
public interface ITextOutput { Task<OutputResult> DeliverAsync(string text, DeliverOptions o, CancellationToken ct); }
```

### Pattern 3: Pipeline stages decoupled by bounded channels (audio path)

**What:** Capture callback → lock-free/bounded queue → resample/level/accumulate worker. The audio callback does the minimum (copy bytes into a ring/channel) and returns.
**When to use:** The audio path always. Doing work or taking locks inside the audio callback causes glitches and dropped frames.
**Trade-offs:** One extra thread, in return for glitch-free capture and a trivially fakeable source.

### Pattern 4: Engine registry with capability flags and a shared contract test

**What:** `ITranscriber` implementations register under an id (`local-whisper`, `openai`, `groq`). The settings value selects one. A shared contract suite (same WAV fixtures, same assertions) runs against every implementation that is available.
**When to use:** Because the project needs both local and cloud and "highest quality pt-BR" is the core value, you will want to compare engines objectively (WER on the same fixtures) and switch without touching the controller.
**Trade-offs:** Slight indirection, but it enables objective model selection (large-v3-turbo vs alternatives) by running the same script.

## Threading Model

Rule of thumb: **never block the UI thread, never do work in the audio callback, never run inference on a thread that owns a message loop.**

| Thread | Owns | Talks to others via |
|--------|------|---------------------|
| **UI thread (STA, message pump)** | Overlay, tray, settings window, animation timer (~60 fps render of the latest level). | Receives `UiState` via dispatcher/`SynchronizationContext.Post`. Sends commands (Cancel, Quit, Open settings) into the controller queue. |
| **Hotkey thread (own message loop)** | Low-level keyboard hook. The hook callback must return in well under a second (Windows silently removes slow hooks), so it only timestamps and posts to a channel, never does work. | Posts `KeyDown/KeyUp` to the TriggerInterpreter, which posts `Toggle/PushStart/PushStop` to the controller queue. |
| **Audio capture thread (OS-owned WASAPI callback)** | Raw frame delivery only. Copy and enqueue. No allocation, no logging, no locks, no engine calls. | Bounded channel to the audio-processing worker. |
| **Audio-processing worker** | Resample to 16 kHz mono, compute level, append to the recording buffer, post `LevelTick` events (throttled to ~30 Hz) to the UI. Level goes to the UI directly (a volatile latest-value), not through the controller queue, so a slow controller never stalls the waveform. | Channel in; volatile level out; `FramesAvailable` to the controller only when needed. |
| **Controller loop (one logical thread / single-consumer queue)** | State machine and effect dispatch. Fast, never blocks. | Everything else posts to its queue. |
| **Inference worker (dedicated thread or long-running task, not the thread pool)** | Model load and `Transcribe`. CPU-heavy for seconds; whisper.cpp and CTranslate2 use their own internal thread pools. Set the inference thread count to the physical-core count (6 on a 5600X) rather than all 12, and keep one core free for UI and audio. Cancellation via token (whisper.cpp supports abort callbacks). | Receives a `PcmBuffer` (immutable), returns `TranscriptResult` as an event to the controller. |
| **IO worker (thread pool)** | Model download, cloud HTTP calls, spooling WAVs, log writes. | Async/await. |
| **Paste step** | Runs on the controller's effect dispatcher, but the clipboard and `SendInput` calls need care (see Pitfalls): the clipboard is process-global and `OpenClipboard` can fail transiently, so retry a few times with short delays. | |

Key cross-thread contracts:

- `PcmBuffer` is **immutable** once handed off. No shared mutable arrays between audio and inference.
- Only the controller writes state. The UI only reads `UiState` snapshots. The overlay animation reads the latest level from a single volatile float.
- Every long operation takes a `CancellationToken` linked to a per-dictation `CancellationTokenSource`, which `Cancel` and `Quit` trip.
- The controller should start the transcribe effect and **return to its loop immediately**; completion re-enters as an event. That keeps Cancel responsive while inference runs.

## Model Loading and Warm-up (latency)

The user-visible latency is **stop → text pasted**. Cold model load (loading a ~1.6 GB large-v3-turbo model, plus first-inference kernel/graph setup) can take several seconds and must never land on the first dictation.

1. **Preload at app start, in the background.** Right after the tray appears, `EngineHost.WarmUpAsync` loads the model on the inference worker. The tray tooltip/tint shows "loading model". If the user hits the hotkey before it is ready, record normally. Transcription then waits on the same load task (a `TaskCompletionSource`), and the pill shows "transcribing" the whole time, so nothing is lost and nothing needs a special UI.
2. **Run a dummy inference to finish warm-up.** After loading, transcribe ~1 s of silence or a bundled tiny clip. The first real call otherwise pays one-time costs (backend init, Vulkan pipeline compile or shader cache, memory page-in). Treat "warm" as loaded plus one dry run.
3. **Keep the model resident.** On 16 GB RAM this is fine for one model. Add an optional "unload after N idle minutes" setting, default off. Never reload per dictation.
4. **Load once per engine/model change, not per settings edit.** Changing hotkey or auto-paste must not touch the model. Switching engine unloads the old one after the new one is ready.
5. **Overlap work with the user's speech.** Model warm-up (when it was not preloaded) and Vulkan init can proceed while the user is still talking. Audio spooling, silence trimming and resampling are done incrementally during capture, so at stop only the final inference remains.
6. **Trim before inference.** Cutting leading/trailing silence reduces decode time and cuts hallucinations. Whisper's cost scales with the 30 s window, so for short utterances prefer an engine and settings that do not pad to a full 30 s window if the backend permits it.
7. **Cloud engine:** "warm-up" is validating the key and opening a keep-alive HTTP connection (HTTP/2) to the provider at app start or on settings save. Encode to FLAC or Opus before upload to cut transfer time (WAV for 30 s of 16 kHz mono is ~1 MB, fine, but compressed is faster on slow uplinks). Keep a retry with backoff, plus a fallback option to the local engine on API failure if the model is present.
8. **Model manager gating:** if the model file is missing, the app must start anyway (tray present), show "model missing" in the overlay on first hotkey, and offer a download. It must never crash on a missing model. This is also what makes first-run testable.
9. **Expose a `Status` and a metrics event** (`load_ms`, `warm_ms`, `infer_ms`, `audio_ms`, real-time factor) written to the log on every dictation. Agents need this to verify latency budgets in CI-style runs.

Budget guidance (to be validated in the engine phase on the target hardware): the "paste within a few seconds for 5-30 s utterances" requirement is a realistic target for large-v3-turbo on a 6-core CPU with int8 or on Vulkan. This is the single biggest quality/latency unknown and should be measured early, not assumed.

## Data Flow

### Primary flow: one dictation

```
[Hotkey down]  (hook thread)
    ↓ KeyDown/KeyUp
[TriggerInterpreter]  tap(<~280ms) → Toggle ; hold → PushStart … PushStop on release
    ↓ event
[DictationController]  Idle → Arming ── effect: capture foreground HWND, show pill
    ↓ effect StartMic
[IAudioSource / WASAPI]  frames ──► [AudioProcessor] ──► PcmBuffer (16k mono, in memory)
                                         └─► level(30 Hz) ──► [PillOverlay waveform]
    ↓ Toggle/PushStop
[DictationController]  Recording → Transcribing ── effect: spool WAV to disk
    ↓ effect Transcribe(PcmBuffer)
[PreProcessor] trim/VAD/gate ─► [ITranscriber (local | cloud)] ─► TranscriptResult
    ↓ event TranscriptReady            (empty ⇒ Idle, "nothing heard")
[TextPostProcessor]  trim / normalize
    ↓ effect Deliver(text)
[ITextOutput]  set clipboard ─► verify ─► (refocus target if needed) ─► release modifiers ─► SendInput Ctrl+V
    ↓ event Pasted
[DictationController]  Pasting → Idle ── delete spool WAV, log metrics, hide pill
```

Direction summary: Hotkey → Controller → Audio → (PCM) → Engine → (text) → Output. UI is downstream-only (subscriber). Settings flow in from the UI and notify Hotkey, Engine and Audio adapters through change events, but only the controller applies them, and only while Idle.

### State management

```
UiState (immutable snapshot, published by controller)
    ↓ subscribe
[Pill / Tray / Settings status]      level: volatile float read by the render timer
    │ user actions
    ▼
[Commands: Cancel, OpenSettings, Quit, DownloadModel] → controller queue → reducer → new state
```

### Key data flows

1. **Audio path:** device format → resample → 16 kHz mono PCM → immutable `PcmBuffer` → WAV spool (safety) and engine input. Level metering is a side-branch, not part of the data path.
2. **Config path:** `ISettingsStore` → `AppSettings` (immutable) → adapters read on construction/change. Changes are applied when Idle (hotkey re-registration, mic device, engine switch → warm the new one). The API key is read through `ISecretStore` and never logged.
3. **Model path:** `ModelManager` ensures the file exists and is verified → `EngineHost` loads it → `LocalWhisperTranscriber` serves requests. A failed load produces `EngineStatus.Failed` surfaced in the tray and overlay.
4. **Failure path:** any adapter throws, which becomes an `Error` event with a user-safe message, shown in the pill, logged with detail. The spooled WAV is kept so the recording is recoverable ("open last recording" in the tray menu is a cheap v1 safety valve).

## Testability Design (headless, agent-verifiable)

Requirement: every phase must be verifiable by agents without the owner. The architecture above is shaped for this. Test layers, cheapest first:

### Layer 1: Pure unit tests (Core.Tests), milliseconds, no devices

- `Reducer` transition table: every (state, event) pair, including illegal ones, cancel races and the max-duration auto-stop.
- `TriggerInterpreter` with a fake clock: tap vs hold thresholds, key repeat (hold generates repeated KeyDown, so ignore repeats), modifier-order variations, release without press.
- `LevelMeter`, `SilenceTrimmer`/VAD, `TextPostProcessor`, `WavIo`, settings migration/validation.

### Layer 2: Controller scenario tests with fakes

Wire the real `DictationController` with `FakeAudioSource` (plays a WAV, either real-time or accelerated), `FakeTranscriber` (returns scripted text, configurable delay/failure/hang), `FakeTextOutput` (records what was delivered), `FakeClock`. Assert the full sequence of `UiState`s and effects. This covers: normal flow, hold-to-talk flow, cancel mid-transcription, engine error, no-mic error, paste failure retains clipboard, hotkey spam, and "hotkey pressed before model warm".

### Layer 3: Engine contract tests on WAV fixtures

- A single contract suite runs against `FakeTranscriber` always, and against `LocalWhisperTranscriber` (and `CloudTranscriber` if a key env var is present, otherwise skipped, never failed) on real fixtures.
- **Fixtures:** short (3-5 s), medium (15 s) and long (>30 s, to exercise chunking) pt-BR clips, plus silence, white noise, a 200 ms click, and a clip with a long leading pause. Store each with an `expected.txt`.
- **Assertion = WER/CER threshold, not exact match** (normalize case and punctuation, accents kept, since accents matter in pt-BR). Example: WER <= 0.15 on clean TTS. Also assert `infer_ms` below a budget and that silence returns empty text (hallucination guard).
- **Generating pt-BR test audio with TTS (reproducible, checked-in script `tools/gen_fixtures`):**
  - *Windows SAPI / `System.Speech` or WinRT `SpeechSynthesizer`* with a pt-BR voice (for example Microsoft Maria or Daniel). Zero download and offline, but the voice must be installed (Settings → Language → Portuguese (Brazil) → Speech). The script should detect and report if no pt-BR voice exists.
  - *Piper* with a pt-BR voice (`pt_BR-faber-medium` or similar): offline, free, deterministic, pip-installable. Good second voice for diversity.
  - *edge-tts* (neural pt-BR voices, for example `pt-BR-FranciscaNeural`): best naturalness, needs internet at generation time only. Generated WAVs are committed, so tests never need the network.
  - Mix voices, then post-process: add low-level noise, vary the speaking rate and volume, convert to 16 kHz mono and also to 44.1/48 kHz stereo to exercise the resampler.
  - Caveat to record in the tests' README: synthetic speech is cleaner than real speech, so WER here is a regression guard and engine-comparison tool, not a true accuracy measure. Add 1-2 real recordings made once through the actual mic path if possible, but never block on them.
- **Mic-path fidelity:** for a real-device smoke test use a virtual audio cable (for example VB-Cable) so the real WASAPI path can be fed a WAV. Treat this as optional and flagged, since the driver install may not be possible unattended. The `FakeAudioSource` already covers pipeline logic.

### Layer 4: Platform integration tests (real Win32, no AI model needed)

- **Clipboard + paste:** spawn a minimal test window (or classic Notepad) in the test process, give it focus, run `ClipboardPasteOutput.DeliverAsync("olá, ação não é fácil")`, and read back via `WM_GETTEXT`. Include accented characters, newlines, long text and an emoji to prove Unicode handling.
- **Hotkey:** inject synthetic key events with `SendInput` (Ctrl+Shift+Space down, wait N ms, up) and assert the `IHotkeySource` emits the right sequence. Low-level hooks do see `SendInput` events, so this works headlessly in an interactive session. Mark the `LLKHF_INJECTED` flag handling explicitly: **do not filter injected events**, or your own tests will not trigger.
- **Overlay:** create the pill, assert via `GetWindowLong` that it has `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, that it is bottom-center on each monitor, that showing it does not change `GetForegroundWindow()`, and take a screenshot (`PrintWindow`/`BitBlt` or `Graphics.CopyFromScreen`) for non-blank pixel and waveform-variation assertions.
- **Single instance, tray:** second launch exits and signals the first; the tray icon exists (check via UIA of the notification area, or expose a diagnostic query).

### Layer 5: End-to-end into Notepad (scripted, the "proof of life")

Test harness in `tests/Somning.E2E`:

1. Launch the real app executable with `--test-harness` (or env var `SOMNING_TEST=1`). This swaps in `FakeAudioSource` backed by a named WAV fixture (and, where a model is present, the real local engine; otherwise `FakeTranscriber` for a plumbing-only run), and exposes a local control channel (named pipe or loopback HTTP, test-mode only, never in release builds) with `status`, `trigger-toggle`, `wait-state Idle`, and `last-metrics`.
2. Launch `notepad.exe`, wait for its window, bring it to the foreground (`SetForegroundWindow` with the AttachThreadInput trick; the test runner is itself an interactive-session process).
3. Either inject the real hotkey with `SendInput` (exercises the hook) or call the control channel. Both variants should exist: the hotkey variant is the true end-to-end, the pipe variant is the stable one.
4. Wait for state `Idle`, read the Notepad edit text via `WM_GETTEXT` (Win10 classic Notepad exposes a plain Edit control) or UI Automation `ValuePattern`/`TextPattern`. Assert it matches the expected text with the WER tolerance. Read the clipboard and assert that **the text remains in the clipboard** (explicit requirement).
5. Repeat the run with Notepad closed or minimized, with a read-only field, and with an elevated target, to assert graceful behavior (see UIPI in Pitfalls).
6. Capture a screenshot at "Recording" and "Transcribing" instants for the overlay evidence, and save metrics JSON as the artifact for the verifying agent.

Constraint to note for the roadmap: UI-level tests (SendInput, focus, overlay) require an **interactive desktop session**. They cannot run in Session 0 or a disconnected RDP session. Agents on this machine that run in the logged-in console session are fine; document this prerequisite and keep Layers 1-3 runnable without a desktop.

### Testability checklist per component

| Component | Headless verification |
|-----------|----------------------|
| TriggerInterpreter / state machine | Pure unit tests, fake clock |
| AudioRecorder | Fake source in logic tests; WASAPI adapter smoke test against default device (skipped if none) |
| Engine | Contract tests on WAV fixtures, WER threshold, latency metrics |
| ModelManager | Local HTTP test server serving a tiny fake model; resume, checksum-mismatch and partial-file cases |
| Paste | Win32 integration test into a test window / Notepad via `WM_GETTEXT` |
| Overlay | Style-flag assertions, foreground-unchanged assertion, screenshot pixel checks |
| Settings | Round-trip, corrupted-file recovery, migration, secret encryption |
| Whole app | E2E with harness and Notepad |

## Build Order (dependencies between components)

Each step ends with something an agent can verify without the owner. Earlier steps de-risk the unknowns that could force a stack or design change (quality/latency on this AMD hardware, paste reliability).

1. **Project skeleton, Core types, ports, fakes, state machine + trigger interpreter, settings model.** No dependencies. Verified by Layer 1 and 2 tests. This fixes the interfaces everything else plugs into.
2. **Engine spike and contract tests (local first).** Depends on: PcmBuffer/WavIo (tiny). Includes the TTS fixture generator, the WER scorer, the model download, load/warm-up, and a latency/WER benchmark of candidate runtimes and models on the 5600X / RX 7600. **This is the highest-risk item (quality vs latency, Vulkan vs CPU); do it early and let it confirm or overturn the engine choice** before building more around it. Verified by the contract suite and benchmark output.
3. **Audio capture + resampler + level meter + spool.** Depends on: ports, PcmBuffer. Verified with fake source in logic tests, plus a real-device smoke test.
4. **Output / paste adapter.** Depends on: nothing but the port. Verified by the Notepad / test-window integration test. Second highest risk (focus, modifiers still held, UIPI, clipboard contention), so build it before the UI polish.
5. **Hotkey service (low-level hook) + hybrid tap/hold.** Depends on: the TriggerInterpreter from step 1. Verified via `SendInput` injection tests.
6. **Wire the headless pipeline end to end (no UI):** composition root, controller with real adapters, a `--test-harness` control channel, and the E2E Notepad test with fake or real engine. At this point the product's **core value works without any UI**, and it is already verifiable. Everything after is presentation and distribution.
7. **Overlay pill with waveform + transcribing/error states.** Depends on: `UiState` and level feed. Verified by window-style assertions, foreground-unchanged check and screenshots.
8. **Tray icon, single-instance, settings window + persistence, secret storage.** Depends on: settings model, controller commands. Includes hotkey re-registration, mic selection and engine switch applied live.
9. **Cloud engine(s) behind the same contract (OpenAI, Groq), error mapping, retries, fallback.** Depends on: engine port and contract suite. Can be built in parallel with 7-8 once step 2 exists. Verified against recorded HTTP responses (mock server) always, and against the real API only if a key is present.
10. **Model manager UX (first-run download, progress in overlay/tray, missing-model handling), warm-up polish, metrics logging.** Depends on: engine host, overlay.
11. **Packaging: installer/portable exe, start-with-Windows (HKCU Run or startup shortcut), code/self-signing decision, log folder shortcut.** Depends on: everything. Verified by installing into a clean directory and running the E2E suite against the installed binary.

Parallelization notes for agents: after step 1, steps 2 (engine), 3 (audio), 4 (output) and 5 (hotkey) are independent and can go to separate agents behind the fixed interfaces. Step 6 is the integration point. Steps 7-9 can again run in parallel.

## Scaling Considerations

This is a single-user desktop tool, so user scale is irrelevant. The relevant "scale" axes are utterance length and resource use:

| Concern | Typical (5-30 s) | Long (1-5 min) | Notes |
|---------|------------------|----------------|-------|
| Memory | ~1 MB per 30 s of 16 kHz int16; model ~1.6 GB resident | ~10 MB per 5 min, trivial | Keep PCM as int16 or float32; no problem either way |
| Inference time | seconds | scales linearly with 30 s chunks | Chunk at silence boundaries, not fixed 30 s cuts, to avoid cutting words; carry the last text as the prompt for the next chunk for continuity |
| Cloud upload | ~1 MB WAV | provider file limits (OpenAI 25 MB) | Compress or chunk; enforce max recording length |
| CPU contention | Inference briefly uses all cores | Same, longer | Lower inference thread priority slightly so the UI and the foreground app stay responsive |

### Scaling priorities

1. **First bottleneck:** cold model load and first-inference setup, solved by preload plus a dry run.
2. **Second bottleneck:** decode time for long utterances, solved by silence-based chunking, trimming, and (if needed) a smaller or faster model variant exposed as a setting.

## Anti-Patterns

### Anti-Pattern 1: Doing the work in the hotkey hook or audio callback

**What people do:** Start recording, or even run logic, directly inside the keyboard hook or the audio callback.
**Why it's wrong:** A slow low-level hook is silently unhooked by Windows after its timeout, and the hotkey just stops working until restart. A blocked audio callback drops frames and glitches the recording.
**Do this instead:** Hook and callback only timestamp and enqueue. All decisions happen in the controller.

### Anti-Pattern 2: Paste while the hotkey's modifiers are still physically down

**What people do:** Send Ctrl+V immediately when the user releases the key, while Ctrl/Shift/Alt from the hotkey are still held, or send synthetic key-ups that confuse the real state.
**Why it's wrong:** The target app sees Ctrl+Shift+V (paste-without-formatting, or a different command), or Shift+Ctrl+Space residue. Results are inconsistent per app.
**Do this instead:** Because paste happens after a multi-second transcription, the modifiers are normally already up, but still check `GetAsyncKeyState` for Ctrl/Shift/Alt/Win and wait (bounded, ~500 ms) until they are released before `SendInput`. Send a clean Ctrl down, V down, V up, Ctrl up sequence as one `SendInput` batch.

### Anti-Pattern 3: UI thread owns the pipeline

**What people do:** Run record, transcribe and paste in UI event handlers or a UI-owned worker (common in early whisper-writer-style apps).
**Why it's wrong:** The overlay freezes during inference (the waveform stops, the "transcribing" spinner stalls), and state races appear.
**Do this instead:** Controller and inference run off the UI thread; the UI renders snapshots.

### Anti-Pattern 4: Overlay that steals focus

**What people do:** Show a normal topmost window, which becomes the foreground window.
**Why it's wrong:** The target field loses focus, so the paste goes to the pill or to nowhere. This is the cardinal failure of this product category.
**Do this instead:** `WS_EX_NOACTIVATE` plus `WS_EX_TOOLWINDOW`, show with `SW_SHOWNOACTIVATE`, never call `Activate()`/`Focus()`. In WPF set `ShowActivated=false`. Add a test that `GetForegroundWindow()` is unchanged by showing the pill. Also record the foreground HWND at record-start and, if it changed by paste time, refocus it (optionally) or warn.

### Anti-Pattern 5: Model loaded per request (or on first use only)

**What people do:** Load the model inside the transcribe call.
**Why it's wrong:** Several seconds of extra latency on every dictation, or on the first one.
**Do this instead:** Preload at startup, run a dry-run inference, keep resident.

### Anti-Pattern 6: Global mutable state and boolean flags (`isRecording`, `isTranscribing`)

**What people do:** Several independent flags set from several threads.
**Why it's wrong:** Impossible states (recording and transcribing at once), double-starts, missed stops.
**Do this instead:** One state enum owned by one writer, with a pure reducer.

### Anti-Pattern 7: Trusting transcription output on silence

**What people do:** Send any captured audio to Whisper.
**Why it's wrong:** Whisper hallucinates plausible phrases on silence or noise, and that text then gets pasted into the user's document.
**Do this instead:** Energy/VAD gate before the engine, `no_speech`/log-prob thresholds, and a post-filter for known hallucination strings. Add silence and noise fixtures to the contract suite.

### Anti-Pattern 8: Losing the recording on failure

**What people do:** Keep audio only in memory until transcription succeeds.
**Why it's wrong:** An API timeout or crash destroys the user's speech, which violates an explicit requirement.
**Do this instead:** Spool to disk at stop, delete after successful paste, keep the last few, and expose "retry last recording" or "open last recording" from the tray.

### Anti-Pattern 9: Test-only behavior compiled into release as an open door

**What people do:** Leave a control channel enabled so tests can drive the app.
**Why it's wrong:** A local control pipe or HTTP port in the production build is an attack surface.
**Do this instead:** Gate the harness behind an explicit flag (and ideally a compile-time define), bind loopback or a pipe ACL'd to the current user, and verify that the release build rejects the flag.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| OpenAI `gpt-4o-transcribe` (audio/transcriptions) | `ITranscriber` impl over HTTPS multipart upload; `language=pt`; optional `prompt` | Upload size limit (25 MB), timeouts and 429/5xx retry with backoff; map errors to user-facing messages; key from DPAPI-protected store |
| Groq `whisper-large-v3-turbo` | Same shape (OpenAI-compatible endpoint) | One shared client with a base-URL and model parameter makes both providers nearly the same class |
| Model host (Hugging Face / provider mirror) | `ModelManager` HTTPS download with Range-resume and SHA-256 | Pin exact file URLs and hashes in the app; never execute downloaded content; write to `.partial` then atomic rename |
| Windows APIs | Adapters in Platform assembly | `SetWindowsHookEx(WH_KEYBOARD_LL)`, `SendInput`, clipboard APIs, WASAPI, `Shell_NotifyIcon`, DPAPI, `RegisterApplicationRestart`/Run key for autostart |
| TTS tooling (dev/test only) | Fixture generator script | Not shipped with the app |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Hotkey adapter → Controller | Events on the controller queue | Adapter owns the thread; no direct calls into controller state |
| Controller → Audio | `IAudioSource` start/stop effects; frames return as events or a channel | Level takes the fast path to the UI directly |
| Controller → Engine | `ITranscriber.TranscribeAsync` with token; result returns as an event | Controller never awaits inline in its loop |
| Controller → Output | `ITextOutput.DeliverAsync`; result returns as an event | Output runs after Transcribing completes; has its own bounded retry |
| Controller → UI | `IObservable<UiState>` / event | One-way. UI → controller only via commands |
| Settings → everyone | Change events, applied by the controller while Idle | Hotkey re-register and engine swap are effects, not direct subscriber reactions |
| EngineHost ↔ ModelManager | Path + status only | ModelManager never loads models; EngineHost never downloads |

## Open Questions for Phase-Specific Research

- Local runtime choice on RX 7600 (whisper.cpp Vulkan vs CPU int8 via faster-whisper/CTranslate2) is a STACK/benchmark question, but it affects only `LocalWhisperTranscriber` thanks to the port. The architecture does not depend on the answer.
- Whether the overlay renderer should be WPF, WinUI, or a web view (Tauri/Electron). For a tiny always-on-top click-through pill, a native borderless window is lighter and more reliable about focus and DPI. This needs a decision in STACK.md.
- Exact tap-vs-hold threshold and key-repeat handling needs a quick empirical tune. Start at ~250-300 ms.
- Whether to refocus the original window if focus changed during transcription (convenient but surprising). Default recommendation: paste into whatever is focused now, and only warn when the original window is gone.
- UIPI: a non-elevated Somning cannot inject keys or paste into elevated windows (Task Manager, admin terminals). Decide whether to run elevated (undesirable) or document the limitation; the paste adapter should detect and report it rather than failing silently.
- Virtual audio cable availability for a real-mic-path smoke test in unattended runs.

## Sources

- Structure and component conventions drawn from knowledge of the open-source dictation apps OpenWhispr, Whispering (Epicenter), Handy (cjpais/Handy, Tauri/Rust: recorder + VAD + model manager + paste), VoiceInk (macOS: Recorder / WhisperState / paste manager) and whisper-writer (savbell/whisper-writer, Python: ResultThread). Not re-read in this session. LOW-MEDIUM confidence on exact internals, HIGH confidence on the shared overall shape.
- Win32 behavior (low-level hook timeout and thread requirements, `RegisterHotKey` lacking key-up, `WS_EX_NOACTIVATE`, `SendInput` and UIPI, injected-event flag `LLKHF_INJECTED`, clipboard contention): Microsoft Win32 documentation, from knowledge. MEDIUM-HIGH confidence; verify specifics during the phase that builds each adapter.
- Whisper behavior (30 s window, silence hallucinations, prompt carry-over for chunking, thread count guidance): whisper.cpp / OpenAI Whisper / faster-whisper documentation and community experience, from knowledge. MEDIUM confidence.
- TTS fixture options (Windows SAPI/WinRT pt-BR voices, Piper, edge-tts): from knowledge; availability of a pt-BR SAPI voice on this machine is unverified and must be checked by the fixture-generation phase.

---
*Architecture research for: Windows push-to-talk voice dictation (Somning)*
*Researched: 2026-10-22*
