# Phase 1 independent cross-AI plan review

Date: 2026-10-22  
Scope: 01-01 through 01-08 PLAN files, SKELETON.md, 01-CONTEXT.md, 01-RESEARCH.md, SPIKE-WIN10-WHISPERNET.md, and Phase 1 requirements/success criteria.

**Findings: 10 HIGH, 12 MEDIUM, 0 LOW.** Execution should not begin unchanged. This is a plan review, not a claim that the unimplemented product already exhibits every failure. Where implementation discretion could avoid a problem, the finding identifies the missing contract or incorrect prescribed recipe and the required acceptance test.

Only this review file was created. Plans and project code were not edited. Checks were read-only: plan inspection, parsing verification strings with the installed Windows PowerShell parser, inspecting the installed runtime's API surface, and reading tagged upstream source/documentation. No phase implementation or desktop-input experiment was run.

## HIGH

### H1. Eighteen of twenty task verification commands cannot run as written in the specified shell

**Plan + task:** 01-01 T1/T2; 01-02 T1; 01-03 T1/T2/T3; 01-04 T1/T2; 01-05 T1/T2/T3; 01-06 T1/T2; 01-07 T1/T2/T3; 01-08 T1/T2.

**Evidence:** The machine runs Windows PowerShell **5.1.19041.6392**. Parsing all 20 `<automated>` strings with `System.Management.Automation.Language.Parser.ParseInput` produced **12 parse failures**, due to `&&`. Six additional strings begin with POSIX `SOMNING_STRICT=1 dotnet ...`; PowerShell parses that as a command named `SOMNING_STRICT=1`, not an environment assignment. Only 01-02 T2 and 01-08 T3 avoid both defects. Examples: 01-01:202,242; 01-02:113; 01-07:209. Some checks additionally use Bash `test`, quoting and command substitution. Git's grep/du/tail are present, so their mere existence is not the blocker; shell syntax and error propagation are.

**Failure:** Autonomous execution stops at verification even after correct implementation. Simply substituting semicolons can instead produce false passes: PowerShell 5.1's `$ErrorActionPreference='Stop'` does not automatically turn a nonzero native-process exit into a terminating error.

**Concrete fix:** Put each task check in a PowerShell 5.1-compatible script or checked script block. Set `$env:SOMNING_STRICT='1'`, invoke each native command separately, and immediately fail on `$LASTEXITCODE -ne 0`. Use `Test-Path`/`Select-String` or explicitly invoke a supported shell for non-PowerShell checks. Restore environment changes in `finally`. Re-parse every replacement command with the installed parser, then run it; preserve MTP exit 8 as a failure for empty filters.

**Sources:** Local parser results; [PowerShell chain operators start in PowerShell 7](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_pipeline_chain_operators); [PowerShell executable argument/exit behavior](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_powershell_exe?view=powershell-5.1).

### H2. Core reducer contracts depend on App-owned types, creating an impossible project dependency

**Plan + task:** 01-07 T1; originating contracts in 01-01 T1 and 01-06 T1.

**Evidence:** 01-01:144 places `EngineTranscription` in `Somning.App.Engine` and its file under `src/Somning.App/Engine`. 01-06 places `EngineStatus` in that same App project. However, 01-07:111 defines Core's `TranscriptionCompleted(..., EngineTranscription Result)` and `EngineStatusChanged(EngineStatus)`. App already references Core. The binding-name section says to consume existing signatures unchanged. Core.Tests references Core and Protocol, not App.

**Failure:** Core cannot compile these events without an upward App dependency; adding that dependency creates an App -> Core -> App cycle and defeats the pure Core boundary. This is a shared-contract ownership defect, not a missing `using`.

**Concrete fix:** Before wave 2, put engine-result/status domain DTOs in `Somning.Core.Engine`, with the supervisor consuming them, or map App DTOs into explicitly separate Core event payloads at the host boundary. Keep only transport DTOs in Protocol. Update the binding contracts and affected file ownership together. Verify Core/Core.Tests build without any reference to App or Platform.Windows.

**Sources:** 01-01 `<interfaces>` and scaffold project graph; 01-06 `<interfaces>`/files; 01-07:103-114; SKELETON Core-purity decision.

### H3. Suppressed Space cannot be polled as physical key state; long PTT recordings stop early

**Plan + task:** 01-02 T1/T2; 01-07 T1; 01-08 T1.

**Evidence:** 01-02:80 prescribes `GetAsyncKeyState(VK_SPACE) & 0x8000` for `IsTriggerPhysicallyDown`. T1 suppresses the Space down/up events. T2 stops a presumed-held recording after that query reports false for >1 second. 01-07 polls this every 50 ms. A low-level-hook-swallowed event does not reach Windows' asynchronous key-state update; moving the query outside the callback does not repair that. Microsoft's PowerToys test guidance explicitly uses an unchanged async state to diagnose swallowed input.

**Failure:** During a correctly held chord, Space can remain logically up in the OS. The missed-release safety therefore stops recording after about one second rather than at physical release. The long hold E2E fixture will be truncated and HOTK-03 fails.

**Concrete fix:** Do not infer release of an intentionally suppressed trigger from `GetAsyncKeyState`. Maintain hook-observed down/up state, detect dropped queue events explicitly, and use a validated independent signal if a genuine missed-key-up watchdog is required. Do not replace this with an untested Raw Input assumption: swallowed input can also affect downstream listeners. Add a real-hook test holding the suppressed trigger for >3 seconds, asserting no premature stop, followed by release and exactly one stop; test watchdog recovery separately.

**Sources:** 01-02:80,98-100,133,141; 01-07:132; [Microsoft PowerToys, Recipe 19](https://github.com/microsoft/PowerToys/blob/main/.github/skills/ui-tests-migration/references/patterns-and-pitfalls.md#recipe-19--tell-a-swallowed-input-event-apart-from-a-product-bug); [LowLevelKeyboardProc timing and suppression](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc).

### H4. PasteTarget captures the focused control but PasteGate never checks it again

**Plan + task:** 01-04 T1/T2; consumed by 01-07 T1 and 01-08 T2.

**Evidence:** 01-04:78 includes `FocusHwnd` in PasteTarget, but :79 supplies only `CurrentForegroundHwnd` to PasteContext. T1/T2 and all decision-table rows compare top-level foreground HWND only. No same-top-level-window focus-change test exists.

**Failure:** Stop in browser field A, switch to browser field B or another tab while inference runs, and the top-level HWND remains identical. The gate approves Ctrl+V into B. The current two-Notepad test passes while this wrong-field paste remains possible. A failed `GetGUIThreadInfo` can likewise leave an unusable focus handle without preventing paste.

**Concrete fix:** Carry current focused-control HWND and process identity into the gate, check capture/query success, and revalidate immediately before injection. Defer on a changed/unknown focus target or invalid target. Add a two-edit-controls-in-one-window test. Document that HWND identity alone cannot distinguish every DOM field; use a stronger supported target identity or conservative deferral for such cases rather than claiming universal field safety.

**Sources:** 01-04:78-83,98-104 and T2 decision table; [GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo); [SendInput delivers to the current input target](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

### H5. NAudio 3.3 stream ownership makes the prescribed spool-disposal recipe incomplete

**Plan + task:** 01-03 T1/T3; 01-07 T1 stop effect.

**Evidence:** 01-03:110 passes a newly constructed writable FileStream to `WaveFileWriter` and says only that Dispose finalizes. 01-07:133 stops capture and disposes the writer before passing the path to the engine. In **NAudio v3.3.0**, the stream-taking constructor explicitly leaves the supplied stream open; only the filename constructor owns its stream. The plan does not require SpoolWriter to retain/dispose that FileStream independently.

**Failure:** Following the minimal recipe leaves a writable file handle open after capture stops. A normal `File.OpenRead` with read-only sharing can then fail with a sharing violation against that existing writer; repair/deletion also fails, and handles accumulate. This is a version-specific trap even if an executor might independently add correct ownership.

**Concrete fix:** SpoolWriter must own both objects: stop/drain callbacks, finalize/dispose WaveFileWriter, then dispose the underlying FileStream in `finally`, including start/error paths. Preserve CreateNew semantics rather than replacing it blindly with a truncating filename constructor. Add an assertion that immediately after Stop/Dispose another process can open the WAV and the original process can reopen it with exclusive read/write sharing and delete it.

**Sources:** 01-03:110,173; 01-07:133; [NAudio v3.3.0 WaveFileWriter constructor/disposal source](https://github.com/naudio/NAudio/blob/v3.3.0/src/NAudio.Core/Wave/WaveOutputs/WaveFileWriter.cs); [Windows file-sharing rules](https://learn.microsoft.com/en-us/windows/win32/fileio/creating-and-opening-files).

### H6. Failed transcription/clipboard delivery can be marked terminal and later deleted without recovery

**Plan + task:** 01-03 T3; 01-04 T1; 01-07 T1/T2/T3; related 01-01 T1 CLI path.

**Evidence:** 01-04:82,101 returns a distinct `DeliveryResult.Outcome="clipboard_failed"`. 01-07's reducer handles pasted/paste_deferred, empty, transcription error and capture failure, but provides no clipboard_failed transition. It prescribes `UpdateSidecar(failed)` on transcription error. 01-03 T3 excludes Failed from recovery and allows terminal recordings to age out under retention. RecoveryService :201 marks text recovered after calling SetText without explicitly requiring its Success result. The CLI at 01-01:194 similarly says exit 0 on text, not on successful clipboard delivery.

**Failure:** A temporary clipboard lock or exhausted worker retry can strand the reducer or falsely complete a recording. The next startup can skip that recording, or mark it recovered even though no text reached the clipboard; retention may later delete it. This violates AUD-04 and OUT-01.

**Concrete fix:** Define clipboard_failed explicitly: retain durable text in Transcribed with a retryable delivery error, emit one error metric, and keep it eligible/protected for recovery. Distinguish retryable engine/capture failures from permanent rejected input; reaching a retry cap must not authorize silent deletion of undelivered speech. Recovery should reuse saved text where available, check ClipboardSetResult.Success, and advance only on confirmed success. CLI success must require clipboard success. Test held-open clipboard, worker retry exhaustion, and restart after each failure.

**Sources:** 01-03 RecoveryPlanner/retention behaviors; 01-04 DeliveryResult and T1; 01-07:131,164,167,201; 01-01:194; REQUIREMENTS AUD-04/OUT-01.

### H7. Wave-2 verification is not safe to run in parallel even though source ownership is mostly disjoint

**Plan + task:** Wave 2, 01-02 through 01-06 all verification; especially 01-05 T3.

**Evidence:** All five depend only on 01-01 and run `dotnet test` against the same projects/build outputs. 01-02 installs a global suppressing hook; 01-04 manipulates foreground/clipboard; 01-03 and 01-06 run engine processes; 01-05 benchmarks the GPU/CPU and sets the clipboard while editing EngineDefaults and the solution. 01-01:199 disables xUnit parallelism only within one test process. It does not serialize five agent-launched processes, restores/builds sharing obj/bin, or benchmark workloads. 01-05 T3 also permits running configurations in the background.

**Failure:** Hooks consume one another's injected chords, clipboard/foreground assertions race, native models compete for RAM/VRAM, build outputs are locked or change under tests, and p95 selection is contaminated by concurrent jobs. A clean source merge does not make those results valid.

**Concrete fix:** Add explicit cross-process resource scheduling: serialized desktop/clipboard/hook tests and an exclusive benchmark interval with no competing engine workload. Use isolated worktrees/output roots for concurrent compilation, or serialize shared-project build/restore; never test against a binary being overwritten. Publish benchmark artifacts once from a stable integrated revision. Per-test data directories alone are insufficient. Keep wave-2 source implementation parallel if desired, but make verification dependencies/locks binding.

**Sources:** Wave-2 frontmatter and verify blocks; 01-01:199; 01-05 T3; SKELETON test/deployment decisions.

### H8. Reducer/effect contracts cannot safely represent the overlapping recordings they require

**Plan + task:** 01-07 T1/T2; conflicts with the assumption in 01-06 T1.

**Evidence:** 01-07:110 has one Current recording and a queue of IDs only. T1 :131 sends DeliveryCompleted back to Idle. T2 :162 requires Start while Busy to create a new recording while the previous result is still pending. The older recording's target/path/metrics must survive, but no per-ID state store is specified. Effects run asynchronously, while IAudioSource.StartAsync may still be opening the mic when Stop arrives. The 50 ms timer is also described as computing OnTick results before posting, whereas key events call the same interpreter on the owner loop.

**Failure:** Sequence A stop -> B start -> A completes can reset the app to Idle while B is recording, deliver A using B's Current target, or dispose the wrong writer. A fast tap can race mic startup/stop. Calling OnTick on the timer thread can mutate a supposedly single-owner interpreter concurrently with OnKey.

**Concrete fix:** Represent capture lifecycle separately from the FIFO processing queue, with complete immutable per-ID records for paths, targets, timings and delivery state. Old completion events must update only their ID and never clear another capture. Serialize start/stop/dispose per capture, including Stop while Starting and adapter exceptions. Timer callbacks should enqueue Tick events; only the owning loop invokes OnTick/OnKey. Await persistence prerequisites before delivery rather than fire-and-forget all effects. Add interleaved A/B completion and delayed-mic-start tests.

**Sources:** 01-07:110-113,131-134,162; 01-06:92; 01-03 IAudioSource/WasapiAudioSource contracts.

### H9. Startup recovery blocks the hook until readiness, contradicting STT-04

**Plan + task:** 01-07 T3 and its key link; conflicts with T1 and 01-06 T2.

**Evidence:** 01-01 defines StartAsync as awaiting Ready. 01-07:201 says Program runs RecoveryService after StartAsync and before KeyboardHook.Start. Its key link explicitly says recovery after engine ready, before the hook starts. T1 :134 and the must-haves instead require the hook to work before readiness. The supervisor's queue-before-ready test does not test app startup.

**Failure:** During model load/warm-up, and potentially a backlog of recovery transcriptions, no hook is installed. An early physical chord is dropped or leaks to the focused app; it cannot reach the supervisor's queue. This fails STT-04 even when EngineSupervisorTests passes.

**Concrete fix:** Define one startup state machine: install the hook/event loop and accept durable capture before engine Ready, while sequencing recovery clipboard writes so they cannot overwrite new deliveries unexpectedly. Alternatively explicitly revise the requirement, rather than claim it is met. Add an App E2E with a deliberately delayed Ready message, inject a chord during that delay, and verify the recording/result survives and is delivered after readiness.

**Sources:** 01-01:192; 01-07 key_links and :134,201; CONTEXT D-05; REQUIREMENTS STT-04.

### H10. The harness's next-outcome contract can lose the outcome before the test subscribes

**Plan + task:** 01-07 T3; 01-08 T1/T2.

**Evidence:** 01-07:116 specifies `wait-outcome` returns the **next** metrics event. 01-08's helpers trigger a stop, then call WaitOutcomeAsync. Empty/too-short outcomes can finish during the stop chord's remaining key-up delay. The crash-recovery test restarts App, waits for readiness, then waits for recovered output that may already have occurred before the pipe client connected. Status exposes last metrics but the wait contract does not specify replay/cursors/ID matching.

**Failure:** A correct fast empty result or startup recovery causes a 60-second timeout. Polling or retrying the gesture can instead consume the wrong recording's result and conceal defects.

**Concrete fix:** Give outcomes monotonic sequence numbers and recording IDs, retain them until acknowledged, and support waiting for a specified ID or after a known cursor, including already-completed outcomes. Expose recovery-complete and replayable startup recovery events. Alternatively register the waiter before the action, but still handle events produced before pipe connection. Test an outcome completed before wait registration and recovery completed before the harness client attaches.

**Sources:** 01-07:116,203; 01-08 T1 helper/action and T2 App_killed_mid_recording_recovers_on_restart.

## MEDIUM

### M1. VAD CPU selection uses an option the native VAD loader ignores

**Plan + task:** 01-05 T1; copied from 01-RESEARCH Pattern 3.

**Evidence:** :136 passes `WhisperFactoryOptions { UseGpu=false }` to WhisperVadFactory, then builds VAD without `.WithUseGpu(false)`. In tag 1.9.1, `WhisperProcessorModelFileLoader.LoadNativeVadContext` forwards the builder's `WhisperVadContextParams` directly, unlike LoadNativeContext, which maps factory options. The builder initializes native defaults and changes UseGpu only through WithUseGpu.

**Failure:** The promised CPU VAD is not actually enforced in a Vulkan worker, changing initialization/performance/failure behavior.

**Concrete fix:** Call `vadFactory.CreateBuilder().WithUseGpu(false).WithThreads(...).WithThreshold(...)...Build()`. Measure readiness/VAD timing for this actual configuration. Add an assertion/log that identifies the intended VAD backend.

**Sources:** [1.9.1 file loader](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/Internals/ModelLoader/WhisperProcessorModelFileLoader.cs), [VAD builder](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/WhisperVadProcessorBuilder.cs).

### M2. Unpaced FileAudioSource has an infinite silence tail but the test expects a bounded-duration file

**Plan + task:** 01-03 T1.

**Evidence:** :112 says the source continues writing silence until StopAsync even after EOF; :113 uses `realTime:false`, runs until source duration plus 0.5 seconds, then expects output duration within 0.6 seconds of the source. With pacing disabled there is no audio-time/EOF completion contract to bound that tail.

**Failure:** A wall-clock wait lets the unpaced loop write far more silence than expected, producing a huge WAV or a deterministic duration assertion failure. Awaiting a source pump that never ends can also deadlock the test.

**Concrete fix:** Define finite EOF completion for unpaced mode, expose completion/sample count, and append an explicit bounded sample count if needed. Restrict ongoing EOF-silence padding to paced real-time harness mode. Tests should stop by processed audio-frame count or EOF, not elapsed wall time on an unpaced producer.

**Sources:** 01-03:112-113; CONTEXT D-19.

### M3. Cancellation cannot be received while the prescribed request loop awaits inference

**Plan + task:** 01-01 T1 worker RequestLoop; 01-06 timeout/shutdown handling.

**Evidence:** 01-01:185 describes one read loop that processes a transcribe request through ProcessAsync, and also handles cancel by cancelling the in-flight CTS. No independent input/control reader is specified. Awaiting inference before reading the next line means cancel/shutdown cannot be read until inference finishes.

**Failure:** Worker protocol cancellation is ineffective and graceful shutdown can hang behind a long or stuck request. Esc UX is deferred, but the worker protocol already promises cancellation and the supervisor needs bounded shutdown.

**Concrete fix:** Keep a responsive control reader separate from the serialized inference executor; cancel only the matching active ID and define queued-ID behavior. Do not run concurrent Whisper calls. Test a long request followed immediately by cancel and shutdown with bounded completion; retain process termination as the last-resort timeout path.

**Sources:** 01-01:185; [WhisperProcessor cancellation path](https://github.com/sandrohanea/whisper.net/blob/1.9.1/Whisper.net/WhisperProcessor.cs).

### M4. PathGuard's string-prefix rule accepts sibling directories

**Plan + task:** 01-01 T1 PathGuard.

**Evidence:** :185 requires Path.GetFullPath to start with the canonical allowed-dir string. With allowed `C:\data\recordings`, `C:\data\recordings-other\x.wav` also starts with it. No trailing-separator/boundary rule or Windows case-comparison rule is stated.

**Failure:** The declared recordings-only trust boundary is not enforced. This is a local IPC validation defect, not a claim of a remote attack surface.

**Concrete fix:** Canonicalize the root with a directory separator and compare using OrdinalIgnoreCase, or use a validated relative-path containment check rejecting rooted/parent escapes. Decide explicitly whether junctions/reparse points are permitted. Test sibling-prefix, parent traversal, different case and valid nested paths.

**Sources:** 01-01:185 and T-01-01; RESEARCH Security Domain V5.

### M5. The filter deliberately deletes legitimate short Portuguese utterances

**Plan + task:** 01-05 T1/T2; inherited product issue in CONTEXT D-12.

**Evidence:** T2 explicitly requires `Obrigado.` and `E aí` to be discarded regardless of the preceding Silero speech decision. Those are valid everyday pt-BR dictations. VAD confirming real speech does not make the recognized words themselves distinguishable from a subtitle hallucination.

**Failure:** The product passes its synthetic non-speech tests while refusing genuine short messages. This conflicts with STT-01's usable dictation goal even though the plan accurately follows the current D-12 lock.

**Concrete fix:** Resolve D-12 at planning level: use the non-speech gate as the primary rejection signal and apply phrase suppression only with additional suspicion evidence, rather than banning normal speech unconditionally. Add spoken Obrigado/E aí fixtures expected to survive as well as non-speech fixtures expected empty. Do not silently change a locked decision during execution; explicitly document this product tradeoff if it is retained.

**Sources:** 01-05 T2 behavior table; CONTEXT D-12; REQUIREMENTS STT-01/STT-03; 01-RESEARCH silence probe (same words occur as hallucinations, which does not prove they are always hallucinations).

### M6. Prefix regexes do not match the normalized representation they are applied to

**Plan + task:** 01-05 T2.

**Evidence:** :173 prescribes stripping punctuation before matching but keeps patterns `^inscreva-se` and `amara\.org`. Hyphens/dots have disappeared or become spaces by then. Exact normalized list entries may still catch the exact phrase, so the existing exact-match tests do not prove prefix detection works.

**Failure:** Variants such as `Inscreva-se no canal` evade the intended prefix safety net, while an implementation following the tests appears correct.

**Concrete fix:** Specify one representation for each rule: match raw/case-folded text with punctuation-aware regexes, or normalize both patterns and inputs consistently. Anchor rules deliberately; avoid an accidental unanchored substring ban. Add prefix-extension and punctuation-variant tests plus legitimate containing-sentence tests. Apply the product correction in M5 separately.

**Sources:** 01-05:173 and T2 behavior table.

### M7. RecoveryServiceTests expects 22 retained recordings under the keep-20 policy

**Plan + task:** 01-07 T3; policy supplied by 01-03 T3.

**Evidence:** 01-07:205 seeds 22 old terminal recordings and two newly recovered ones, then expects the newest 20 terminal recordings **plus** the two recovered recordings. RecoveryPlanner treats Recovered as terminal, and RetentionPolicy/CONTEXT D-11 keep the newest 20 recordings, protecting only still-pending IDs. The two recovered IDs are no longer pending.

**Failure:** A correct keep-20 implementation leaves 18 old terminal recordings plus two recovered ones, not 22 total; the prescribed test fails. Changing the implementation to satisfy it silently changes retention semantics.

**Concrete fix:** Assert 20 total terminal recordings including both newly recovered recordings. If a temporary recovered-recording exemption is intended, specify its lifetime and protection set explicitly in the policy/context and tests.

**Sources:** 01-03 T3 recovery/retention behaviors; 01-07:201,205; CONTEXT D-11.

### M8. Resilience/focus tests use sleeps and cross-test ordering instead of observable boundaries

**Plan + task:** 01-06 T1; 01-08 T2.

**Evidence:** Supervisor kill test waits 100 ms; App kill test kills immediately after the stop chord; focus test switches windows within 100 ms while the stop target is captured later in an asynchronous effect. It explicitly retries if paste wins. `Metrics_line_has_all_fields` then says to read the log after test 4, without a shared-fixture contract; xUnit tests do not have that implied ordering or shared per-test data directory.

**Failure:** Kill tests may exercise pre-request restart rather than a crash during inference. The focus test can switch before target capture, making a paste into B correct under the captured target while the test calls it wrong. Independently running/reordering the metrics test can fail or inspect stale evidence.

**Concrete fix:** Add harness-only acknowledgements/barriers for target-captured and request-started, correlated by recording ID. Wait for the desired boundary before killing/switching; do not mask a race with a retry. Make the metrics test arrange its own dictation or share an explicit class fixture with a defined lifecycle. Run each test individually as well as together.

**Sources:** 01-06 T1 test 1; 01-07:133 target-capture effect; 01-08 T2 tests 1,4,5.

### M9. The metrics contract omits required load timing and lacks completion timestamps for its pure reducer

**Plan + task:** 01-07 T1/T2; 01-08 T2/T3; originating 01-01/01-06 result contracts.

**Evidence:** ROADMAP SC5 and 01-08 must-haves require capture/load/inference/paste timing. The enumerated DictationMetrics fields at 01-07:173 contain no load_ms or warm_ms, and EngineTranscription does not carry them. Additionally, TriggerFired has NowMs, but CaptureStopped/TranscriptionCompleted/DeliveryCompleted carry no monotonic occurrence timestamp; DeliveryResult.PasteMs begins at delivery, not the recording-stop event. A pure reducer cannot read the clock to compute true stop_to_paste_ms from these payloads. T2's log test also says all 13 field names while the prescribed record lists 15 including reason and stop_to_paste_ms.

**Failure:** The implementation must violate purity, invent missing timing data, or report a partial/summed estimate as end-to-end latency. SC5 load timing is not covered even if the described test passes.

**Concrete fix:** Carry load/warm evidence from Ready into cached engine-session metadata and the agreed metrics schema. Stamp stop and delivery-complete events in adapters with the same monotonic clock. Compute latency from those timestamps, with clear boundaries. Use one authoritative field list/schema in the logger and all tests rather than a stale field-count assertion.

**Sources:** ROADMAP SC5; 01-07:111,173,177; 01-08 must-haves/T2 metrics test; 01-01 ReadyMessage/EngineTranscription contracts.

### M10. The final one-command verifier never runs the automated real-microphone smoke test

**Plan + task:** 01-03 T2; 01-08 T3.

**Evidence:** 01-03 creates WasapiSmokeTests to open the default device and write one second without human speech. 01-08:154 enumerates the full verifier's integration classes but omits WasapiSmokeTests. All mandatory E2E uses FileAudioSource. The evidence report describes microphone smoke as not automated and leaves an optional owner checklist.

**Failure:** The final gate can pass while WASAPI startup/callback/stop is broken; converter and file-source tests do not execute that adapter. The optional manual owner check is not itself a blocking hidden step, but it is the only remaining route to notice this normal-mode defect.

**Concrete fix:** Include the already-planned WasapiSmokeTests in verify-phase1, recording a skip only when no capture device exists. Run capture even if nobody speaks; format/duration/start/stop are automatable. Keep subjective first-word quality in the optional checklist and state that limit honestly. Ensure the test has bounded startup/stop deadlines.

**Sources:** 01-03 T2 WasapiSmokeTests; 01-08:154 and its optional human-check; REQUIREMENTS AUD-01.

### M11. Runtime capture failure has no adapter-to-host error contract

**Plan + task:** 01-03 T2; 01-07 T1/T2.

**Evidence:** IAudioSource exposes StartAsync/StopAsync and Level only. WasapiAudioSource measures initial DataAvailable and handles no-device startup, but does not specify forwarding WasapiRecorder.RecordingStopped errors/device loss after startup. EffectRunner emits CaptureFailed only from startup failure. CaptureConverter/sink exceptions can also occur on the recorder callback thread.

**Failure:** A microphone unplug/privacy/device failure mid-recording can stop producing data while the app remains Recording, or throw through a callback without a durable error outcome. The five-minute cap is not a substitute for reporting adapter failure.

**Concrete fix:** Add a per-capture asynchronous fault/completion signal to the audio port and handle RecordingStopped/error callbacks. Post an ID-correlated event to the owner loop, finalize/close the partial spool, preserve recoverable audio, and emit one error metric. Bound waiting for the first DataAvailable. Test injected mid-capture failure and a source that never produces its first callback.

**Sources:** 01-03 IAudioSource contract and T2 action; 01-07 CaptureFailed/EffectRunner; [NAudio v3.3.0 WasapiRecorder events](https://github.com/naudio/NAudio/blob/v3.3.0/src/NAudio.Wasapi/WasapiRecorder.cs).

### M12. The model-fetch invocation cannot pass its array as written; gate implementation must respect the PS5.1 runtime API

**Plan + task:** 01-01 T2; 01-08 T3 gate extension.

**Evidence:** 01-01:242 invokes `powershell -File tools/fetch-models.ps1 -Only ggml-large-v3-turbo.bin,ggml-silero-v6.2.0.bin`. Across the native powershell.exe -File boundary this does not evaluate a PowerShell array expression; the comma-containing value is a string. The script's `-Only <names>` contract does not say to split it. Separately, the referenced RESEARCH ProcessStartInfo sample includes StandardInputEncoding. Read-only reflection in this Windows PowerShell session confirms that property and ArgumentList are absent, although StandardOutputEncoding/StandardErrorEncoding exist.

**Failure:** Fixing `&&` alone leaves the fetch check unable to select the two required models, and copying the modern .NET process recipe into gate.ps1 can fail before the published engine runs.

**Concrete fix:** From a PowerShell wrapper use `& .\tools\fetch-models.ps1 -Only @('ggml-large-v3-turbo.bin','ggml-silero-v6.2.0.bin')`; or explicitly define/split a CSV parameter for native -File invocation. For the gate, use .NET Framework-compatible ProcessStartInfo members, correctly quoted Arguments, and an explicit UTF-8 writer over stdin's BaseStream when needed; do not assign nonexistent StandardInputEncoding/ArgumentList properties. Verify the gate under actual powershell.exe 5.1 with non-ASCII/space paths.

**Sources:** 01-01:230,236,242; RESEARCH Pattern 4; local ProcessStartInfo property reflection; [powershell.exe -File argument limitations](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_powershell_exe?view=powershell-5.1); [modern StandardInputEncoding API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.standardinputencoding).

## LOW

No additional independently actionable LOW defect is reported. Cosmetic documentation changes are not counted as execution blockers.

## Checks that should not be mistaken for new blockers

- **Whisper.net 1.9.1 on this Windows 10 machine is empirically proven.** SPIKE-WIN10-WHISPERNET.md remains valid: 20 completed q5/f16 CPU/Vulkan transcriptions from a self-contained directory build. Its six initially uncaptured exit codes remain disclosed. The Phase 1 gate correctly rechecks the actual worker. This review does not reopen H1 as an untested incompatibility.
- The original spike left flash attention at its default off. The additional flash-attention evidence is in 01-RESEARCH, not in that spike; final selection still needs the real worker benchmark. Preserve that distinction in SKELETON/evidence.
- Whisper.net/runtime 1.9.1, NAudio 3.3.0, CsWin32 0.3.367 and xunit.v3 4.0.2 are coherent pinned choices. xunit.v3 4.0.2 delegates to the MTP-v2 package; the global.json runner selection and class-filter family are appropriate. Do not replace these with older VSTest syntax as a supposed fix for H1. [xunit.v3 package](https://www.nuget.org/packages/xunit.v3/4.0.2), [MTP/filter documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).
- Wave-2 frontmatter does not show an obvious same-source-file ownership collision between 01-02 through 01-06. Pre-generating NativeMethods.txt in plan 01 is useful. H7 concerns shared outputs/resources and measurement validity; H2 concerns cross-project contract ownership, not an invented textual merge conflict.
- Edge TTS already exists in the authorized spike environment, and research verified the local SAPI fallback voice. The optional microphone checklist is expressly nonblocking. I did not count those as a present required-human checkpoint. The fixture README's human-install language should not be used to stop available SAPI generation or previously authorized tool use.
- UI, tray, Esc cancellation UX, cloud and installer/autostart are explicitly deferred. Their absence is not a Phase 1 coverage defect. Worker control responsiveness in M3 and automatic WASAPI smoke coverage in M10 are within the existing Phase 1 contracts.

## Recommended correction order

1. Fix shell verification and shared Core contracts before scaffolding (H1/H2/M12).
2. Resolve trigger-state and spool ownership recipes before wave 2 (H3/H5); bind verification resource isolation (H7).
3. Complete safe focus, durable delivery and audio error contracts (H4/H6/M11).
4. Reconcile reducer concurrency, startup and harness event replay before integration (H8/H9/H10).
5. Correct the remaining adapter/filter/test/metrics contracts, then run the existing published gate and measured acceptance suite without weakening assertions.
