# Windows 10 / Whisper.net 1.9.1 empirical spike

Date: 2026-10-22. Throwaway workspace: `C:\somning-spike`.

## Answer to CROSS-REVIEW H1

**Yes: Whisper.net 1.9.1 runs on this Windows 10 Pro 19045 machine from a self-contained win-x64 directory publish, using both the CPU runtime and Vulkan on the AMD RX 7600.** Both q5_0 and f16 large-v3-turbo models completed five fresh-process runs per backend (20 completed transcriptions). No CPU-package fallback was permitted in Vulkan runs. Native logs identify the RX 7600 and GPU buffers, and loaded-module records identify the published Vulkan DLLs and AMD driver.

H1 is therefore not an observed MVP blocker on this machine with these exact dependencies. This demonstrates execution despite the documented OS support mismatch; it does not expand upstream support policy or prove compatibility on a clean Windows 10 machine, older drivers, or missing C++ runtimes.

## Machine and dependencies

- OS: Microsoft Windows 10 Pro, version 10.0.19045, build 19045.
- CPU: AMD Ryzen 5 5600X 6-Core Processor; 6 cores / 12 logical processors; decoder threads explicitly set to 6.
- Configured hardware context: 16 GB RAM; RX 7600 8 GB.
- Display adapter: Parsec Virtual Display Adapter; driver 0.45.0.0.
- Display adapter: AMD Radeon RX 7600; driver 32.0.31041.1004.
- Build SDK: .NET SDK 10.0.203. Published application runtime: .NET 10.0.7, x64.
- NuGet references: `Whisper.net`, `Whisper.net.Runtime`, `Whisper.net.Runtime.Vulkan`, all exactly `1.9.1`.
- Pre-existing VC++ x64 runtime: Installed=1; registry version `v14.51.36247.00`.
- System dependency: `C:\Windows\System32\vulkan-1.dll` version `1.4.341.0`.
- System dependency: `C:\Windows\System32\vcruntime140.dll` version `14.51.36247.0`.
- System dependency: `C:\Windows\System32\vcruntime140_1.dll` version `14.51.36247.0`.
- System dependency: `C:\Windows\System32\msvcp140.dll` version `14.51.36247.0`.

No SDK, Vulkan SDK or VC++ redistributable was installed by this spike. The existing C++ runtime and graphics driver were left intact.

## Reproduction and test method

The source project, runner scripts, model files, audio and raw evidence remain under `C:\somning-spike`. The Somning repository was used only for this report.

```powershell
dotnet new console -f net10.0 --name WhisperSpike --output C:\somning-spike\app
dotnet add C:\somning-spike\app\WhisperSpike.csproj package Whisper.net --version 1.9.1
dotnet add C:\somning-spike\app\WhisperSpike.csproj package Whisper.net.Runtime --version 1.9.1
dotnet add C:\somning-spike\app\WhisperSpike.csproj package Whisper.net.Runtime.Vulkan --version 1.9.1
dotnet publish C:\somning-spike\app\WhisperSpike.csproj -c Release -r win-x64 --self-contained -o C:\somning-spike\publish
# Run the matrix, spawning a new powershell.exe -NoProfile and a new exe per run:
& C:\somning-spike\RunAll.ps1
```

The tested output is a directory publish, not single-file or NativeAOT. There was no `dotnet run` or `dotnet app.dll` execution in the benchmark. Each published exe started from the results directory, outside its publish directory.

Each child PowerShell used PATH=`C:\Windows\System32;C:\Windows`; `Get-Command dotnet` returned no command. `DOTNET_ROOT` and `DOTNET_ROOT_X64` pointed at a nonexistent spike-local directory, `DOTNET_MULTILEVEL_LOOKUP=0`, and Vulkan SDK / .NET injection environment variables were cleared. Per-run module snapshots confirm `coreclr.dll` loaded from `C:\somning-spike\publish\coreclr.dll`, rather than an installed shared runtime. These measures demonstrate no runtime dependence on the installed .NET SDK; they do not remove system graphics/C++ runtime dependencies.

Before the first WhisperFactory/native call, Program.cs sets `RuntimeOptions.RuntimeLibraryOrder` to exactly `[RuntimeLibrary.Vulkan]` or `[RuntimeLibrary.Cpu]`. It also passes `WhisperFactoryOptions.UseGpu=true` or `false`, respectively. Processor settings are `.WithLanguage("pt").WithThreads(6)`; remaining decoding defaults are unchanged. No prompt, VAD, manual trimming, flash-attention opt-in or cloud transcription was used.

`load_ms` measures factory/native/model creation plus processor construction. `infer_ms` measures consuming `ProcessAsync` for the WAV, including its audio processing and transcription. Each run reloads the model in a fresh process; these are not repeated in-process warm-model measurements. Windows file and driver caches were not cleared. The slower first Vulkan call is retained in the data; a cache/initialization effect is plausible but was not isolated experimentally. App startup, module enumeration, result serialization and disposal are outside these two timing intervals.

Build completed successfully with no reported warnings/errors. Runs were sequential, with a 180-second timeout each; there were no timeouts.

## Audio and model identity

- WAV: `C:\somning-spike\audio\pt-br.wav`; 8.400 seconds; PCM signed 16-bit little-endian; 16,000 Hz; mono (verified by ffprobe).
- Generation: isolated `uv` environment under the spike, `edge-tts 7.3.1`, voice `pt-BR-FranciscaNeural`; ffmpeg resampled the generated MP3 to the required WAV. Edge TTS used network access to create the fixture; Whisper inference was local.
- Literal TTS input: `Hoje vamos testar o reconhecimento de voz em portugues do Brasil. O aplicativo deve escrever esta frase com rapidez e precisao.`
- This is one synthetic sentence, suitable for runtime/functionality testing; it is not a representative pt-BR quality benchmark.

| Model | Bytes | SHA-256 |
|---|---:|---|
| `ggml-large-v3-turbo-q5_0.bin` | 574041195 | `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2` |
| `ggml-large-v3-turbo.bin` | 1624555275 | `1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69` |

Download sources: [q5_0 model](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin), [f16 model](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin). Hashes above were calculated locally after download; no independently pinned upstream revision/hash was required for this throwaway test.

## Timings: all 20 fresh-process runs

| Model | Requested backend | Run | Loaded runtime | load_ms | infer_ms | Completion | Exit code |
|---|---|---:|---|---:|---:|---|---|
| q5 | vulkan | 1 | Vulkan | 792.30 | 6286.15 | transcript produced | not captured* |
| q5 | vulkan | 2 | Vulkan | 819.08 | 3247.43 | transcript produced | not captured* |
| q5 | vulkan | 3 | Vulkan | 821.53 | 3448.68 | transcript produced | not captured* |
| q5 | vulkan | 4 | Vulkan | 737.31 | 3216.56 | transcript produced | not captured* |
| q5 | vulkan | 5 | Vulkan | 786.03 | 3250.57 | transcript produced | not captured* |
| q5 | cpu | 1 | Cpu | 544.27 | 17637.20 | transcript produced | not captured* |
| q5 | cpu | 2 | Cpu | 544.51 | 17464.97 | transcript produced | 0 |
| q5 | cpu | 3 | Cpu | 592.95 | 17352.06 | transcript produced | 0 |
| q5 | cpu | 4 | Cpu | 589.09 | 17298.73 | transcript produced | 0 |
| q5 | cpu | 5 | Cpu | 547.59 | 17392.97 | transcript produced | 0 |
| f16 | vulkan | 1 | Vulkan | 2477.02 | 6866.16 | transcript produced | 0 |
| f16 | vulkan | 2 | Vulkan | 2376.77 | 8474.65 | transcript produced | 0 |
| f16 | vulkan | 3 | Vulkan | 2522.48 | 7069.15 | transcript produced | 0 |
| f16 | vulkan | 4 | Vulkan | 2440.31 | 5183.64 | transcript produced | 0 |
| f16 | vulkan | 5 | Vulkan | 2238.60 | 3574.45 | transcript produced | 0 |
| f16 | cpu | 1 | Cpu | 1925.49 | 22059.64 | transcript produced | 0 |
| f16 | cpu | 2 | Cpu | 1790.82 | 22417.93 | transcript produced | 0 |
| f16 | cpu | 3 | Cpu | 1964.05 | 23606.93 | transcript produced | 0 |
| f16 | cpu | 4 | Cpu | 1893.39 | 22993.54 | transcript produced | 0 |
| f16 | cpu | 5 | Cpu | 1810.16 | 22881.16 | transcript produced | 0 |

*Initial runner instrumentation returned null ExitCode for the first five q5 Vulkan runs and the first q5 CPU run despite completed success JSON/transcripts. Retaining the Process handle before waiting fixed exit-code capture for subsequent runs. Those initial exit codes are unknown, not assumed to be zero. No transcription exception, timeout or native error was observed in their logs. The raw evidence retains this distinction.

| Model | Backend | Median load_ms | Median infer_ms | Min-max infer_ms | Median inference / audio duration |
|---|---|---:|---:|---|---:|
| q5 | vulkan | 792.30 | 3250.57 | 3216.56-6286.15 | 0.387 |
| q5 | cpu | 547.59 | 17392.97 | 17298.73-17637.20 | 2.071 |
| f16 | vulkan | 2440.31 | 6866.16 | 3574.45-8474.65 | 0.817 |
| f16 | cpu | 1893.39 | 22881.16 | 22059.64-23606.93 | 2.724 |

Five runs per configuration do not establish a robust p95 or long-run reliability claim. Do not extrapolate these timings to 30-second natural utterances or a permanently preloaded application.

## Transcripts and errors

Runs matching this transcript: 20/20.

> Hoje vamos testar o reconhecimento de voz em português do Brasil. O aplicativo deve escrever esta frase com rapidez e precisão.

No managed transcription exception, native error or timeout was observed. Raw stdout/stderr is retained per run, including all native logging. These results establish this sentence is transcribed successfully, not general pt-BR WER or real-microphone behavior.

## Native runtime and GPU evidence

Representative Vulkan native log lines:

```text
[Debug] Successfully loaded whisper library from C:\somning-spike\publish\runtimes\vulkan\win-x64\whisper.dll
[Info] whisper_init_with_params_no_state: use gpu    = 1
[Info] ggml_vulkan: Found 1 Vulkan devices:
[Info] ggml_vulkan: 0 = AMD Radeon RX 7600 (AMD proprietary driver) | uma: 0 | fp16: 1 | bf16: 1 | warp size: 64 | shared memory: 32768 | int dot: 1 | matrix cores: KHR_coopmat
[Info] whisper_model_load:      Vulkan0 total size =   573.40 MB
[Info] whisper_backend_init_gpu: device 0: Vulkan0 (type: 1)
[Info] whisper_backend_init_gpu: found GPU device 0: Vulkan0 (type: 1, cnt: 0)
[Info] whisper_backend_init_gpu: using Vulkan0 backend
```

Loaded modules for the first q5 Vulkan run:

| Module | Actual loaded path |
|---|---|
| `amdvlk64.dll` | `C:\Windows\System32\DriverStore\FileRepository\amdvlk.inf_amd64_914ba89eaaafdf60\amdvlk64.dll` |
| `coreclr.dll` | `C:\somning-spike\publish\coreclr.dll` |
| `ggml-base-whisper.dll` | `C:\somning-spike\publish\runtimes\vulkan\win-x64\ggml-base-whisper.dll` |
| `ggml-cpu-whisper.dll` | `C:\somning-spike\publish\runtimes\vulkan\win-x64\ggml-cpu-whisper.dll` |
| `ggml-vulkan-whisper.dll` | `C:\somning-spike\publish\runtimes\vulkan\win-x64\ggml-vulkan-whisper.dll` |
| `ggml-whisper.dll` | `C:\somning-spike\publish\runtimes\vulkan\win-x64\ggml-whisper.dll` |
| `MSVCP140.dll` | `C:\Windows\SYSTEM32\MSVCP140.dll` |
| `VCOMP140.DLL` | `C:\Windows\SYSTEM32\VCOMP140.DLL` |
| `VCRUNTIME140_1.dll` | `C:\Windows\SYSTEM32\VCRUNTIME140_1.dll` |
| `VCRUNTIME140.dll` | `C:\Windows\SYSTEM32\VCRUNTIME140.dll` |
| `vulkan-1.dll` | `C:\Windows\SYSTEM32\vulkan-1.dll` |
| `whisper.dll` | `C:\somning-spike\publish\runtimes\vulkan\win-x64\whisper.dll` |
| `Whisper.net.dll` | `C:\somning-spike\publish\Whisper.net.dll` |
| `WhisperSpike.dll` | `C:\somning-spike\publish\WhisperSpike.dll` |
| `WhisperSpike.exe` | `C:\somning-spike\publish\WhisperSpike.exe` |

CPU runs load `whisper.dll`, `ggml-whisper.dll`, `ggml-base-whisper.dll` and `ggml-cpu-whisper.dll` from `publish\runtimes\win-x64`; their module snapshots contain no Vulkan backend or Vulkan loader. CPU runtime info reports AVX2/F16C/FMA support. This verifies that the two configurations used distinct requested runtime packages.

## Published native files

| Relative path under publish | Bytes |
|---|---:|
| `runtimes/vulkan/win-x64/ggml-base-whisper.dll` | 636928 |
| `runtimes/vulkan/win-x64/ggml-cpu-whisper.dll` | 782848 |
| `runtimes/vulkan/win-x64/ggml-vulkan-whisper.dll` | 58013696 |
| `runtimes/vulkan/win-x64/ggml-whisper.dll` | 67072 |
| `runtimes/vulkan/win-x64/whisper.dll` | 484864 |
| `runtimes/win-x64/ggml-base-whisper.dll` | 636928 |
| `runtimes/win-x64/ggml-cpu-whisper.dll` | 782848 |
| `runtimes/win-x64/ggml-whisper.dll` | 67072 |
| `runtimes/win-x64/whisper.dll` | 484864 |

The full unmodified publish contains 240 files totaling 219,947,663 bytes, excluding external models/audio. In addition to the Windows x64 runtime files above and .NET runtime DLLs, package content includes other-platform native files; no pruning was done. Full inventory: `C:\somning-spike\results\publish-inventory.csv`.

## VC++ redistributable and Vulkan SDK answer

**VC++: the tested process depends on existing system C++ runtime DLLs.** Both backends loaded `MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll` and OpenMP `VCOMP140.DLL` from Windows System32, not the publish directory. PE import-table inspection independently confirms the native binaries import the C++ runtime DLLs, and the base/CPU backend imports VCOMP140.DLL. Exact per-DLL imports are saved in `C:\somning-spike\results\native-imports.json`. They were already installed at version 14.51.36247.0. No redist installation was necessary on THIS machine. A bare-machine/no-redist test was not performed, and system redistributables were not removed. Therefore this result is not evidence that an installer can omit the prerequisite; the minimum compatible redist version was not determined.

**Vulkan SDK: no SDK PATH or SDK environment variable was needed to run.** The process loaded the system Vulkan loader 1.4.341.0 and AMD driver DLL from DriverStore. The test did not uninstall any globally present SDK, but loaded-module paths show execution used the system loader/driver and published backend, not an SDK directory. No claim about older Vulkan loaders/drivers is made.

## Implications and limits

- H1: execution verified on the owner's exact Windows 10 machine; no observed runtime compatibility blocker in this matrix.
- Keep the self-contained directory layout as the known-working baseline. Single-file extraction, installer behavior and clean-machine deployment remain untested.
- Vulkan substantially outperforms CPU on this fixture. CPU is a functioning availability fallback but is too slow here to support the research document's optimistic few-second latency expectation.
- Quantization/default decoding produced a successful fixture transcript; general f16/q5 accuracy equivalence is not established.
- This spike does not test GPU-to-CPU recovery after a crash, missing-dependency behavior, sustained recording, Silero VAD, hotkeys, WPF overlay, clipboard or paste.
- Background workload, page cache and GPU driver cache were not controlled; the first-run penalty is measured, not attributed conclusively.

## Evidence locations

- `C:\somning-spike\app\Program.cs` and `WhisperSpike.csproj`: exact source/settings/package references.
- `C:\somning-spike\RunAll.ps1` and `RunOne.ps1`: test matrix and sanitized child shell runner.
- `C:\somning-spike\results\{q5|f16}-{vulkan|cpu}-{1..5}.json`: timings, transcript, runtime info and loaded-module paths.
- Adjacent `.environment.json`, `.exit.json`, `.stdout.log`, `.stderr.log`: launch isolation, completion metadata and exact logs.
- `machine.json`, `model-hashes.json`, `audio-info.json`, `publish-inventory.csv`, `publish.log`, `run-summary.log`: machine/build/input evidence.

Context sources: [Whisper.net 1.9.1 documented prerequisites](https://github.com/sandrohanea/whisper.net/blob/1.9.1/readme.md), [runtime selection options](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/LibraryLoader/RuntimeOptions.cs). The pass/fail and timing conclusions in this report come from local execution, not those documents.
