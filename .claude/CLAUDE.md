<!-- GSD:project-start source:PROJECT.md -->

## Project

**Somning**

Somning is a Windows 10 desktop voice-dictation utility (in the spirit of Claude Code's `/voice` mode and Wispr Flow). The user presses a global hotkey, a small pill overlay with an animated waveform (WhatsApp-voice-note style) appears at the bottom-center of the screen while it listens, and when recording stops the speech is transcribed with the highest possible quality in Brazilian Portuguese, copied to the clipboard and auto-pasted (Ctrl+V) into whatever field is focused. Built for a single user (the owner) for daily use across every app.

**Core Value:** Press the hotkey, speak Portuguese, and accurate text lands in the focused field (and stays in the clipboard) — reliably, every time, in any app.

### Constraints

- **Platform**: Windows 10 Pro 10.0.19045 x64 — sole target
- **Language**: pt-BR only in v1 — owner's request; keep language as a config value so v2 can expand
- **Cost**: local engine must be free and offline-capable — cloud is optional opt-in
- **Simplicity**: "functional and simple" — minimal UI, no feature bloat
- **Latency**: text should paste within a few seconds after stop for typical 5–30 s utterances
- **Autonomy**: every phase must be verifiable by agents without the owner (automated tests; scripted runs)

<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->

## Technology Stack

## Decision Summary

| Question | Decision | Fallback |
|----------|----------|----------|
| App language/UI | **C# / .NET 10 + WPF** (self-contained single-file publish) | Python 3.13 (only if Whisper.net proves unusable) |
| Local STT | **whisper.cpp via Whisper.net, Vulkan runtime, ggml large-v3-turbo (q5_0 or f16)** | Same package, CPU runtime (AVX2) with q5_0/q8_0; second fallback: whisper-server.exe sidecar |
| Cloud STT | **Groq whisper-large-v3-turbo** (cheap, fast) and **OpenAI gpt-4o-transcribe** (highest quality), plain HttpClient multipart | whisper-1 / gpt-4o-mini-transcribe |
| Hotkey | Own `WH_KEYBOARD_LL` hook via CsWin32 | SharpHook |

## 1. App Language / Runtime: C# / .NET 10 WPF

- **Hotkey / low-level hook reliability (decisive):** tap-vs-hold needs raw key-down/key-up events with timestamps and suppression of the key so the target app does not receive Space. `WH_KEYBOARD_LL` from a native-speed managed thread with a dedicated message pump is rock solid in .NET; Python hooks (`keyboard`, `pynput`) run callbacks on the GIL and Windows silently unhooks a LL hook if the callback exceeds the timeout (default ~300 ms), which Python GC pauses/PyInstaller startup make more likely. `RegisterHotKey` cannot detect key-up so it is unsuitable for hold-to-talk.
- **Overlay quality:** WPF gives GPU-composited per-pixel-alpha windows, `CompositionTarget.Rendering` 60 fps animation, and trivial extended-style control (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_TOPMOST`). Python (Qt/tkinter) can do it but with more packaging weight and DPI quirks.
- **Whisper integration:** Whisper.net binds whisper.cpp directly in-process, with a Vulkan runtime package, so GPU acceleration on the RX 7600 needs no CUDA and no sidecar. The Python equivalents (faster-whisper) have no AMD GPU path on Windows at all (CPU only).
- **Packaging:** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` -> roughly 70-150 MB exe incl. runtime (the model, ~0.55-1.6 GB, is downloaded/stored separately in `%LOCALAPPDATA%\Somning\models`). PyInstaller/Nuitka bundles with onnx/ctranslate2 DLLs are comparable or larger and slower to start.
- **Agent buildability/testability:** .NET 10 SDK is already installed; `dotnet build/test` gives deterministic compile errors for agents; xUnit with WAV fixtures tests the STT path headlessly; the hotkey state machine, VAD and paste logic are pure classes testable without a desktop. Strongly typed P/Invoke via CsWin32 removes the most common agent mistake (wrong Win32 signatures).

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET SDK / runtime | 10.0 (LTS) | Runtime | Already installed; LTS; self-contained publish needs no user install |
| WPF (`net10.0-windows`) | built-in | Overlay + settings window | Per-pixel alpha, GPU animation, mature; WinUI 3 adds packaging pain (MSIX/Windows App SDK) for no gain |
| Whisper.net | 1.9.0 stable (1.9.1-preview1 exists; verify latest stable on nuget at install) | whisper.cpp binding | In-process, streaming segments, supports VAD/ggml models; one NuGet set covers GPU+CPU |
| Whisper.net.Runtime.Vulkan | 1.9.1 (match Whisper.net major.minor; verify) | Vulkan GPU backend, Windows x64 | Only practical AMD GPU path without ROCm; needs a Vulkan 1.3-capable driver (AMD Adrenalin already ships it) |
| Whisper.net.Runtime | 1.9.0 | CPU (AVX2) runtime | Automatic fallback; Whisper.net picks runtime by priority (CUDA > Vulkan > CPU) and falls back if Vulkan init fails |
| ggml-large-v3-turbo(-q5_0).bin | from ggerganov/whisper.cpp HF repo | Model | 4 decoder layers vs 32 -> ~6-8x faster than large-v3 at near-equal WER; q5_0 ~574 MB, f16 ~1.6 GB |
| Microsoft.Windows.CsWin32 | latest stable (verify) | Source-generated P/Invoke (hooks, SendInput, window styles) | Eliminates hand-written signature bugs |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| NAudio | 2.2.1 (verify latest) | Mic capture (`WaveInEvent`/`WasapiCapture`), device enumeration, resample | Capture at 16 kHz/16-bit/mono directly with `WaveInEvent` (MME resamples); RMS/peak per 20-30 ms buffer for waveform |
| System.Windows.Forms `NotifyIcon` | built-in (`<UseWindowsForms>true`) | Tray icon + context menu | Zero-dependency, reliable; H.NotifyIcon.Wpf is an acceptable alternative but adds a dependency for no needed feature |
| System.Security.Cryptography.ProtectedData | built-in | DPAPI (`DataProtectionScope.CurrentUser`) for API key | Encrypt key into settings.json (base64); no Credential Manager lib needed |
| System.Text.Json | built-in | Settings at `%APPDATA%\Somning\settings.json` | Source-gen context for trimming safety |
| Microsoft.Win32.Registry | built-in | Autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | No admin needed, trivially testable |
| Serilog (+Sinks.File) | 4.x (verify) | Rolling log file for tray "Open logs" | Needed for autonomous debugging |
| Microsoft.Extensions.Http / HttpClient | built-in | Cloud STT calls | Multipart form upload; no vendor SDK needed |
| Silero VAD ggml (`ggml-silero-v5.1.2.bin`) via whisper.cpp VAD | whisper.cpp >=1.7.6 VAD params | Trim silence before decode | See VAD section |
| Velopack | latest stable (verify) | Installer + auto-update + shortcuts | Simple `vpk pack` for Setup.exe; alternative is a plain zip/Inno Setup |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| xUnit + FluentAssertions-style asserts | Unit/integration tests | Fixture WAVs (synthetic tone, silence, and a TTS-generated pt-BR sentence) checked into `tests/fixtures` |
| `dotnet publish` single-file self-contained | Release build | `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`; do NOT use NativeAOT/trimming aggressively (WPF + Whisper.net native loading) |
| FlaUI (optional) | UIA E2E: launch app, send hotkey, assert text in Notepad | Scripted E2E for autonomous verification; pair with a `--selftest wav.wav` CLI flag that runs hotkey-less pipeline |

## 2. Local STT Runtime

- CTranslate2 has no AMD GPU backend on Windows (CUDA only) so it is CPU-bound on the 5600X. whisper.cpp Vulkan offloads the encoder (the heavy part, ~30 s window regardless of clip length) to the RX 7600.
- Same model weights, so pt-BR quality is equivalent (see below); the runtime choice is purely speed/packaging.
- Keeps everything in one process and one language (no Python sidecar, no 100+ MB ctranslate2/onnx wheels).

| Backend | 10 s clip | 30 s clip | Notes |
|---------|-----------|-----------|-------|
| whisper.cpp Vulkan, turbo f16/q5_0, RX 7600 | ~1-2.5 s | ~2-4 s | Encoder dominates; Vulkan on RDNA3 is mid-tier vs CUDA. Includes warm model; first call adds model load (~1-3 s) - preload at app start |
| whisper.cpp CPU AVX2, turbo q5_0, 5600X (6 threads) | ~4-8 s | ~8-15 s | Roughly 0.3-0.5x real-time factor; acceptable fallback |
| faster-whisper CPU int8, turbo, 5600X | ~4-9 s | ~8-16 s | Comparable to whisper.cpp CPU; no advantage here |
| Groq cloud | ~0.5-1 s (+ upload) | ~1 s | ~200x real-time server side |

## 3. Cloud Engines

| Engine | Endpoint | Price | Notes |
|--------|----------|-------|-------|
| Groq `whisper-large-v3-turbo` | `POST https://api.groq.com/openai/v1/audio/transcriptions` (OpenAI-compatible) | $0.04 / audio hour; 10 s billing minimum per request | Same model as local -> consistent pt quality; ~200x real-time; free-tier rate limits; file limit 25 MB free / larger on dev tier. Best default cloud choice (cost + speed) |
| OpenAI `gpt-4o-transcribe` | `POST https://api.openai.com/v1/audio/transcriptions` | ~$0.006/min (~$0.36/h) | Best accuracy/lowest WER of the family, strong in pt; supports `language=pt` and `prompt`; no word timestamps. Choose for "highest quality" |
| OpenAI `gpt-4o-mini-transcribe` | same | ~$0.003/min | Cheaper; slightly lower quality |
| OpenAI `whisper-1` | same | $0.006/min | Legacy (large-v2); only one with SRT/VTT/word timestamps - not needed. Don't use |

## 4. Libraries Per Capability

| Capability | Choice | Rationale |
|------------|--------|-----------|
| Global hotkey, tap vs hold | Custom `WH_KEYBOARD_LL` hook (CsWin32 `SetWindowsHookEx`) on a dedicated thread with its own message loop; callback only enqueues events to a `Channel<T>`; a pure `HotkeyStateMachine` class decides tap/hold (e.g. hold if key down > ~250-300 ms -> release stops; short tap -> toggle). Swallow the chord key (return 1) while armed | Full control of down/up, suppression, and unit-testable state machine; avoids dependency risk. Alternative: **SharpHook** (libuiohook wrapper; NuGet, verify version) if hook code proves fiddly. Not `RegisterHotKey` (no key-up), not NHotkey |
| Audio capture 16 kHz mono + level | NAudio `WaveInEvent` (WaveFormat 16000, 16, 1, BufferMilliseconds=30) | Native resampling by driver; compute RMS per buffer -> `IObservable`/event to overlay; device selection via `WaveInEvent.GetCapabilities`. Fall back to `WasapiCapture` + `MediaFoundationResampler` if a device refuses 16 kHz |
| Overlay | WPF `Window`: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost`, `ShowInTaskbar=False`, `ShowActivated=False`; in `SourceInitialized` set `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_TRANSPARENT` (click-through); position bottom-center of the monitor under the foreground window, above taskbar via `SystemParameters.WorkArea`. Waveform drawn via `DrawingVisual`/`Canvas` bars updated in `CompositionTarget.Rendering` with exponential smoothing of RMS; states: listening / transcribing (spinner or shimmer) / error | Non-activating so paste target keeps focus; GPU-composited; smooth 60 fps. Skia not needed |
| Tray icon | WinForms `NotifyIcon` with `ContextMenuStrip` (Settings, Open logs, Quit) | Simple, stable; hide the main window at launch |
| Clipboard + paste | `Clipboard.SetText` (STA thread, retry on `CLIPBRD_E_CANT_OPEN` up to ~5x/50 ms) then `SendInput` Ctrl+V. **Before sending, release any physically-held modifiers/hotkey keys (synthesize key-up for Ctrl/Shift/Space/Alt) and wait ~30-50 ms**, then send V down/up with Ctrl, using `KEYEVENTF_SCANCODE`-agnostic virtual-key input. Mark injected input so own hook ignores it (`LLKHF_INJECTED`) | Hotkey modifiers still down would turn Ctrl+V into Ctrl+Shift+V - classic bug. Leave text in clipboard per requirement. Caveat: cannot paste into elevated (admin) windows from a non-elevated process (UIPI) |
| Settings persistence | JSON in `%APPDATA%\Somning\settings.json` via System.Text.Json; atomic write (temp + rename) | Simple, human-editable, testable |
| API key storage | DPAPI `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` stored base64 in settings | No plaintext on disk; Credential Manager (e.g. `Meziantou.Framework.Win32.CredentialManager`) is an equally valid option but DPAPI needs zero deps |
| Autostart | HKCU `Run` key, value = quoted exe path with `--minimized`; toggle in settings | No admin, no scheduled task needed |
| Settings UI | Small WPF window (hotkey recorder, engine dropdown, API key PasswordBox, mic dropdown, auto-paste checkbox) | "Functional and simple" |
| Installer | Velopack (or Inno Setup) | One-file Setup.exe, per-user install, no admin |

## Installation

# csproj: <UseWindowsForms>true</UseWindowsForms> (NotifyIcon), <RuntimeIdentifier>win-x64</RuntimeIdentifier>

# tests

# model (store in %LOCALAPPDATA%\Somning\models)

# https://huggingface.co/ggerganov/whisper.cpp -> ggml-large-v3-turbo.bin or ggml-large-v3-turbo-q5_0.bin

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| C#/.NET 10 WPF | Python 3.13 + uv + PySide6/pywhispercpp | Only if Whisper.net Vulkan fails and a sidecar is not acceptable; accepts weaker hook reliability and larger bundle |
| Whisper.net (in-proc) | whisper-server.exe sidecar (HTTP) | If native DLL loading/crashes in-proc take down the app; isolates crashes, costs extra process management |
| whisper.cpp Vulkan | faster-whisper CPU int8 | If Vulkan shows driver bugs/hallucination differences and CPU latency (est. 5-15 s) is tolerable |
| Own LL hook | SharpHook | If agents struggle with hook threading; adds libuiohook native dependency |
| WinForms NotifyIcon | H.NotifyIcon.Wpf | If WPF-native tray menus/XAML templating are wanted |
| DPAPI | Windows Credential Manager | If the user wants key visible in Windows Credential UI |
| WPF | WinUI 3 / Avalonia / Tauri / Electron | Never for this app: transparency/click-through/non-activating and tray are harder or heavier (Electron ~150 MB+, WinUI 3 needs Windows App SDK) |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `RegisterHotKey` for the main trigger | No key-up event, so hold-to-talk impossible | `WH_KEYBOARD_LL` |
| Python `keyboard`/`pynput` hooks in a bundled exe | GIL/GC stalls can exceed hook timeout and Windows silently removes the hook; antivirus flags PyInstaller exes more | C# hook thread |
| Whisper.net.Runtime.Cuda / CUDA anything | No NVIDIA GPU | Vulkan + CPU runtimes |
| whisper-1 as a cloud default | Older large-v2 model, same price as gpt-4o-transcribe | gpt-4o-transcribe / Groq turbo |
| OpenAI Realtime/streaming transcription | Out of scope (transcribe-on-stop), costs ~3x | Batch endpoint |
| Native AOT / aggressive trimming | Breaks WPF and native-library probing | Plain self-contained single-file |
| `Clipboard.SetText` without retry, or SendInput while modifiers are held | Intermittent paste failures / wrong shortcut | Retry + release modifiers first |
| Focus-taking overlay (default `Show()` activation) | Steals focus; paste lands in the wrong place | `ShowActivated=False` + `WS_EX_NOACTIVATE` |
| Storing API key in plaintext JSON | Credential leak | DPAPI |
| Large-v3 (non-turbo) locally | ~6-8x slower for marginal gain | large-v3-turbo |

## Stack Patterns by Variant

- Ship CPU runtime with q5_0 turbo, keep Vulkan as an opt-in setting, preload the model at startup.
- Because CPU latency of ~5-15 s for 30 s clips is still within "a few seconds" for typical 5-15 s utterances.
- Default cloud engine = gpt-4o-transcribe with a pt-BR prompt; Groq turbo as the cheap tier.
- Fall back to leaving text in clipboard and show overlay toast "copiado" (requirement keeps text in clipboard anyway).

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| Whisper.net 1.9.x | Whisper.net.Runtime* 1.9.x (keep identical) | Mismatched native/managed versions cause load errors |
| Whisper.net.Runtime.Vulkan | Windows x64 + Vulkan loader/driver >= 1.3 (NuGet page lists a toolkit >= 1.3.290 / 1.4.321 requirement; end-user needs only an up-to-date Adrenalin driver, verify on clean machine) | Auto-selected by priority CUDA > Vulkan > CPU; fall back to CPU on init error |
| ggml model files | whisper.cpp version bundled in Whisper.net | Use models from the whisper.cpp HF repo; newer ggml formats may need newer runtime |
| WPF `net10.0-windows` | Windows 10 1809+ | Win10 19045 fine; per-monitor DPI via app manifest `PerMonitorV2` |

## Sources

- https://www.nuget.org/packages/whisper.net.runtime.vulkan - Vulkan runtime 1.9.1, Windows x64 (HIGH)
- NuGet profile of Whisper.net author - Whisper.net 1.9.0 stable / 1.9.1-preview1 (MEDIUM, snapshot dated early 2026)
- https://console.groq.com/docs/model/whisper-large-v3-turbo and https://groq.com/pricing - $0.04/h, 10 s minimum (HIGH)
- Third-party OpenAI price aggregators (costgoat.com/pricing/openai-transcription, diyai.io) - $0.006 / $0.003 per min (MEDIUM; verify on openai.com/api/pricing)
- whisper.cpp Vulkan guides (github.com/ggml-org/whisper.cpp, RX 7600 Vulkan 1.3 capable) and VAD PR (ggml-org/whisper.cpp#3524, Silero VAD) (MEDIUM-HIGH)
- faster-whisper turbo issue #1030 and model cards - CTranslate2 GPU-oriented, no CPU Ryzen benchmark (LOW for CPU numbers)
- Training knowledge (unverified in this session): NAudio 2.2.1, Velopack, CsWin32, SharpHook versions - verify at install

## Gaps / Needs Phase-1 Validation

- Measure real Vulkan vs CPU latency on the 5600X / RX 7600 for 10 s and 30 s pt-BR clips (decide f16 vs q5_0).
- Confirm Whisper.net 1.9.x exposes whisper.cpp VAD parameters.
- Confirm Vulkan runtime loads on a machine without the Vulkan SDK installed (driver loader only).
- Confirm OpenAI current pricing page and Groq file-size limits.

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
