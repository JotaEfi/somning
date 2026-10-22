# Phase 1: Dictation Loop MVP - Research

**Researched:** 2026-10-22
**Domain:** .NET 10 Windows desktop: global LL keyboard hook, WASAPI capture, whisper.cpp (Whisper.net) in a worker process, clipboard + SendInput paste, xUnit E2E on the interactive desktop
**Confidence:** HIGH for the engine and native layout (probed with real published builds on this machine). MEDIUM for hook, paste and E2E focus handling (official docs plus partial probes). LOW where tagged [ASSUMED].

## Summary

The biggest Phase 1 risk was whether Whisper.net's native runtime runs on Windows 10 19045. This session tested it directly. A scratch `dotnet publish -r win-x64 --self-contained` of a console app using Whisper.net 1.9.1, Whisper.net.Runtime 1.9.1 and Whisper.net.Runtime.Vulkan 1.9.1 was run with `env -i PATH=C:\Windows\system32;C:\Windows`, so no SDK was on PATH. It loaded **both** the CPU and the Vulkan runtime, found the RX 7600 (`ggml_vulkan: 0 = AMD Radeon RX 7600 (AMD proprietary driver) ... matrix cores: KHR_coopmat`) and transcribed pt-BR TTS fixtures correctly with ggml-large-v3-turbo f16 and q5_0. The README still says "Windows 11 or Windows Server 2022 (or newer) is required". The D-16 gate must still be re-run inside the real solution, but it is now expected to pass, and the fallback path (rebuilding whisper.cpp) is unlikely to be needed. One caveat: the natives import `MSVCP140.dll`, `VCRUNTIME140(_1).dll` and `VCOMP140.DLL`. These came from the VC++ redistributable already installed on this machine (v14.51.36247), not from the publish folder.

The second biggest finding changes the benchmark design (D-17). **`WhisperFactoryOptions.UseFlashAttention = true` is decisive on Vulkan.** large-v3-turbo f16 on a 15.6 s clip took about 3.6 s warm without flash attention, which fails the 3.0 s budget, and about 0.55 s with it (q5_0: about 0.5 s). The CPU path (Ryzen 5600X, 6 threads) took about 15 s for the same clip even with flash attention, so CPU is "degraded mode" and can never meet the budget. Flash attention must be a matrix dimension and should be the default. The first Vulkan inference after load is slow (shader/pipeline warm-up: 4.0 s on tiny, 1.2 s on turbo+FA), so the D-05 warm-up is mandatory.

The third finding is about hallucinations, and it affects D-12. On silence the turbo model printed "Legenda por Sônia Ruberti" (with the initial prompt) and "E aí" (without it). On pink noise it printed "Aplausos." or "."; on a 440 Hz tone it printed ".". **`SegmentData.NoSpeechProbability` was 0.000 in every run, including silence**, so the D-12 `no_speech` threshold has no effect with this binding and model. Silero VAD v6.2.0 through `WhisperVadFactory` returned **0 segments** for the silence, noise, tone and 0.2 s tap fixtures. It still found speech at −20 dB, at −30 dB and with noise mixed in, all in 1–71 ms. The VAD gate is the real defense. The blocklist plus a "no letters/digits" filter is the safety net, and `no_speech` should be logged but not relied on.

**Primary recommendation:**
- Build `Somning.Engine` as a `net10.0-windows` console worker.
- Set `RuntimeOptions.RuntimeLibraryOrder = [Vulkan]` or `[Cpu]` (a single entry, chosen by `--backend`) before creating any `WhisperFactory`.
- Use `UseFlashAttention = true`, `.WithLanguage("pt").WithNoContext().WithThreads(6).WithPrompt(...)`, and Silero v6.2.0 VAD as the primary silence gate.
- Force UTF-8 on the worker's stdio and use InvariantCulture everywhere.
- Default to Vulkan + f16 (or q5_0) + FA, pending the full benchmark.

## Architectural Responsibility Map

The tiers here are processes and layers, not web tiers.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Hotkey chord recognition + suppression (HOTK-01) | Platform.Windows (hook thread, synchronous) | — | The suppression decision must come from the callback's return value; it cannot be deferred |
| Tap/hold timing, auto-repeat, missed key-up (HOTK-02/03) | Core (`TriggerInterpreter`, pure) | App (event queue) | Pure function of timestamped events + `IClock`; table-testable |
| Dictation state machine (reducer) | Core (`DictationController` reducer) | App (single-writer event loop) | Single writer; effects go to ports |
| Mic capture, downmix/resample to 16 kHz mono (AUD-01) | Platform.Windows (`WasapiRecorder` adapter) | Core (`IAudioSource` port; file-backed fake) | NAudio stays out of Core |
| Durable spool WAV + sidecar + recovery (AUD-04) | Core (recording store logic, completion states) | Platform (file IO) / App (startup recovery) | Recovery rules are pure; IO is behind a port |
| Duration gate + Silero VAD gate (STT-03) | Engine worker (VAD needs Whisper.net natives) | Core (post-filter/blocklist, pure) | VAD shares the native library and must run in the crash-isolated process |
| Whisper inference, warm-up, backend modes (STT-01/02/04) | Engine worker process | App (`EngineSupervisor`: spawn, ready, restart, CPU fallback) | Native crash boundary (CROSS-REVIEW H2) |
| Benchmark + gate (STT-08) | tools/ (benchmark CLI driving the published Engine) | Engine (same code path) | Must measure the published artifact, not the dev build |
| Clipboard set + verify (OUT-01) | Platform.Windows (STA thread with an owner HWND) | Core (`ITextOutput` port) | OpenClipboard needs an owner window |
| Target-aware Ctrl+V + UIPI check (OUT-02/03) | Platform.Windows (foreground/IL/SendInput) | Core (paste decision rules, pure) | The decision is pure; the Win32 calls are thin |
| Timing log line (APP-06) | App (Serilog composition) | Core (metrics record) | One structured line per dictation |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 10.0.203 (runtime 10.0.7) | Build/runtime | [VERIFIED: `dotnet --info` on this machine] |
| Whisper.net | **1.9.1** (published 2026-06-01; 1.9.2-preview1 exists — do not use) | whisper.cpp binding | [VERIFIED: NuGet registration API + tag 1.9.1 source]; bundles whisper.cpp **1.8.5** (`project("whisper.cpp" VERSION 1.8.5)` at submodule f24588a) |
| Whisper.net.Runtime | **1.9.1** | CPU (AVX2) natives `runtimes/win-x64/*.dll` | [VERIFIED: publish probe] |
| Whisper.net.Runtime.Vulkan | **1.9.1** | Vulkan natives `runtimes/vulkan/win-x64/*.dll` | [VERIFIED: publish probe; RX 7600 detected] |
| NAudio | **3.3.0** (published 2026-10-19; meta-package → NAudio.Core/Wasapi/WinMM/Midi 3.3.0) | Mic capture (`WasapiRecorder`), resampling, `WaveFileWriter` | [VERIFIED: NuGet + v3.3.0 source]. You can reference only `NAudio.Wasapi` 3.3.0 + `NAudio.Core` 3.3.0 instead of the meta-package. **Do not let a floating version resolve `NAudio.Wasapi` 22.0.0**: it is an unlisted entry dated 1900-01-01 |
| Microsoft.Windows.CsWin32 | **0.3.367** (published 2026-10-22, i.e. today; 0.3.366 is 3 days old) | Source-generated P/Invoke | [VERIFIED: scratch build with the NativeMethods.txt below compiled, 0 warnings after fix]. `PrivateAssets=all`, `AllowUnsafeBlocks=true` |
| xunit.v3 | **4.0.2** (2026-10-09) | Tests on Microsoft Testing Platform v2 | [VERIFIED: scratch project ran under `dotnet test` with global.json `"test": {"runner": "Microsoft.Testing.Platform"}`; `Assert.SkipUnless` reports "ignorado" (skipped)] |
| Serilog | **4.4.0** | Logging | [VERIFIED: NuGet stable list] |
| Serilog.Sinks.File | **7.0.0** | Rolling file | [VERIFIED: NuGet stable list] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.Json (in-box) | 10.0 | JSON Lines protocol, sidecars | Use the source-generated `JsonSerializerContext` |
| edge-tts (Python, dev-time only) | 7.3.1 [WARNING: flagged as suspicious — verify before using.] | pt-BR fixture generation | Run from a venv in `tools/make-fixtures`; outputs MP3 that is converted to 16 kHz WAV |
| ffmpeg (system) | present via winget | MP3→16 kHz mono WAV, −20 dB/noise mixing in fixture generation | Dev-time tool only. Fallback: NAudio `MediaFoundationReader` + `MediaFoundationResampler` |
| SAPI (System.Speech) | OS voices `Microsoft Maria Desktop pt-BR`, OneCore `MSTTS_V110_ptBR_DanielM/MariaM` | TTS fallback if edge-tts fails | [VERIFIED: enumerated on this machine] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `WasapiRecorder` + managed resample | `WaveIn` (renamed from WaveInEvent in NAudio 3) at 16 kHz | MME resampling is simpler but D-10 locks "device native format + resample"; WaveIn is still fine as an emergency fallback |
| Managed `WdlResamplingSampleProvider` | `WasapiRecorderBuilder.WithFormat(new WaveFormat(16000,16,1))` (engine AUTOCONVERTPCM + SRC_DEFAULT_QUALITY) | The engine conversion is simpler and also valid. It is not D-10's literal "capture native + resample" wording, so use it only if the managed path proves troublesome |
| Own Win32 clipboard | WinForms `Clipboard.SetDataObject(text, true, 10, 100)` | WinForms has retry built in, but OLE clipboard on an STA thread adds hidden behavior; own Win32 code matches D-13 (sequence-number verify) |

**Installation (Directory.Packages.props, central package management):**

```xml
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Whisper.net" Version="1.9.1" />
    <PackageVersion Include="Whisper.net.Runtime" Version="1.9.1" />
    <PackageVersion Include="Whisper.net.Runtime.Vulkan" Version="1.9.1" />
    <PackageVersion Include="NAudio.Core" Version="3.3.0" />
    <PackageVersion Include="NAudio.Wasapi" Version="3.3.0" />
    <PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.367" />
    <PackageVersion Include="Serilog" Version="4.4.0" />
    <PackageVersion Include="Serilog.Sinks.File" Version="7.0.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.2" />
  </ItemGroup>
</Project>
```

Root `global.json` (required for xunit.v3 4.x under the .NET 10 `dotnet test`):

```json
{ "sdk": { "version": "10.0.203", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" } }
```

## Package Legitimacy Audit

The seam (`gsd-tools package-legitimacy`) supports npm/pypi/crates only, so it was run for pypi. NuGet packages were checked against the NuGet registration API (publisher, dates, dependencies) and their official GitHub source tags.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| Whisper.net / .Runtime / .Runtime.Vulkan 1.9.1 | NuGet | 4.7 mo | (not queried) | github.com/sandrohanea/whisper.net (tag 1.9.1 cloned) | OK (manual) | Approved |
| NAudio(.Core/.Wasapi) 3.3.0 | NuGet | 3 days | (not queried) | github.com/naudio/NAudio (tag v3.3.0 cloned; author Mark Heath) | OK (manual), very new release | Approved: pin exactly |
| Microsoft.Windows.CsWin32 0.3.367 | NuGet | 0 days | (not queried) | github.com/microsoft/CsWin32 | OK (manual), very new | Approved. Fall back to 0.3.366 if a regression appears |
| Serilog 4.4.0 / Serilog.Sinks.File 7.0.0 | NuGet | stable | (not queried) | github.com/serilog | OK (manual) | Approved |
| xunit.v3 4.0.2 | NuGet | 13 days | (not queried) | github.com/xunit/xunit | OK (manual) | Approved |
| edge-tts 7.3.1 | PyPI | 9 days (project years old) | ~2.1M/week (pypistats) | github.com/rany2/edge-tts | **SUS** (seam: `too-new`, `unknown-downloads`) | Flagged. Dev-only tool in an isolated venv; output is committed fixtures. Planner adds a checkpoint, or pins `7.2.8` (2026-03-22) if 7.3.x misbehaves |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** edge-tts (dev-time only; the release is merely recent; installed and tested working in a scratch venv this session)

## Architecture Patterns

### System Architecture Diagram

```
 physical keyboard / test SendInput(marker=TEST)
          │
          ▼
 [Hook thread: WH_KEYBOARD_LL + GetMessage loop]
   ├─ injected && dwExtraInfo==PASTE_MARKER → CallNextHookEx (ignore)
   ├─ Ctrl+Shift held && VK_SPACE → return 1 (suppress), enqueue KeyEvent(t)
   └─ else → CallNextHookEx
          │ Channel<InputEvent>
          ▼
 [App event loop (single writer)] ──► TriggerInterpreter (pure) ──► DictationController reducer (pure)
          │ effects                                                       │
          ├─► IAudioSource.Start (WASAPI mix fmt → mono → 16k → s16) ─► spool {ts}.wav (Flush ~1 s) + {ts}.json "captured"
          ├─► at stop: capture TargetInfo(fg HWND, focus HWND, pid, IL)
          ├─► EngineSupervisor ──stdin JSONL──► [Somning.Engine.exe --backend vulkan|cpu]
          │        ▲                             1. duration < 0.4 s → empty
          │        │ stdout JSONL (UTF-8)        2. Silero VAD (WhisperVadFactory) → 0 segs → empty
          │        └──── result/error ◄──────── 3. Whisper turbo (FA, pt, NoContext, prompt) on full WAV
          │   (exit / timeout / gpu error → restart --backend cpu, retry once; ≤3 restarts/10 min)
          ├─► PostFilter (pure): normalize, blocklist, no-letters → empty
          ├─► sidecar "transcribed" → ClipboardWriter (owner HWND, retry, seq#) → "clipboard_set"
          └─► PasteGate: wait ≤1.5 s modifiers up → fg unchanged? target IL ≤ own IL? → SendInput×4 (PASTE_MARKER)
                 → "pasted" | "paste_deferred"  → Serilog one-line metrics
 App start: scan recordings/*.json not ≥ clipboard_set → re-transcribe → clipboard (no paste) → "recovered"
```

### Recommended Project Structure

```
Somning.sln
global.json  Directory.Build.props  Directory.Packages.props  .gitignore (models/, *.bin, bin/, obj/, publish/)
src/
  Somning.Core/                 net10.0         pure: Trigger/, Dictation/ (reducer, states, effects), Ports/, Text/ (PostFilter, Blocklist, Wer?), Recording/ (sidecar model, recovery rules)
  Somning.Engine.Protocol/      net10.0         DTOs + JsonSerializerContext (shared App↔Engine)
  Somning.Platform.Windows/     net10.0-windows NativeMethods.txt, Hook/, Audio/, Clipboard/, Paste/, Foreground/, Integrity/
  Somning.Engine/               net10.0-windows Exe (console), Whisper.net packages, Program (JSONL loop), Transcriber, VadGate, Warmup
  Somning.App/                  net10.0-windows WinExe, composition root, EngineSupervisor, RecoveryService, Logging, TestHarness/ (#if SOMNING_TEST_HARNESS)
tests/
  Somning.Core.Tests/           pure tables: TriggerInterpreter, reducer, PostFilter, recovery rules, WER
  Somning.Integration.Tests/    engine contract (fixtures), worker crash/restart, clipboard, E2E Notepad (Trait Category=E2E)
  fixtures/                     *.wav (16 kHz mono) + *.txt expected, manifest.json
tools/
  Somning.Bench/                benchmark CLI → BENCHMARK.md
  make-fixtures/                make_fixtures.py (edge-tts) + requirements.txt (pinned) + SAPI fallback .ps1
  fetch-models.ps1              pinned URLs + SHA-256
```

Why `net10.0-windows` for the Engine: the Whisper.net runtime targets copy Linux natives when the TFM has no `-` (`<ItemGroup Condition="$(TargetFramework.Contains('-')) == false ">`, Whisper.net.Runtime.targets:368) and macOS natives for `-macos` or plain TFMs (:293). [VERIFIED: runtimes/Whisper.net.Runtime/Whisper.net.Runtime.targets:143,293,368] The probe published 211 MB including linux/mac natives with `net10.0`.

### Pattern 1: Engine backend selection (before any native init)

**What:** One process = one backend. The order list has one element, so Vulkan never silently falls through to CPU inside the same process.
**Source (verbatim, RuntimeOptions.cs:9-36 @1.9.1):** "Setting values in this class will affect the behavior of the Whisper runtime only if they are done before any `WhisperFactory` is created." The default is `[RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Vulkan, RuntimeLibrary.CoreML, RuntimeLibrary.OpenVino, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]`, and `LoadedLibrary`: "Once a library is loaded, it will be used for all subsequent processing." The enum (RuntimeLibrary.cs) is `Cpu, Cuda, Cuda12, Vulkan, CoreML, OpenVino, CpuNoAvx`. [VERIFIED: Whisper.net/LibraryLoader/RuntimeOptions.cs:13-36, RuntimeLibrary.cs:5-13 @tag 1.9.1] The library is cached in a static `Lazy<LoadResult>` [VERIFIED: Whisper.net/Internals/WhisperLibrary.cs:11-20].

```csharp
// Somning.Engine Program.cs — FIRST statements, before touching any Whisper.net type that loads natives
using Whisper.net; using Whisper.net.LibraryLoader; using Whisper.net.Logger;
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);       // pt-BR text was mojibake without this (probe)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; // pt-BR printed "0,000"
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

RuntimeOptions.RuntimeLibraryOrder = backend == "vulkan" ? [RuntimeLibrary.Vulkan] : [RuntimeLibrary.Cpu];
using var nativeLog = LogProvider.AddLogger((lvl, msg) => { Console.Error.Write(msg); gpuDetector.Observe(msg); });

var sw = Stopwatch.StartNew();
using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions {
    UseGpu = backend == "vulkan",
    UseFlashAttention = useFlashAttention,   // default true; ~6.6x faster on RX 7600 (probe)
});
long loadMs = sw.ElapsedMilliseconds;
RuntimeLibrary? loaded = RuntimeOptions.LoadedLibrary;  // Vulkan | Cpu
// GPU really in use iff native log contained "whisper_backend_init_gpu: using Vulkan0 backend"
```

`WhisperFactoryOptions` defaults [VERIFIED: Whisper.net/WhisperFactoryOptions.cs:12-19]: `UseGpu = true; UseFlashAttention = false; UseDtwTimeStamps = false; ... GpuDevice = 0; ... DelayInitialization = false;` (so `FromPath` loads the model eagerly).

If the Vulkan DLLs cannot load, `CreateBuilder`/`FromPath` throws `Exception("Failed to load native whisper library. Error: ...")` [VERIFIED: WhisperLibrary.cs:31-37]. The worker should map that to `error{code:"backend_init_failed"}` and exit non-zero; the App then restarts it with `--backend cpu`.

### Pattern 2: Processor build + inference

Builder methods present in 1.9.1 [VERIFIED: Whisper.net/WhisperProcessorBuilder.cs:32-588]: `WithThreads(int)`, `WithNoContext()`, `WithSingleSegment()`, `WithAudioContextSize(int)`, `WithSuppressRegex(string)`, `WithPrompt(string)` ([EXPERIMENTAL]), `WithCarryInitialPrompt(bool)`, `WithLanguage(string)`, `WithTemperature(float)`, `WithTemperatureInc(float)`, `WithEntropyThreshold(float)`, `WithLogProbThreshold(float)`, `WithNoSpeechThreshold(float)`, `WithGreedySamplingStrategy(...)`, `WithBeamSearchSamplingStrategy(...)`, `WithProbabilities()`, `Build()`. There is **no** VAD option on this builder; VAD is a separate factory.

Effective native defaults when a builder value is not set come from `whisper_full_default_params` (whisper.cpp 1.8.5) [VERIFIED: src/whisper.cpp:5919-5965 @f24588a]: `n_threads = std::min(4, hardware_concurrency)`, `no_context = true`, `carry_initial_prompt = false`, `language = "en"`, `suppress_blank = true`, `suppress_nst = false`, `temperature = 0.0f`, `temperature_inc = 0.2f`, `entropy_thold = 2.4f`, `logprob_thold = -1.0f`, `no_speech_thold = 0.6f`. The XML doc saying the temperature default is "0.2f" is misleading: Whisper.net only overrides when `HasValue` [VERIFIED: WhisperProcessor.cs:591-623]. So D-06's "temperature fallback enabled" is already the default (`temperature_inc 0.2`). Call `WithNoContext()` explicitly anyway, for intent.

```csharp
// Build once per (language, prompt); WhisperProcessor serializes calls with an internal semaphore.
await using var processor = factory.CreateBuilder()
    .WithLanguage("pt")
    .WithNoContext()
    .WithThreads(6)                               // D-06 (physical cores: Ryzen 5 5600X = 6, verified)
    .WithPrompt(DefaultPromptPtBr)                // short punctuated pt-BR sentence
    .Build();

using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelForId);
var sb = new StringBuilder(); float maxNoSpeech = 0;
using var wav = File.OpenRead(spoolPath);         // WaveParser: 16 kHz only, PCM only
await foreach (var seg in processor.ProcessAsync(wav, cts.Token)) {
    sb.Append(seg.Text);
    maxNoSpeech = Math.Max(maxNoSpeech, seg.NoSpeechProbability); // log only — observed always 0.000
}
```

`ProcessAsync(Stream|ReadOnlyMemory<float>|float[], CancellationToken)` exists; cancellation goes through the native abort callback and progress/segment callbacks [VERIFIED: WhisperProcessor.cs:247-339, 659-728]. `SegmentData` exposes `Text, Start, End, MinProbability, MaxProbability, Probability, NoSpeechProbability, Language, Tokens` [VERIFIED: WhisperProcessorEvents.cs:48-121]. Min/Max/Probability are 0 unless `WithProbabilities()`. `WaveParser` throws `NotSupportedWaveException("Only 16KHz sample rate is supported.")` for anything other than 16000 Hz, and only PCM is accepted [VERIFIED: Whisper.net/Wave/WaveParser.cs:378-392].

### Pattern 3: Silero VAD gate (separate stage)

```csharp
// Source: examples/Vad/Program.cs @1.9.1 (adapted). Load once at worker start.
using var vadFactory = WhisperVadFactory.FromPath(sileroPath, new WhisperFactoryOptions { UseGpu = false });
using var vad = vadFactory.CreateBuilder()
    .WithThreshold(0.5f)
    .WithMinSpeechDuration(TimeSpan.FromMilliseconds(250))
    .WithSpeechPadding(TimeSpan.FromMilliseconds(200))
    .Build();
IReadOnlyList<VadSegmentData> segs = await vad.DetectSpeechAsync(File.OpenRead(spoolPath), ct); // Start/End TimeSpan
if (segs.Count == 0) return Empty("vad_no_speech");
// D-12: still transcribe the FULL untrimmed audio; VAD is only a gate.
```

API [VERIFIED: WhisperVadFactory.cs:27-48, WhisperVadProcessorBuilder.cs:30-111, WhisperVadProcessor.cs:29-131, VadSegmentData.cs:10-20]: `WithThreads, WithUseGpu, WithGpuDevice, WithThreshold, WithMinSpeechDuration, WithMinSilenceDuration, WithMaxSpeechDuration, WithSpeechPadding, WithSamplesOverlap`; `DetectSpeech(Async)(Stream|float[]|ReadOnlyMemory<float>)`; `ResetState()`. The `SileroVadType` enum is `V5_1_2, V6_2_0` [VERIFIED: Ggml/SileroVadType.cs:5-9]. Native defaults [VERIFIED: whisper.cpp src/whisper.cpp:4438-4447]: `threshold 0.5f, min_speech_duration_ms 250, min_silence_duration_ms 100, max_speech_duration_s FLT_MAX, speech_pad_ms 30, samples_overlap 0.1`.

Probe results with Silero v6.2.0 on CPU (load 29–45 ms):

| Fixture | Segments | Time |
|---|---|---|
| 9.7 s pt-BR | 2 | 71 ms |
| −20 dB copy | 2 | |
| −30 dB copy | 1 | |
| speech + pink noise | 2 | |
| 3 s digital silence | **0** | |
| 3 s pink noise (amp 0.05) | **0** | |
| 2 s 440 Hz tone | **0** | |
| 0.2 s cut | **0** | |
| jfk.wav | 4 | |

[VERIFIED: probe run this session]

### Pattern 4: Worker protocol host (App side)

```csharp
var psi = new ProcessStartInfo(enginePath, $"--backend {backend} --model \"{model}\" --vad \"{vad}\"") {
    UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false),
    StandardErrorEncoding = new UTF8Encoding(false),
};
// Assign the child to a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so an App crash never orphans a 1.6 GB worker.
// One reader task: stdout lines → JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.EngineMessage)
// One reader task: stderr lines → Serilog (worker native logs).
// Readiness timeout ~60 s (f16 load 1.7–1.9 s + warm-up), per-request timeout ~ max(30 s, 3× audio).
```

The probe confirmed that the worker's **stdout contained only program output**; all whisper.cpp/ggml logs went through `LogProvider.AddLogger` to stderr [VERIFIED: probe redirected 2> and stdout was clean]. Keep it that way: never call `LogProvider.AddConsoleLogging` in the worker.

### Pattern 5: LL hook thread (CsWin32)

```csharp
// Platform.Windows/Hook/KeyboardHook.cs (sketch)
sealed class KeyboardHook : IDisposable {
    readonly HOOKPROC _proc;                 // field: keeps delegate alive (GC!)
    Thread _thread; uint _threadId; UnhookWindowsHookExSafeHandle? _hook;
    public KeyboardHook(ChannelWriter<KeyEvent> sink) { _proc = HookProc; /*...*/ }
    public void Start() {
        _thread = new Thread(() => {
            _threadId = PInvoke.GetCurrentThreadId();
            _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, null, 0);
            // (raw overload alternative: hmod = GetModuleHandle(default(PCWSTR)))
            while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0) > 0) { PInvoke.TranslateMessage(msg); PInvoke.DispatchMessage(msg); }
            _hook.Dispose();
        }) { IsBackground = true, Name = "Somning.Hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    unsafe LRESULT HookProc(int code, WPARAM w, LPARAM l) {
        if (code < 0) return PInvoke.CallNextHookEx(null, code, w, l);
        var k = (KBDLLHOOKSTRUCT*)l.Value;
        bool injected = (k->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0;
        if (injected && k->dwExtraInfo == Markers.Paste) return PInvoke.CallNextHookEx(null, code, w, l);
        // minimal synchronous chord logic: no allocation, no locks, no I/O; < 1 ms
        // track Ctrl/Shift from hook events (GetAsyncKeyState is NOT updated for the current key inside the callback)
        // if VK_SPACE && ctrl && shift (or Space already "owned"): enqueue (TryWrite to bounded channel) and return (LRESULT)1
        return PInvoke.CallNextHookEx(null, code, w, l);
    }
    public void Dispose() => PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
}
```

Doc facts [CITED: learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc]:
- "the thread that installed the hook must have a message loop".
- "If the hook procedure times out, the system passes the message to the next hook. However, on Windows 7 and later, the hook is silently removed without being called."
- "Windows 10 version 1709 and later: The maximum timeout value the system allows is 1000 milliseconds."
- Returning nonzero prevents passing the message "to the rest of the hook chain or the target window procedure".
- The callback runs "before the asynchronous state of the key is updated", so GetAsyncKeyState cannot read the current key inside the callback.

`LowLevelHooksTimeout` on this machine = 1000 [VERIFIED: registry].

Generated signatures (CsWin32 0.3.367, verified this session):
- `SetWindowsHookEx(WINDOWS_HOOK_ID, HOOKPROC, SafeHandle? hmod, uint) → UnhookWindowsHookExSafeHandle`
- `CallNextHookEx(SafeHandle? hhk, int, WPARAM, LPARAM)`
- `delegate LRESULT HOOKPROC(int code, WPARAM, LPARAM)`
- `SendInput(ReadOnlySpan<INPUT>, int cbSize)`
- `KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED = 0x10`

### Pattern 6: Target-aware paste

```csharp
// At stop:
var fg = PInvoke.GetForegroundWindow();
uint pid; uint tid = PInvoke.GetWindowThreadProcessId(fg, &pid);
var gti = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
PInvoke.GetGUIThreadInfo(tid, ref gti);                 // gti.hwndFocus = focused control
var target = new PasteTarget(fg, gti.hwndFocus, pid, IntegrityReader.Rid(pid));

// Before paste (pure PasteGate decides; Platform executes):
// 1) clipboard set + GetClipboardSequenceNumber changed  2) poll GetAsyncKeyState(VK_CONTROL/SHIFT/MENU/LWIN/RWIN)&0x8000 every 15 ms ≤ 1.5 s
// 3) GetForegroundWindow()==target.Fg  4) target.Rid <= ownRid (Rid unknown/AccessDenied ⇒ defer)
Span<INPUT> ins = stackalloc INPUT[4];
Key(ref ins[0], VIRTUAL_KEY.VK_CONTROL, up:false); Key(ref ins[1], VIRTUAL_KEY.VK_V, false);
Key(ref ins[2], VIRTUAL_KEY.VK_V, true);           Key(ref ins[3], VIRTUAL_KEY.VK_CONTROL, true);
// each: type=INPUT_KEYBOARD, ki.dwExtraInfo = Markers.Paste, ki.dwFlags |= KEYEVENTF_KEYUP for ups
uint n = PInvoke.SendInput(ins, sizeof(INPUT)); // n != 4 ⇒ paste_deferred
```

UIPI [CITED: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput]: "This function fails when it is blocked by UIPI. Note that neither GetLastError nor the return value will indicate the failure was caused by UIPI blocking." Also: "Applications are permitted to inject input only into applications that are at an equal or lesser integrity level." The inserted-count check therefore cannot detect UIPI, which is why the integrity comparison is mandatory.

Integrity read: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid)` → `OpenProcessToken(h, TOKEN_QUERY, out tok)` → `GetTokenInformation(tok, TokenIntegrityLevel, buf, ...)` → `TOKEN_MANDATORY_LABEL.Label.Sid` → `*GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1)` = RID. Compare with the RID of `GetCurrentProcess()`.

### Pattern 7: Clipboard (own Win32, owner window required)

[CITED: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openclipboard]: "If an application calls OpenClipboard with hwnd set to NULL, EmptyClipboard sets the clipboard owner to NULL; this causes SetClipboardData to fail."

So the App needs a hidden owner HWND on an STA thread with a message loop, for example a message-only window (`HWND_MESSAGE`) on the App's UI thread:

```
seq0 = GetClipboardSequenceNumber()
for attempt in 1..10: if OpenClipboard(ownerHwnd) break; sleep(10 * 2^min(attempt,4) ms)   // fail ⇒ error
EmptyClipboard(); h = GlobalAlloc(GMEM_MOVEABLE, (len+1)*2); p = GlobalLock(h); copy UTF-16 + '\0'; GlobalUnlock(h)
if SetClipboardData(CF_UNICODETEXT=13, (HANDLE)h) == 0 → GlobalFree(h), fail      // on success the SYSTEM owns h: never free
CloseClipboard();  verify GetClipboardSequenceNumber() != seq0
```

Use the **raw `HANDLE` overload** of `SetClipboardData`. CsWin32 also generates `SetClipboardData(uint, SafeHandle) → SafeFileHandle` [VERIFIED: generated code], and disposing a SafeHandle would free or close system-owned memory.

### Pattern 8: Durable spool

- Writing: `WaveFileWriter(FileStream, new WaveFormat(16000,16,1))`; call `writer.Flush()` about every 1 s. NAudio 3.3's `Flush()` "updates header, so that WAV file will be valid up to the point currently written" (non-destructive header snapshot) [VERIFIED: NAudio.Core/Wave/WaveOutputs/WaveFileWriter.cs:553-575 @v3.3.0]. A killed process therefore leaves a parseable WAV that is at most about 1 s short.
- Sidecar JSON: write to `{ts}.json.tmp`, then `File.Move(tmp, final, overwrite: true)`.

### Pattern 9: Capture pipeline (NAudio 3.3)

```csharp
// Source: NAudio Docs/WasapiRecorder.md @v3.3.0 (adapted)
var recorder = new WasapiRecorderBuilder()            // default device, shared, event sync, 100 ms buffer, MIX format
    .WithBufferLength(30)
    .Build();
var mix = recorder.WaveFormat;                        // typically 48 kHz IEEE float, 2 ch
var buffered = new BufferedWaveProvider(mix) { ReadFully = false, DiscardOnBufferOverflow = false, BufferDuration = TimeSpan.FromSeconds(5) };
ISampleProvider mono = Downmix(buffered.ToSampleProvider(), mix.Channels);  // own N→1 average (ToMono() is stereo-only)
var to16k = new WdlResamplingSampleProvider(mono, 16000);
var pcm16 = to16k.ToWaveProvider16();
recorder.DataAvailable += (span, flags, _, _) => {    // span valid only inside callback
    buffered.AddSamples(span);  /* then drain pcm16 into writer + RMS */ };
recorder.StartRecording();
```

Verified APIs @v3.3.0:
- `CaptureDataAvailableHandler(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)` (WasapiRecorder.cs:743)
- builder `WithDevice/WithSharedMode/WithExclusiveMode/WithEventSync/WithPollingSync/WithBufferLength/WithFormat/WithMmcssThreadPriority/WithLowLatency/WithDefaultDeviceStreamRouting(BuildAsync)/Build` (WasapiRecorderBuilder.cs:34-288)
- `WasapiCapture` is `[Obsolete("Use WasapiRecorderBuilder ...")]` (WasapiCapture.cs:14)
- `WaveInEvent` is `[Obsolete("WaveInEvent has been renamed to WaveIn...")]` (WaveIn.cs:350-357)
- classes `WdlResamplingSampleProvider`, `StereoToMonoSampleProvider`, `SampleToWaveProvider16`, `BufferedWaveProvider`, extension `ToSampleProvider/ToWaveProvider16/ToMono` exist in NAudio.Core. **`BufferedWaveProvider.ReadFully` defaults to `true` (zero-fills)** (BufferedWaveProvider.cs:27); set it to false for streaming conversion.

`AudioClientBufferFlags.Silent` on a packet means the device delivered silence; treat that buffer as zeros.

### Anti-Patterns to Avoid

- **Default `RuntimeLibraryOrder`, or listing both Vulkan and Cpu in one process:** the CPU never takes over after a GPU crash in-process (the Lazy is static). Use one backend per worker process.
- **Relying on `no_speech` / `NoSpeechThreshold` to block hallucinations:** observed 0.000 on silence. Use VAD plus a post-filter.
- **`Console.WriteLine` of the protocol under default encodings or pt-BR culture:** mojibake (`Ol�`) and decimal commas were both observed.
- **Logging transcript text:** D-23 forbids it. Log `chars` only.
- **Synthesizing key-ups for user-held modifiers:** D-13 forbids it. Poll and wait instead.
- **`GetAsyncKeyState` for the triggering key inside the hook callback:** it reflects the state before the event.
- **Heavy work, allocation or locks in the hook callback:** risks the 1000 ms silent unhook. Use a bounded `Channel` with `TryWrite` only.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Speech detection | RMS/energy gate as speech proof | Whisper.net `WhisperVadFactory` + Silero v6.2.0 | RMS accepts noise and rejects quiet speech; Silero handled −30 dB speech and rejected noise/tone in the probe |
| Resampling | Linear interpolation | `WdlResamplingSampleProvider` (or WASAPI AUTOCONVERTPCM) | Aliasing hurts ASR |
| WAV container + crash-safe header | Manual RIFF writer | NAudio `WaveFileWriter.Flush()` | Non-destructive header snapshot is built in |
| P/Invoke signatures | Hand-written `[DllImport]` | CsWin32 `NativeMethods.txt` | Struct sizes (INPUT, GUITHREADINFO) are the classic bug |
| JSON | Manual string building | System.Text.Json source-gen | Escaping of quotes, newlines and accents in transcripts |
| Model download hash | Custom hashing | `SHA256.HashDataAsync(stream)` + the pinned table below | Already in the BCL |
| Native lib probing | Custom `LoadLibrary` | Whisper.net loader + `RuntimeOptions` | It already probes `runtimes/vulkan/win-x64` relative to the app base dir |

**Key insight:** the risky parts (native inference, VAD, WAV durability, resampling) all have tested library paths. Hand-written code should be limited to the small pure decision logic (TriggerInterpreter, reducer, PasteGate, PostFilter, recovery), which is exactly what can be table-tested.

## Model Acquisition (D-15): pinned URLs + SHA-256

The LFS `oid` equals the SHA-256 of the file. This was confirmed by downloading and hashing tiny, q5_0, f16 and silero v6.2.0 this session; all four matched. [VERIFIED: HF API tree + sha256sum]

| File | Size (bytes) | SHA-256 | URL (pinned commit) |
|---|---|---|---|
| ggml-large-v3-turbo.bin (f16) | 1624555275 | `1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69` | `https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo.bin` |
| ggml-large-v3-turbo-q8_0.bin | 874188075 | `317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1` | same repo/commit, `/ggml-large-v3-turbo-q8_0.bin` (pinned URL returned 302 + X-Linked-Size 874188075) |
| ggml-large-v3-turbo-q5_0.bin | 574041195 | `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2` | same repo/commit, `/ggml-large-v3-turbo-q5_0.bin` |
| ggml-silero-v6.2.0.bin | 885098 | `2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987` | **`https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin`** |

**Correction to D-15 wording:** Silero is **not** in `ggerganov/whisper.cpp`; it is in `ggml-org/whisper-vad` (repo files: `ggml-silero-v5.1.2.bin`, `ggml-silero-v6.2.0.bin`) [VERIFIED: HF API]. Whisper.net's own downloader points at a third repo, `https://huggingface.co/sandrohanea/whisper.net/resolve` [VERIFIED: Ggml/WhisperGgmlDownloader.cs:7]. Do not use `WhisperGgmlDownloader`; it ignores the D-15 source and the hash table.

URLs 302-redirect to a CDN, so `HttpClient` must follow redirects (the default). Shortcut for the executor: this session left verified copies of f16, q5_0 and silero (plus ggml-tiny.bin, SHA `be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21`) in `C:\Users\ADMINI~1\AppData\Local\Temp\claude\C--Users-Administrator-source-repos-somning\cd515f9f-9a57-4744-bd0f-c017a7d1d2a1\scratchpad\dl\`. That folder may be gone, so the fetch script must still be able to download, but it can copy from there first and verify the hash.

## Probe Benchmark (this session, published self-contained build, minimal PATH)

The fixtures were edge-tts pt-BR (`fr.wav` 9.7 s, Francisca; `long15.wav` 15.58 s, Antonio), converted with ffmpeg to 16 kHz mono s16. "Warm" means runs 2–3 on the same processor.

| Backend | Model | FlashAttn | Load ms | 15.6 s clip infer ms (warm) | 9.7 s clip warm | Notes |
|---|---|---|---|---|---|---|
| Vulkan | f16 | off | 1903 | 3565–3603 | ~3490 | **fails** 3.0 s budget |
| Vulkan | f16 | **on** | 1676 | 541–562 (run0 1199) | 435–448 | text correct |
| Vulkan | q5_0 | off | 796 | — | 3307–3814 (run0 6069) | |
| Vulkan | q5_0 | **on** | 930 | 499–531 | 417–427 | same text as f16 on these fixtures |
| CPU (6 thr) | q5_0 | off | 652 | — | 20616–24681 | degraded |
| CPU (6 thr) | q5_0 | on | 522 | 14805–15016 | — | degraded |
| CPU (6 thr) | q5_0 | on + `WithAudioContextSize(768)` | 651 | 7175–7270 | — | still degraded; output changed ("45 mil reais," vs "R$ 45 mil") |
| Vulkan | tiny | off | 281 | — | (jfk 11 s) run0 3993 → 252 | first-call warm-up cost |

[VERIFIED: probe output this session]

The inference time is roughly flat with clip length. whisper.cpp pads to a 30 s window, so silence costs about as much as speech; this is another reason the VAD gate also saves latency. The planner should treat these as **indicative**. D-17 still requires ≥10 runs and p50/p95 stop-to-paste on the real fixture set.

## Common Pitfalls

### Pitfall 1: Hallucinations that `no_speech` doesn't catch
**What goes wrong:** Silence or noise produces "Legenda por Sônia Ruberti", "E aí", "Aplausos." or "." [VERIFIED: probe], and these get pasted.
**Why it happens:** Whisper is trained on subtitles. In this binding/model, `NoSpeechProbability` came out 0.000 every time [VERIFIED: probe, with and without prompt].
**How to avoid:** Gate order per D-12, but make **VAD the primary gate**. Post-filter:
- Normalize the text (lowercase, strip punctuation and whitespace).
- Discard if it has no letter or digit.
- Discard on whole-output blocklist matches, plus prefix patterns: `legenda(s)? (por|pela)`, `amara.org`, `obrigad[oa]( por assistir)?`, `inscreva-se`, `aplausos`, `música`, `[música]`, `e aí`.
- Keep `no_speech` in the log.
**Warning signs:** The silence, noise or tone fixtures produce outcome ≠ `empty`.

### Pitfall 2: Flash attention not enabled
**What goes wrong:** The benchmark shows Vulkan at about 3.6 s and the default falls to "degraded".
**How to avoid:** Add `FlashAttention ∈ {on, off}` to the D-17 matrix and default to on. Re-verify transcript equality across FA on/off in the benchmark WER column.

### Pitfall 3: First Vulkan inference is slow
**What goes wrong:** The first dictation after start takes several seconds (shader compile).
**How to avoid:** D-05 warm-up must run a **full `ProcessAsync`** on 1 s of silence (its text is discarded; it will hallucinate) before printing `ready`. `warm_ms` goes in `ready`.

### Pitfall 4: UTF-8 / culture across the pipe and in logs
**What goes wrong:** pt-BR accents come out mojibaked (`Ol�`); `F3` formatting printed `0,000` under the pt-BR culture [VERIFIED: probe].
**How to avoid:**
- Worker: UTF-8 (no BOM) Console encodings.
- App: `StandardOutputEncoding/StandardInputEncoding = UTF8`.
- Use `CultureInfo.InvariantCulture` for all numeric formatting in logs, JSON and BENCHMARK.md.
- Add a protocol test that round-trips "Olá, ação, João, até às 3h".

### Pitfall 5: Foreground lock breaks the E2E
**What goes wrong:** `Process.Start("notepad")` from the test runner did not become foreground, and `SetForegroundWindow` returned false. Neither the Alt-key trick, `AttachThreadInput` nor `SwitchToThisWindow` helped while an orphan `OpenWith` dialog held the foreground. After an injected mouse click, `SetForegroundWindow` succeeded; on a later run Notepad became foreground directly [VERIFIED: probe].
**How to avoid:**
- Write a test helper `EnsureForeground(hwnd)` that tries, in order: `SetForegroundWindow`; `AttachThreadInput` + `BringWindowToTop` + `SetForegroundWindow`; `SetWindowPos(HWND_TOPMOST)` + injected click inside the Edit client rect + `SetForegroundWindow`; then restore `HWND_NOTOPMOST`.
- Then **assert `GetForegroundWindow()==notepad`**, otherwise `Assert.Skip("could not acquire foreground")`.
- Click inside Notepad's Edit control only.

### Pitfall 6: The whole session runs elevated
**What goes wrong:** The machine runs as the built-in `Administrator`. With `EnableLUA=1` and `ConsentPromptBehaviorAdmin=0`, `IsInRole(Administrator)` is True [VERIFIED: probe], so Somning, Notepad and the tests all run at High IL. The "elevated target" path (OUT-03) never occurs naturally, and Vulkan loader env vars may be ignored.
**How to avoid:**
- Unit-test PasteGate with a fake `IIntegrityReader`.
- Integration-test the real reader returns the same RID for the current process and for a child Notepad.
- For a real UIPI E2E, start the App de-elevated with `runas /trustlevel:0x20000 "<path>\Somning.App.exe --test-harness ..."` against the High-IL Notepad [ASSUMED: that trustlevel 0x20000 yields Medium IL here]. Skip if the IL is not lower.

### Pitfall 7: Ctrl+Shift input-language/layout toggle
**What goes wrong:** The user has `en-US` (US-International `0409:00020409`) and `pt-BR` (ABNT `0416:00000416`) [VERIFIED: Get-WinUserLanguageList]. `HKCU\Keyboard Layout\Toggle` is absent, so Windows defaults apply. The hook suppresses only Space, so the system can see Ctrl+Shift pressed and released "alone" and switch layout [ASSUMED: Windows default Ctrl+Shift layout hotkey; whether a suppressed Space prevents it].
**How to avoid:** The E2E records `GetKeyboardLayout(notepadThread)` before and after the hotkey and asserts it is unchanged. If it fails, inject a "mask" key (unassigned VK `0xE8`, tagged with the Somning marker) on chord recognition, as AutoHotkey's `#MenuMaskKey` does [ASSUMED]. The full conflict warning is HOTK-05 (Phase 2).

### Pitfall 8: VC++ runtime dependency hidden by the dev machine
**What goes wrong:** All win-x64 natives import `MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, and the ggml base/cpu DLLs import `VCOMP140.DLL` (OpenMP). `ggml-vulkan-whisper.dll` imports `vulkan-1.dll` [VERIFIED: PE import table parse]. These are not in the publish folder; this machine has VC++ x64 14.51.36247.
**How to avoid:** The Phase 1 gate notes this in BENCHMARK.md as a deployment prerequisite (for Phase 4). It does not block Phase 1.

### Pitfall 9: Vulkan implicit layers (OBS, Steam, EOS, RTSS)
**What goes wrong:** The Vulkan loader inserted `VK_LAYER_OBS_HOOK` into the worker, even with `VK_LOADER_LAYERS_DISABLE=~implicit~` set [VERIFIED: VK_LOADER_DEBUG=layer log]. Possible cause: the loader ignores layer env vars in elevated processes [ASSUMED]. Inference still worked.
**How to avoid:** Set `VK_LOADER_LAYERS_DISABLE=~implicit~` in the worker's `ProcessStartInfo.Environment` (harmless). Do not count on it. GPU crashes must be absorbed by the D-04 restart-in-CPU path. Log the layer list (`VK_LOADER_DEBUG=layer` only in a diagnostic mode).

### Pitfall 10: WER false failures from number/currency normalization
**What goes wrong:** Turbo wrote "três horas" as "3 horas" and "quarenta e cinco mil reais" as "R$ 45 mil" [VERIFIED: probe].
**How to avoid:** Write fixture source text the way Whisper renders it (digits for numbers, no spelled currency), store the expected text separately, and normalize (lowercase invariant, strip punctuation, collapse whitespace; keep diacritics) before WER.

### Pitfall 11: Spool WAV after a crash
**What goes wrong:** A WAV with a zero data-length header (process killed before Dispose) is rejected by `WaveParser`.
**How to avoid:** Call `Flush()` about every second (header snapshot). Recovery should also tolerate a missing or short tail by recomputing the data size from the file length if the header disagrees.

### Pitfall 12: Generated SafeHandle overloads that free things they shouldn't
**What goes wrong:**
- `SetClipboardData(uint, SafeHandle) → SafeFileHandle`: the clipboard owns the memory after success.
- `GetModuleHandle(string) → FreeLibrarySafeHandle`: disposing it calls FreeLibrary on a module you never loaded.
**How to avoid:** Use the raw-handle overloads for these two [VERIFIED: generated signatures].

## Code Examples

### NativeMethods.txt (verified to generate with CsWin32 0.3.367, 0 warnings)

```
SetWindowsHookEx
UnhookWindowsHookEx
CallNextHookEx
GetModuleHandle
GetMessage
PeekMessage
TranslateMessage
DispatchMessage
PostThreadMessage
GetCurrentThreadId
KBDLLHOOKSTRUCT
KBDLLHOOKSTRUCT_FLAGS
WM_KEYDOWN
WM_KEYUP
WM_SYSKEYDOWN
WM_SYSKEYUP
WM_QUIT
WM_GETTEXT
WM_GETTEXTLENGTH
SendInput
INPUT
GetAsyncKeyState
GetKeyState
OpenClipboard
CloseClipboard
EmptyClipboard
SetClipboardData
GetClipboardData
IsClipboardFormatAvailable
GetClipboardSequenceNumber
GetClipboardOwner
GlobalAlloc
GlobalLock
GlobalUnlock
GlobalFree
GetForegroundWindow
GetGUIThreadInfo
GetWindowThreadProcessId
OpenProcess
OpenProcessToken
GetTokenInformation
GetSidSubAuthority
GetSidSubAuthorityCount
TOKEN_MANDATORY_LABEL
SECURITY_MANDATORY_HIGH_RID
CloseHandle
GetCurrentProcess
SendMessage
FindWindowEx
GetKeyboardLayout
GetLastInputInfo
```

Add these as needed (not compiled in the probe, so treat them as [ASSUMED] names): `CreateWindowEx`, `RegisterClassEx`, `DefWindowProc`, `HWND_MESSAGE`, `CreateJobObject`, `SetInformationJobObject`, `AssignProcessToJobObject`, `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`, `SetForegroundWindow`, `AttachThreadInput`, `BringWindowToTop`, `SetWindowPos`. Put the test-only ones in the test project's own NativeMethods.txt. Note: `LLKHF_INJECTED` alone triggers `PInvoke004: Use the name of the enum that declares this constant: KBDLLHOOKSTRUCT_FLAGS` [VERIFIED].

### WER (word-level Levenshtein)

```csharp
public static double Wer(string expected, string actual) {
    string[] r = Norm(expected), h = Norm(actual);
    var d = new int[r.Length + 1, h.Length + 1];
    for (int i = 0; i <= r.Length; i++) d[i, 0] = i;
    for (int j = 0; j <= h.Length; j++) d[0, j] = j;
    for (int i = 1; i <= r.Length; i++)
        for (int j = 1; j <= h.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1));
    return r.Length == 0 ? (h.Length == 0 ? 0 : 1) : (double)d[r.Length, h.Length] / r.Length;
}
static string[] Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ")
    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
```

### Fixture generation (dev-time)

```powershell
python -m venv tools\make-fixtures\.venv
tools\make-fixtures\.venv\Scripts\python -m pip install edge-tts==7.3.1
tools\make-fixtures\.venv\Scripts\edge-tts --voice pt-BR-FranciscaNeural --text "<texto>" --write-media out.mp3
ffmpeg -loglevel error -y -i out.mp3 -ar 16000 -ac 1 -c:a pcm_s16le out.wav
ffmpeg -i out.wav -af volume=-20dB -c:a pcm_s16le quiet.wav                      # quiet speech
ffmpeg -f lavfi -i anullsrc=r=16000:cl=mono -t 3 -c:a pcm_s16le silence.wav       # silence
ffmpeg -f lavfi -i "anoisesrc=r=16000:color=pink:amplitude=0.05" -t 3 -c:a pcm_s16le noise.wav
```

The available voices are `pt-BR-AntonioNeural`, `pt-BR-FranciscaNeural`, `pt-BR-ThalitaMultilingualNeural` [VERIFIED: `edge-tts --list-voices`]. edge-tts needs network access; it is a dev-time step and its outputs are committed (D-18).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| NAudio 2 `WaveInEvent`, `WasapiCapture` | NAudio 3 `WaveIn`, `WasapiRecorderBuilder`/`WasapiRecorder` (span-based DataAvailable) | NAudio 3.0 | Old names compile as `[Obsolete]` aliases; use the new ones |
| xUnit v2 + VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) | xunit.v3 4.x on Microsoft Testing Platform v2 | xunit.v3 4.0 (2026-08) | Needs the `global.json` `test.runner`; filters are `--filter-trait`, `--filter-not-trait`, `--filter-class`; `--explicit on/off/only` |
| Separate ONNX Silero | whisper.cpp built-in ggml Silero (`WhisperVadFactory`) | whisper.cpp ≥1.7.6 / Whisper.net 1.9 | No ONNX dependency |

**Deprecated/outdated:**
- Whisper.net `WhisperGgmlDownloader` for production: it uses the `sandrohanea/whisper.net` HF repo, not the D-15 source.
- STACK.md's "auto fallback CUDA > Vulkan > CPU": not valid in-process (CROSS-REVIEW H2, confirmed by source).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `runas /trustlevel:0x20000` launches the App at Medium IL on this machine (for the UIPI E2E) | Pitfall 6 | The UIPI E2E can't run; fall back to unit tests with a fake integrity reader (acceptable) |
| A2 | Windows default Ctrl+Shift layout hotkey can fire when Space is suppressed by the LL hook; a VK 0xE8 mask key prevents it | Pitfall 7 | Layout silently flips during dictation; the E2E layout assertion detects it |
| A3 | The Vulkan loader ignores `VK_LOADER_LAYERS_DISABLE` in elevated processes | Pitfall 9 | Only diagnostic; restart-in-CPU covers crashes |
| A4 | `WithAudioContextSize` reduction is unsafe for quality in general (it changed the output in the probe) | Benchmark | If it is acceptable, it could rescue CPU latency; keep it out of defaults |
| A5 | The CsWin32 names listed as "add as needed" generate without errors | Code Examples | A compile-time error is fixed trivially by the executor |
| A6 | Building one `WhisperProcessor` per (language, prompt) and reusing it is cheaper than per-request build (per-processor native state allocation) | Pattern 2 | Only latency; measure in the benchmark |
| A7 | Blocklist prefix patterns listed in Pitfall 1 are sufficient for common pt-BR subtitle hallucinations | Pitfall 1 | Extra hallucinations leak only if VAD also passes them; extend the list from logs |

## Open Questions (RESOLVED — adopted in plans: f16 vs q5_0 decided by D-17 benchmark; D-12 no_speech threshold retained with VAD primary; automated WASAPI smoke and optional subjective microphone check; edge-tts pinned 7.3.1 reused from spike venv, SAPI fallback)

1. **f16 vs q5_0 default.** Both produced identical text on two TTS fixtures, and q5_0 + FA was slightly faster (≈0.5 s). D-17's "most accurate" rule needs the full fixture set (quiet/noisy/numbers). Adopted in plan 01-08: let the benchmark decide, and expect f16+FA if within budget, since both are far under 3.0 s.
2. **Does `NoSpeechProbability` ever become non-zero?** It was always 0.000 here. Adopted in plan 01-05: keep the D-12 threshold code (configurable, e.g. 0.6), log the value, and do not rely on it. Revisit after real-mic data.
3. **Real-mic first-word clipping.** Mic-open latency is logged (D-10), but there was no real-mic test in this session. Adopted in plans 01-06/01-13: bounded automated WASAPI smoke plus optional subjective first-word checklist.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test/publish | ✓ | 10.0.203 (runtime 10.0.7; WindowsDesktop 10.0.7) | — |
| Vulkan loader + AMD driver | Vulkan backend | ✓ | loader 1.4.341; driver 26.8.1, API 1.4.349, RX 7600, KHR_coopmat | CPU worker |
| Vulkan SDK | — (not needed) | ✗ (`VULKAN_SDK` unset) | — | not required (probe ran without it) |
| VC++ 2015-2022 x64 redist | Whisper natives | ✓ | 14.51.36247 | — (Phase 4 must ship/bootstrap it) |
| AVX2/FMA CPU | CPU runtime | ✓ | Ryzen 5 5600X, 6C/12T | — |
| Python | fixture generation | ✓ | 3.13.3 | SAPI pt-BR voices |
| edge-tts | fixtures | not installed globally (pip index shows 7.3.1) | 7.3.1 | SAPI `Microsoft Maria Desktop` pt-BR |
| ffmpeg | fixture conversion/mixing | ✓ | winget install | NAudio MediaFoundation in a C# tool |
| Interactive desktop | E2E (hook/SendInput/Notepad) | ✓ | session 1 "console", Active, UserInteractive=True | tests skip with a message |
| Classic Notepad (Edit control) | E2E | ✓ | `C:\Windows\System32\notepad.exe`, child class `Edit` (WM_SETTEXT/WM_GETTEXT probe OK) | UIA |
| Network to huggingface.co | model fetch | ✓ | — | local copies in the scratchpad (see Model Acquisition) |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** edge-tts (reuse authorized spike binary or SAPI; no new package install scheduled).

## Validation Architecture

> `workflow.nyquist_validation` is `false` in config.json, but the orchestrator explicitly requested this section.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xunit.v3 4.0.2 on Microsoft Testing Platform v2 (.NET 10 SDK) |
| Config file | `global.json` with `"test": {"runner": "Microsoft.Testing.Platform"}` (Wave 0) |
| Quick run command | `dotnet test --project tests/Somning.Core.Tests` |
| Full suite command | `dotnet test --solution Somning.sln` (E2E tests carry `[Trait("Category","E2E")]` and self-skip without a desktop or foreground) |
| Bench/gate | `dotnet run -c Release --project tools/Somning.Bench -- --engine <publish>/engine/Somning.Engine.exe --fixtures tests/fixtures --runs 10 --out .planning/phases/01-dictation-loop-mvp/BENCHMARK.md` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command / Mechanism | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| STT-01/02/08 (SC1) | Published Engine (no SDK on PATH) transcribes pt-BR fixture on cpu and vulkan; benchmark writes p50/p95 + selected defaults | gate script + bench | `pwsh tools/gate.ps1`: `dotnet publish src/Somning.Engine -c Release -r win-x64 --self-contained -o publish/engine`; copy to `%TEMP%\somning-gate`; run with `PATH=C:\Windows\system32;C:\Windows`; assert `ready.backend` + WER ≤ 10% | ❌ Wave 0 |
| HOTK-02/03 | Tap <300 ms toggles; hold ≥300 ms is PTT; auto-repeat ignored; missed key-up stop; 5 min cap | unit (table, fake clock) | `dotnet test --project tests/Somning.Core.Tests --filter-class *TriggerInterpreterTests` | ❌ |
| HOTK-01 (SC2) | Space never leaks; chord suppressed | E2E | Notepad text has no " " prefix after the hotkey; WM_GETTEXT exact compare | ❌ |
| AUD-01 | Capture produces 16 kHz mono s16 | integration | `FileAudioSource` + format assertions; resample unit test (48 kHz stereo float sine → 16 kHz mono, frequency preserved) | ❌ |
| AUD-04 (SC4) | Killed App mid-recording → spool recovered on restart → clipboard has text, no paste | integration | Start App harness, start recording, `Process.Kill`, restart, assert sidecar `recovered` + clipboard | ❌ |
| STT-02 (SC4) | Worker killed or GPU fault → restart in cpu, retry once, App alive | integration | Kill `Somning.Engine` PID mid-transcribe; harness-only `SOMNING_ENGINE_FAULT=crash_on_transcribe` env for the vulkan worker; assert result from `backend=cpu` | ❌ |
| STT-03 (SC3) | Silence, noise, tone and 0.2 s fixtures → outcome `empty`, clipboard untouched (seq# unchanged) | integration (engine contract) + unit (PostFilter) | `--filter-class *SilenceGateTests`, `*PostFilterTests` | ❌ |
| STT-04 | Ready only after warm-up; hotkey before ready is queued | unit (reducer) + integration (`ready.warm_ms` > 0; request sent before ready completes) | `*EngineSupervisorTests` | ❌ |
| OUT-01/02 (SC2) | Clipboard == text after paste; Notepad text == transcript (WER ≤ 10%) | E2E (tap-tap and hold-release variants) | `dotnet test --project tests/Somning.Integration.Tests --filter-trait "Category=E2E"` | ❌ |
| OUT-03 (SC5) | Foreground changed → `paste_deferred`; elevated target → `paste_deferred` | unit (PasteGate with fakes) + E2E (switch foreground to a second Notepad before stop+paste) + optional UIPI E2E (A1) | `*PasteGateTests` | ❌ |
| APP-06 (SC5) | One structured log line per dictation with all fields; no transcript text; redaction of `*key*/*token*/Authorization` | unit (Serilog `InMemory`-style sink or a custom `ILogEventSink` collector) | `*DictationMetricsLogTests` | ❌ |

### Sampling Rate
- **Per task commit:** Core tests (<10 s).
- **Per wave merge:** full suite, including engine contract tests (needs models in `%LOCALAPPDATA%\Somning\models`; skip with a message if absent).
- **Phase gate:** full suite green + BENCHMARK.md committed + E2E run on the interactive desktop.

### Wave 0 Gaps
- [ ] `global.json`, `Directory.Packages.props`, `Directory.Build.props` (TreatWarningsAsErrors except obsolete-in-tests, Nullable enable, InvariantGlobalization **false**: pt text needs culture data)
- [ ] `tests/fixtures/` + `tools/make-fixtures` (edge-tts venv) + manifest with expected text
- [ ] `tools/fetch-models.ps1` (pinned URLs + SHA table above)
- [ ] Test helpers: `NotepadDriver` (launch, EnsureForeground, WM_GETTEXT, kill), `HotkeyInjector` (SendInput with TEST marker), `DesktopFact` skip guard

## Security Domain

`security_enforcement: true`, ASVS L1. This phase has no network listener and no authentication. Its attack surface is local IPC, files and input injection.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | partial | Named pipe `\\.\pipe\somning-test` only in Debug/`SOMNING_TEST_HARNESS` builds (D-20); create it with a `PipeSecurity` ACL limited to the current user SID; `PipeOptions.CurrentUserOnly` |
| V5 Input Validation | yes | Engine validates `wav_path` (must be under `%LOCALAPPDATA%\Somning\recordings`, canonicalized with `Path.GetFullPath`, `.wav` extension) and rejects other paths; JSON via source-gen with max line length; reject unknown message types |
| V6 Cryptography | yes (integrity only) | `SHA256.HashDataAsync` for model verification; never load a model whose hash mismatches |
| V7 Error/Logging | yes | D-23: no transcript text, Serilog destructuring redaction for `*key*`, `*token*`, `Authorization`; errors to the log without stack traces containing user text |
| V12 Files | yes | Spool/sidecar under the per-user LocalAppData; atomic writes; retention (last 20) |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Another local process spoofs the test pipe to inject hotkeys/audio | Spoofing/Elevation | Harness compiled out of Release; current-user-only pipe ACL |
| Engine fed an arbitrary path (e.g. reading other files) | Information disclosure | Path allow-list (recordings dir), WAV header validation |
| Tampered/poisoned model file | Tampering | Pinned commit URLs + SHA-256 table; verify before load |
| Transcripts leaking to logs | Information disclosure | Length-only logging; redaction policy; unit test asserts the log line has no transcript substring |
| Synthetic Ctrl+V into the wrong window | Tampering (integrity of user data) | Target-aware PasteGate (fg unchanged, IL check, modifiers released) |
| Keystroke capture concerns (LL hook sees all keys) | Information disclosure | Callback inspects only VK/flags; never logs or stores key codes other than chord events |

## Project Constraints (from CLAUDE.md)

- Platform: Windows 10 Pro 19045 x64 only; pt-BR only, with the language kept as a config value; local engine free/offline; "functional and simple"; every phase verifiable by agents without the owner.
- Stack directives: C#/.NET 10; own `WH_KEYBOARD_LL` via CsWin32 (not RegisterHotKey); no CUDA; no NativeAOT/aggressive trimming; directory self-contained publish (preserve the native runtime layout); `Clipboard` with retry and no synthetic modifier games (superseded by D-13: wait, don't synthesize).
- Global rules (~/.claude/rules): immutability preferred; files 200–400 lines (max 800); functions <50 lines; no deep nesting; explicit error handling; no hardcoded secrets; input validation at boundaries; TDD (RED→GREEN), 80% coverage target; conventional commits.
- GSD workflow: edits only via GSD commands (execute-phase).

## Sources

### Primary (HIGH confidence)
- github.com/sandrohanea/whisper.net @ tag **1.9.1** (commit 98278acc, cloned this session): `readme.md`, `LibraryLoader/RuntimeOptions.cs`, `RuntimeLibrary.cs`, `NativeLibraryLoader.cs`, `Internals/WhisperLibrary.cs`, `WhisperFactory.cs`, `WhisperFactoryOptions.cs`, `WhisperProcessorBuilder.cs`, `WhisperProcessor.cs`, `WhisperProcessorEvents.cs`, `WhisperVad*.cs`, `Ggml/*`, `Wave/WaveParser.cs`, `runtimes/*/*.targets`, `examples/Vad/Program.cs`
- github.com/ggml-org/whisper.cpp @ f24588a (Whisper.net submodule; v1.8.5): `src/whisper.cpp` default params, VAD defaults, no_speech computation
- github.com/naudio/NAudio @ **v3.3.0**: `Docs/WasapiRecorder.md`, `Docs/Resampling.md`, `WasapiRecorder(Builder).cs`, `WaveIn.cs`, `WasapiCapture.cs`, `WaveFileWriter.cs`, `BufferedWaveProvider.cs`
- NuGet registration/flat-container API (versions, dates, deps); HF API (`ggerganov/whisper.cpp`, `ggml-org/whisper-vad`) file sizes, LFS SHA-256, repo commits
- learn.microsoft.com: LowLevelKeyboardProc, SendInput, OpenClipboard
- xunit.net MTP docs (global.json runner)
- **Empirical probes this session**: self-contained publish + minimal-PATH runs (CPU/Vulkan, tiny/turbo f16/q5_0, FA on/off, audio_ctx), Silero VAD on 9 fixtures, PE import parse, CsWin32 generation build, xunit.v3 4.0.2 run, Notepad foreground/WM_GETTEXT, environment (UAC, layouts, voices, Vulkan layers)

### Secondary (MEDIUM confidence)
- Handy README (implicit-layer guidance; does not name the variable it sets beyond preserving `VK_LOADER_LAYERS_DISABLE`)
- pypistats (edge-tts downloads)

### Tertiary (LOW confidence)
- Windows default layout-toggle behavior with a suppressed Space; AutoHotkey mask-key technique; `runas /trustlevel` IL (all [ASSUMED])

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH (versions from the registry; Whisper.net/NAudio APIs read from tagged source; CsWin32 and xunit compiled and run)
- Engine/native/latency: HIGH for "works on Win10 19045 and FA matters"; MEDIUM for exact p95 (2–3 runs only)
- Hook/paste: MEDIUM (official docs + signature generation; not exercised end-to-end here)
- E2E harness: MEDIUM (foreground lock observed; the workaround succeeded once)
- Pitfalls: HIGH for the probe-observed ones (hallucinations, no_speech=0, encoding, culture, elevation); LOW for [ASSUMED] ones

**Research date:** 2026-10-22
**Valid until:** 2026-11-21 (NAudio 3.3.0 and CsWin32 0.3.367 are days old; re-check for patch releases at plan time)
