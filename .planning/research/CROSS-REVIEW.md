# Independent Stack Cross-Review

Reviewed: 2026-10-22  
Scope: `.planning/research/STACK.md`, informed by `.planning/PROJECT.md`  
Target: Windows 10 Pro 19045 x64; Ryzen 5 5600X; 16 GB RAM; Radeon RX 7600 8 GB

## Recommendation and evidence limits

Keep C#/.NET WPF for the Windows shell, but make Whisper.net/Vulkan conditional on an early deployment and inference test. The architecture is reasonable; STACK.md overstates Windows compatibility, fallback reliability, performance and quality equivalence.

This review checked the planning documents, current NuGet metadata, official documentation and upstream source, including Whisper.net's 1.9.1 tag. No inference benchmark, clean-machine deployment test or published-application validation was performed. A documented support mismatch is distinguished below from a demonstrated runtime failure. Package versions reflect the review-time lookup, not an instruction to upgrade without testing.

## CRITICAL

No confirmed CRITICAL defect was established by this document/source review. In particular, the Windows 10 native-runtime issue below is a potential MVP blocker, not proof that the binaries cannot execute.

## HIGH

### H1. Whisper.net's documented OS requirement conflicts with the sole target

**Evidence:** STACK.md:183 assumes Windows 10 compatibility. The Whisper.net 1.9.1 README explicitly requires Windows 11/Server 2022 or newer for the CPU runtime; Vulkan inherits those prerequisites. It also lists the VS2022 C++ redistributable and Vulkan Toolkit >=1.4.321.1. This does not prove the binaries fail on Windows 10, but compatibility cannot be marked established. The statement that only an updated AMD driver is needed remains an unverified deployment assumption.

**Impact:** Both the preferred local engine and its advertised CPU fallback may lack documented support on the only target OS. A working developer machine could conceal missing deployment dependencies.

**Correction:** Make inference from the published build on Windows 10 19045 the first acceptance gate. Test CPU and RX 7600 Vulkan independently, without developer SDKs on PATH. Verify the actual driver, loader and C++ runtime dependencies. If unsuccessful, build ABI-compatible native libraries with a Windows 10-compatible toolchain, or use a tested whisper.cpp worker. Changing UI frameworks would not resolve this native dependency problem. Do not assume a generic prebuilt sidecar automatically restores Windows 10 compatibility; validate its binaries too.

**Source:** [Whisper.net 1.9.1 prerequisites](https://github.com/sandrohanea/whisper.net/blob/1.9.1/readme.md).

### H2. Automatic fallback is substantially overstated

**Evidence:** STACK.md:36,88,183 promises Vulkan -> CPU after initialization failures or exceptions. The native loader selects a library during loading; the selected implementation is cached through a static `Lazy`. This differs from retrying model creation or inference after a GPU failure. A native crash can terminate the application before managed recovery runs.

**Impact:** The app can crash or remain unable to transcribe despite the presence of a CPU runtime package. Changing runtime order after native initialization does not establish a fresh runtime selection.

**Correction:** Separate library-load fallback, recoverable model errors and process crashes. For the strongest reliability, use a restartable local inference worker with explicit GPU/CPU startup modes. A separate process running Whisper.net is also possible; HTTP whisper-server is not mandatory. Configure runtime selection before native initialization. If retaining inference inside the UI process, acknowledge the crash boundary and test recovery explicitly. Recreating a model with GPU disabled is distinct from switching the loaded native runtime and also needs validation.

**Sources:** [Native loader](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/LibraryLoader/NativeLibraryLoader.cs), [cached implementation](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/Internals/WhisperLibrary.cs), [runtime options](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/LibraryLoader/RuntimeOptions.cs).

### H3. Paste handling can target the wrong field and disturb keyboard state

**Evidence:** STACK.md:111 recommends synthesizing releases for physically held modifiers. These releases change the input state seen by other applications while the user is still holding the keys. An overlay preserving focus also does not prevent the user switching applications during transcription. `SendInput` targets current input focus and is restricted by UIPI.

**Impact:** Text may land in an unintended application or field; synthetic releases can disrupt ongoing shortcuts. Ordinary unelevated input cannot guarantee delivery into elevated applications, contradicting an unconditional “every app” promise.

**Correction:** Record the intended target. Before delivery, verify focus and wait a bounded period for physical modifiers to be released. If the target changed or remains unsuitable, keep the text on the clipboard and report that paste was deferred. Check the inserted-event count. Explicitly qualify elevated-app support. Do not silently steal focus to force delivery or claim success merely because input events were submitted.

**Source:** [Microsoft SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

### H4. Recording preservation is only addressed for cloud errors

**Evidence:** STACK.md:101 specifies retaining cloud WAVs, but the proposed local path has no equally explicit durable recording/recovery contract. Local native crashes, cancellation and application termination can also lose recordings.

**Impact:** The app can violate PROJECT.md's requirement to never silently lose a recording even if cloud error handling is correct.

**Correction:** Persist capture incrementally before inference, retain the original untrimmed audio, and recover pending recordings after restart. Define completion separately for transcription, clipboard placement and paste. A successful transcription followed by clipboard failure must remain recoverable. Test failure during capture finalization, inference and delivery; define cleanup only after the recoverable result has been secured.

**Sources:** `.planning/PROJECT.md`, active graceful-error requirement; `.planning/research/STACK.md:101`; [native implementation boundary](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/Internals/WhisperLibrary.cs).

## MEDIUM

### M1. Version/API guidance is stale and installation is not reproducible

**Evidence:** STACK.md:34-52,123-137 mixes Whisper.net 1.9.0 and runtime 1.9.1, leaves several versions unspecified, and installs CsWin32 with `--prerelease` despite an available stable release.

Current stable versions returned by NuGet during this review:

| Package | Stable version | Concrete correction |
|---|---|---|
| Whisper.net | 1.9.1 | Pin together with matching runtimes. |
| Whisper.net.Runtime | 1.9.1 | Validate Windows 10 separately. |
| Whisper.net.Runtime.Vulkan | 1.9.1 | Windows x64 is documented; RX 7600 deployment still needs verification. |
| NAudio | 3.3.0 | Choose current APIs or deliberately pin a tested 2.x version. |
| Microsoft.Windows.CsWin32 | 0.3.367 | Stable exists; remove `--prerelease`. |
| Velopack | 1.2.161 | Pin the packaging tool and application integration. |

NAudio 3 retains `WaveInEvent` as an obsolete alias for `WaveIn`; `WasapiCapture` is obsolete in favor of `WasapiRecorderBuilder`/`WasapiRecorder`. Therefore the examples need an explicit API/version choice. This is not evidence that the legacy aliases immediately fail to compile.

**Impact:** An autonomous agent following the unversioned commands may receive different APIs from those described, introduce avoidable warnings or take unintended prerelease dependencies.

**Correction:** Pin exact package versions before implementation, keep the Whisper managed/native package set aligned, and make restore reproducible. For NAudio, either adopt and test the 3.3 API deliberately or keep a tested 2.x baseline with matching examples. Treat CsWin32 as a source generator: declare the required APIs in `NativeMethods.txt` and validate generated usage in the target project.

**Sources:** [Whisper.net](https://www.nuget.org/packages/Whisper.net), [CPU runtime](https://www.nuget.org/packages/Whisper.net.Runtime), [Vulkan runtime](https://www.nuget.org/packages/Whisper.net.Runtime.Vulkan), [NAudio](https://www.nuget.org/packages/NAudio), [CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32), [Velopack](https://www.nuget.org/packages/Velopack), [NAudio migration guide](https://github.com/naudio/NAudio/blob/main/Docs/MigratingFromNAudio2.md), [NAudio 3.3.0 WaveIn source](https://github.com/naudio/NAudio/blob/v3.3.0/src/NAudio.WinMM/WaveIn.cs), [NAudio 3.3.0 capture source](https://github.com/naudio/NAudio/blob/v3.3.0/src/NAudio.Wasapi/WasapiCapture.cs).

### M2. Silero VAD support is confirmed, but its integration is described incorrectly

**Evidence:** STACK.md:86,200 leaves Whisper.net VAD exposure unresolved. Whisper.net 1.9.1 exposes a separate VAD pipeline:

`WhisperVadFactory.FromPath(...)` -> `CreateBuilder()` -> `Build()` -> `DetectSpeechAsync(...)`.

Its builder exposes `WithThreshold`, `WithMinSpeechDuration`, `WithMinSilenceDuration`, `WithMaxSpeechDuration`, `WithSpeechPadding`, `WithSamplesOverlap`, thread count and GPU options. These are not automatic settings on `WhisperProcessorBuilder`; the application must apply detected segments to transcription. The tagged example uses Silero v6.2.0. The tagged model-type enum includes both v5.1.2 and v6.2.0.

**Impact:** An agent could add an unnecessary ONNX runtime dependency or assume VAD automatically trims the transcription input. A hard RMS gate can discard quiet speech while accepting non-speech noise.

**Correction:** Remove the speculative need for a separate ONNX dependency. Describe the separate VAD stage and how speech regions are consumed. Preserve padding and the original recording. Treat RMS as an energy heuristic, not proof of speech or a guarantee against hallucinations. Test quiet speech and boundary consonants before enabling destructive trimming.

**Sources:** [VAD builder](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/WhisperVadProcessorBuilder.cs), [VAD example](https://github.com/sandrohanea/whisper.net/blob/1.9.1/examples/Vad/Program.cs), [Silero model types](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/Ggml/SileroVadType.cs).

### M3. Performance and pt-BR quality need acceptance criteria, not equivalence claims

**Evidence:** STACK.md:68 calls runtime choice “purely speed/packaging,” while its latency table is explicitly unmeasured on the target hardware. Decoding settings, quantization, VAD and segmentation can change output. OpenAI documents minor degradation from turbo's decoder reduction; that does not establish equivalent Brazilian Portuguese accuracy across the proposed configurations. The claim that a 5-15 second CPU fallback meets “a few seconds” is also not established.

**Impact:** A technically functioning fallback may fail the latency requirement, and an apparently faster configuration may lose words, names or numbers. TTS and silence fixtures alone cannot establish natural dictation quality.

**Correction:** Compare f16/q8/q5 with matched decoding settings on natural pt-BR recordings: short commands, names, numbers, technical vocabulary, pauses, quiet speech and background noise. Measure warm p50/p95 stop-to-paste, cold start and repeated-run stability. Define explicit latency and quality thresholds before selecting a default. Treat the current timing table as hypotheses, not acceptance evidence. Keep language fixed to `pt`; do not infer a guaranteed Brazilian dialect/style benefit from a generic prompt alone.

**Sources:** [OpenAI turbo model card](https://huggingface.co/openai/whisper-large-v3-turbo), [faster-whisper implementation and benchmarks](https://github.com/SYSTRAN/faster-whisper), `.planning/PROJECT.md` latency and quality requirements.

### M4. Packaging needs a verified native layout and startup integration

**Evidence:** STACK.md:23,59,116 assumes self-contained single-file deployment settles distribution. Self-contained .NET does not settle C++/Vulkan dependencies. Whisper.net's package copies DLLs into a runtime directory tree, so “single EXE” needs artifact verification. Velopack requires `VelopackApp.Build().Run()` early in startup; its WPF guide recommends a custom `[STAThread] Main`.

**Impact:** Development builds may work while the installed executable fails native probing or Velopack lifecycle handling. A nominally self-contained installer may still download prerequisites, which matters for offline installation expectations.

**Correction:** Start with a self-contained directory publish inside one installer, preserving native paths. Verify installation and inference before optimizing executable bundling. Integrate Velopack startup explicitly. Audit actual native prerequisites and use a target-compatible redistributable; do not assume installing the newest redistributable restores Windows 10 compatibility. If prerequisites are bootstrapped over the network, distinguish offline operation after installation from fully offline installation.

**Sources:** [Vulkan packaging targets](https://github.com/sandrohanea/whisper.net/blob/1.9.1/runtimes/Whisper.net.Runtime.Vulkan/Whisper.net.Runtime.Vulkan.targets), [Velopack WPF integration](https://docs.velopack.io/getting-started/csharp), [Velopack prerequisite bootstrapping](https://docs.velopack.io/packaging/bootstrapping).

### M5. Hook reliability is asserted too strongly; asynchronous work cannot decide suppression later

**Evidence:** STACK.md:20,107,156 describes the managed hook as “rock solid,” attributes comparative Python unreliability to GC/startup, and says the callback only enqueues events while a separate state machine decides the chord. Windows timeout/removal behavior applies regardless of implementation language. Suppression must be decided synchronously by the callback's return value. Windows 10 1709+ caps the timeout at 1,000 ms; the documentation does not establish the proposed Python-versus-.NET reliability comparison.

**Impact:** An implementation that defers all recognition can leak Space into the target application. Blocking or allocation-heavy callbacks can silently lose the hook in either language.

**Correction:** Keep minimal synchronous chord recognition and suppression bookkeeping in the hook callback; enqueue recording, UI and inference work. Run a dedicated message loop, keep the callback alive, and avoid blocking operations. Test auto-repeat, modifier release ordering, tap/hold boundaries and injected-input handling. Replace categorical language claims with measured callback behavior and end-to-end checks.

**Source:** [Microsoft LowLevelKeyboardProc documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc).

## LOW

### L1. “Never use Tauri/Rust” is not supported by the evidence

**Evidence:** STACK.md:149 categorically excludes Tauri alongside other UI alternatives. Handy demonstrates the requested offline speech-to-text, global shortcut and paste workflow using Tauri/Rust. Its existence does not prove Somning should adopt that stack, but it disproves treating the approach as categorically unsuitable.

**Correction:** Replace “never” with a scoped tradeoff: WPF is the preferred fresh implementation for this Windows-only project and installed .NET toolchain; Tauri/Rust is viable, especially if adapting a proven codebase. For a new implementation it adds Rust/native/frontend boundaries without inherently improving Whisper's pt-BR accuracy. Evaluate native runtime compatibility and crash isolation independently from the UI framework.

**Source:** [Handy upstream repository and workflow](https://github.com/cjpais/Handy).

### L2. .NET runtime compatibility and Microsoft support status should be distinguished

**Evidence:** STACK.md:185 broadly states “Windows 10 1809+” and “Win10 19045 fine.” The review-time .NET 10 support matrix does not list Windows 10 Pro 22H2 as a supported OS entry. This is a support-policy limitation, not proof that .NET 10/WPF cannot run on the target.

**Correction:** State the precise deployment target and validate it directly. Document separately the application's observed compatibility, Microsoft's OS/runtime support policy, and Whisper.net's stricter native prerequisites. Do not infer that switching to another .NET version automatically resolves OS lifecycle or native compatibility issues.

**Source:** [.NET 10 supported OS matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

## Option assessment

The recommendations below are engineering judgments informed by the documented capabilities, not measured comparative buildability or latency results on this hardware.

| Option | Reliability and deployment | pt-BR quality | Autonomous agent buildability | Assessment for Somning |
|---|---|---|---|---|
| C#/WPF + Whisper.net inside the UI process | Direct Windows integration; native/driver crashes share the UI process. Current native package OS support needs validation. | Strong candidate with turbo; validate decoding, quantization and VAD. | Installed SDK, typed APIs and straightforward headless tests; native probing remains a risk. | Best default shell choice; conditional approval for inference inside the UI process. |
| C#/WPF + inference sidecar | Strongest crash boundary among these proposed layouts; requires readiness, timeout, cancellation and restart handling. Sidecar OS compatibility must still be tested. | Same whisper.cpp model/settings can be used; isolation itself does not improve recognition. | Adds process/protocol work but makes failures and backend restarts explicit and testable. | Prefer when reliability outweighs a single-process preference, or native crashes appear. |
| Python + faster-whisper | Credible CPU baseline. Documented GPU execution requires NVIDIA CUDA/cuDNN, offering no documented RX 7600 acceleration advantage. | Competitive candidate; parity is not automatic across decoding/settings. | Good fixture-driven testing; packaging native dependencies and desktop integration still require work. | Benchmark as a CPU alternative; no demonstrated reason to replace the WPF shell. |
| Tauri/Rust like Handy | Viable existing implementation pattern for offline dictation, shortcuts and paste. Native GPU failures remain relevant. | Does not inherently improve Whisper's pt-BR accuracy. | Adapting a proven codebase may help; a fresh implementation adds Rust/native/frontend boundaries. | Valid alternative, not categorically unsuitable; WPF remains simpler for this project's narrow Windows scope. |

**Sources:** [Whisper.net](https://github.com/sandrohanea/whisper.net), [faster-whisper requirements](https://github.com/SYSTRAN/faster-whisper), [Handy](https://github.com/cjpais/Handy), [Microsoft input-injection constraints](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

Handy's current documentation also describes disabling Vulkan implicit overlay/capture layers on Windows to avoid crashes. This is a concrete reason to include overlay/capture-tool coexistence in GPU stability testing; it is not evidence that this exact failure occurs on the owner's RX 7600.

**Source:** [Handy Vulkan overlay/capture guidance](https://github.com/cjpais/Handy#vulkan-overlays-and-capture-tools-on-windows-handy_keep_vulkan_implicit_layers).

## MVP acceptance gates

1. Successfully run CPU and Vulkan inference from the published artifact on Windows 10 19045, without relying on developer SDKs or accidental PATH dependencies.
2. Establish measured stop-to-paste latency and natural pt-BR accuracy for the chosen model, quantization and decoding settings. Classify CPU fallback as degraded service if it misses the latency threshold.
3. Demonstrate recording recovery after inference failure or worker termination, and preserve results after clipboard/paste failure.
4. Demonstrate safe target-aware paste, modifier handling and visible clipboard-only fallback for changed focus or blocked input.
5. Verify tap/hold behavior, key suppression, installed startup and repeated recording/transcription cycles through automated checks plus target-machine smoke tests.

The immediate potential MVP blocker is packaged local inference on the exact Windows 10/RX 7600 target. The evidence supports retaining WPF while resolving that uncertainty before committing to the native runtime and process layout.
