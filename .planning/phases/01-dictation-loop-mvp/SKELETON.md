# Walking Skeleton — Somning

**Phase:** 1
**Generated:** 2026-10-22

## Capability Proven End-to-End

`Somning.App.exe --transcribe-file <pt-BR wav>` copies the WAV into the recordings spool, starts the `Somning.Engine.exe` worker, the worker transcribes it locally with Whisper large-v3-turbo (Vulkan or CPU), and the punctuated pt-BR text ends up on the Windows clipboard. One log line records the result. This is plan 01-01, Task 1, and `SkeletonRoundTripTests` checks it.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Language / runtime | C# on .NET 10 (SDK 10.0.203), `net10.0` for pure libraries and `net10.0-windows` for Win32 and engine projects | Already installed and LTS. The pure Core can be tested without a desktop. Research found that a plain `net10.0` engine copies Linux/macOS natives |
| UI framework | None in Phase 1. The App is a WinExe with a hidden message-only window | The Phase 1 goal allows "no UI beyond logs". WPF arrives in Phase 2 for the overlay and tray |
| Process model | `Somning.App` (composition root) plus a child `Somning.Engine.exe` per backend, talking stdin/stdout JSON Lines in UTF-8 (D-03) | Native whisper.cpp crashes stay out of the App. Whisper.net cannot switch backends in-process because the loaded library is a static Lazy, so CPU fallback means restarting the worker (D-04) |
| STT engine | Whisper.net 1.9.1 + Runtime + Runtime.Vulkan 1.9.1 (whisper.cpp 1.8.5), ggml large-v3-turbo, flash attention on, Silero v6.2.0 VAD gate | Verified on this Win10 19045 machine with an RX 7600. Flash attention is about 6.6x faster on Vulkan. Final defaults are recorded in BENCHMARK.md |
| Data layer | Files only: `%LOCALAPPDATA%\Somning\{models,recordings,logs}`. Each spool WAV has a JSON sidecar recording its completion stage (D-11) | No database is needed. Recordings must survive crashes (AUD-04). `SOMNING_DATA_DIR` and `SOMNING_MODELS_DIR` override the paths for tests |
| Auth | None (single local user). The harness pipe is current-user-only and Debug-only (D-20) | Personal desktop tool. There is no network listener |
| Win32 interop | Microsoft.Windows.CsWin32 0.3.367 via `NativeMethods.txt` | Generated signatures avoid struct-size bugs. Pitfall: use the raw-handle overloads for SetClipboardData and GetModuleHandle |
| Audio | NAudio 3.3.0: `WasapiRecorderBuilder` at the device mix format, then `WdlResamplingSampleProvider` to 16 kHz mono s16, then `WaveFileWriter` with a Flush about every second | Matches D-10. The new non-obsolete APIs are used |
| Logging | Serilog 4.4.0 + Sinks.File 7.0.0, rolling daily with 7-day retention, one metrics line per dictation, redaction policy, no transcript text (D-23) | APP-06 and privacy |
| Tests | xunit.v3 4.0.2 on Microsoft Testing Platform (`global.json` test runner). Core.Tests are pure. Integration.Tests drive the real engine, hook, Notepad and clipboard and run serially. With `SOMNING_STRICT=1`, a skip guard becomes a failure | Every phase must be agent-verifiable. MTP exits with code 8 when a filter matches no tests |
| Deployment target | Phase 1: a self-contained win-x64 directory publish (`publish/app` with `publish/app/engine`) that runs with no SDK on PATH (gate.ps1). Phase 4 adds the installer and must ship the VC++ 2015-2022 x64 redistributable | Checked by the D-16 gate. Single-file and trimming are not used, to keep the native runtime layout intact |
| Directory layout | `src/Somning.{Core,Engine.Protocol,Platform.Windows,Engine,App}`, `tests/Somning.{Core,Integration}.Tests`, `tests/fixtures`, `tools/` (fetch-models, gate, make-fixtures, Somning.Bench, verify-phase1). Inside each project, code is organized in feature folders (Trigger/, Dictation/, Recording/, Output/, Hook/, Audio/, Paste/, Clipboard/) | D-01 (costly to change, and locked by CONTEXT) |
| Core pattern | Ports and adapters with a pure `DictationController` reducer, driven by one single-writer event loop. `TriggerInterpreter`, `PasteGate`, `PostFilter`, `RestartPolicy` and `RecoveryPlanner` are pure and table-tested | The risky decisions stay deterministic and testable without a desktop |

## Stack Touched in Phase 1

- [x] Project scaffold: solution, central package versions, warnings as errors, xunit.v3 MTP runner (plan 01)
- [x] "Routing" equivalent: App CLI modes (`--transcribe-file`, normal mode, `--test-harness`) and the JSONL message types (plans 01 and 07)
- [x] Persistence: real writes of the spool WAV and sidecar, and real reads during startup recovery (plans 03 and 07)
- [x] Interaction: the global hotkey drives recording, then clipboard and Ctrl+V into the focused app (plans 02, 04 and 07)
- [x] Deployment: published directory build running without the SDK (`tools/gate.ps1`, plus `-App` in plan 08), and the one-command `tools/verify-phase1.ps1`

## Out of Scope (Deferred to Later Slices)

- Overlay pill, waveform, tray menu, settings UI, Esc cancel, sounds, single instance, first-run model download UI (Phase 2)
- Custom vocabulary UI (STT-07), hotkey change and conflict warning (Phase 2)
- Cloud engines Groq/OpenAI (Phase 3)
- Installer, autostart, sleep/resume hook watchdog, paste matrix (Phase 4)
- Pre-roll ring buffer with an always-open mic (rejected for privacy), per-app paste method, transcript logging (deferred ideas)

## Subsequent Slice Plan

Each later phase adds one vertical slice on top of this skeleton without changing its architectural decisions:

- Phase 2: the user sees and controls dictation day to day. WPF overlay and tray attach to the reducer's state stream, and settings feed EngineDefaults and TriggerOptions
- Phase 3: the user can opt into cloud transcription through a second transcriber behind the same EngineTranscription contract
- Phase 4: the user installs Somning on a clean machine. The publish layout is packaged with the VC++ redistributable, and the hook gets a watchdog
