# Pitfalls Research

**Domain:** Windows 10 push-to-talk voice dictation (global hotkey -> mic capture -> Whisper pt-BR local/cloud -> clipboard + auto Ctrl+V, waveform pill overlay, tray)
**Researched:** 2026-10-22
**Confidence:** MEDIUM-HIGH. Based on well-documented Win32 behavior (UIPI, LowLevelHooksTimeout, WS_EX_NOACTIVATE, SendInput semantics) and widely reported Whisper/faster-whisper/whisper.cpp behavior. Web verification was not run in this pass; items marked (verify) need a spike confirmation. Phase names below are suggested roadmap labels (P1..P6, see mapping at the end).

Suggested phases used for mapping:
- **P1 Core spike**: hotkey + mic capture + STT pipeline, headless-testable
- **P2 Paste and clipboard**: injection, focus, clipboard
- **P3 Overlay + tray + settings**
- **P4 Cloud engine**
- **P5 Packaging/installer + autostart**
- **P6 Hardening/soak**

## Critical Pitfalls

### Pitfall 1: Whisper hallucinates on silence / very short clips
**What goes wrong:** Accidental taps, silent recordings, or trailing silence produce phantom text, which then gets auto-pasted into the user's document. Typical pt-BR outputs: "Obrigado.", "Obrigado por assistir.", "Legendas pela comunidade Amara.org", "Legendas pela comunidade Amara.org.", "Inscreva-se no canal", "Tchau, tchau", "...", or music-note characters. These come from subtitle-heavy training data.
**Why it happens:** Whisper is a seq2seq model trained on subtitled web video and always tries to emit something; with no speech and no VAD it falls back on those priors. Short clips (<1 s) and clips with long leading/trailing silence are worst. The `initial_prompt` / previous-text conditioning can make it worse.
**How to avoid:**
1. Gate before the model: minimum duration (e.g. <0.4-0.5 s of audio -> discard) and an energy/VAD check; if Silero VAD finds no speech, skip transcription and show "nothing heard" in the overlay (never paste).
2. Use VAD filtering (faster-whisper `vad_filter=True` with Silero, `min_silence_duration_ms` ~500, `speech_pad_ms` ~200-400; whisper.cpp `--vad` with Silero model) and transcribe only the speech segments.
3. Set `condition_on_previous_text=False`, `no_speech_threshold` ~0.6, `log_prob_threshold` -1.0, `compression_ratio_threshold` 2.4, temperature fallback tuple.
4. Post-filter blacklist (case/punctuation-insensitive, whole-output match, not substring) for known phantoms: "obrigado", "obrigada", "legendas pela comunidade amara.org", "tchau", "inscreva-se", "e aí", "..." — drop only when the entire output (or the only segment) matches AND audio was short/quiet. Keep list in a config file, not code.
5. Trim leading/trailing silence from the buffer before decoding.
**Warning signs:** Pasted text appears when user did not speak; output identical across different recordings; transcription of a 0.3 s tap is non-empty; "Amara.org" in logs.
**Phase to address:** P1 (gating, VAD, thresholds, blacklist); P6 adds a regression fixture suite.

### Pitfall 2: Repetition loops / truncation with large-v3 and turbo on long or noisy audio
**What goes wrong:** Output repeats the same phrase dozens of times ("e aí, e aí, e aí..."), or drops large middle portions, or emits timestamps-only garbage. large-v3 is notably worse than v2 for repetition and hallucination; large-v3-turbo (4 decoder layers) is faster but can loop on pt-BR with disfluencies, and has slightly lower accuracy than large-v3 in some languages.
**Why it happens:** Greedy/beam decoding gets stuck when `condition_on_previous_text=True` feeds a bad segment back; 30 s window edges; low-SNR audio; no repetition penalty.
**How to avoid:**
1. `condition_on_previous_text=False`; `compression_ratio_threshold=2.4` with temperature fallback (0.0..1.0) so looped segments are retried hotter.
2. Always VAD-chunk long utterances into <=30 s speech chunks (faster-whisper does this with VAD); never feed minutes of raw audio.
3. Add `repetition_penalty` (~1.1) / `no_repeat_ngram_size` (e.g. 3-4) as an option; test impact on pt-BR.
4. Post-detect loops (repeated n-gram > N times or output tokens >> speech duration x ~6 tokens/s) and re-run with a different setting or fall back to the other model.
5. Build a small benchmark of pt-BR fixtures (clean read speech, disfluent speech, noisy, 5/30/60 s) and compare turbo vs large-v3 vs distilled/other on WER and loops before locking the default. Keep the model name a config value.
6. Fix `language="pt"` (do NOT autodetect; short clips misdetect as Spanish/Galician) and consider an `initial_prompt` with domain vocabulary only if it measurably helps (it can also trigger prompt echoing).
**Warning signs:** Output length wildly exceeding speech duration; repeated sentence endings; latency spike for one clip (decoder running to max tokens).
**Phase to address:** P1 (decoding config + benchmark); P6 (loop detector regression tests).

### Pitfall 3: Low-level keyboard hook dropped or laggy (LowLevelHooksTimeout)
**What goes wrong:** If a `WH_KEYBOARD_LL` callback does not return within the timeout (default ~300 ms on Win10; registry `LowLevelHooksTimeout`), Windows silently skips it, and on newer Windows builds can unhook it permanently without notice. Result: hotkey works for hours, then stops until restart. Heavy work in the hook (logging, GC pause, Python GIL contention, STT model running in the same process/thread) causes it. Also, hooks stop receiving events when sleeping/locking/RDP session changes occur.
**Why it happens:** Doing real work (audio start, UI, file I/O) inside the callback; hook thread without its own message loop; Python GIL held by a long inference call in the same process.
**How to avoid:**
1. Hook callback only: identify key, push to a lock-free queue/`PostMessage`, return immediately (<1 ms). All logic in another thread.
2. Run the hook on a dedicated thread with its own `GetMessage` loop, at elevated thread priority; in Python, keep inference in a separate process (or release the GIL via native code) so the hook thread is never starved.
3. Prefer `RegisterHotKey` for the plain "toggle" use (no hook needed), but it cannot see key-up for push-to-talk. For hold-to-talk use either the LL hook, or RegisterHotKey for key-down plus polling `GetAsyncKeyState` for release (timer ~15 ms) — cheaper and not subject to hook timeout. Decide in P1 and document why.
4. Watchdog: periodically (every ~30-60 s and on `WM_WTSSESSION_CHANGE` / `WM_POWERBROADCAST` resume) verify the hook is alive (e.g. synthetic/own heartbeat via a self-sent benign event, or just reinstall the hook), and re-register on resume/unlock.
5. Log hook callback duration p99 to the log file.
**Warning signs:** Hotkey dead after sleep/unlock/long inference; works in dev but not after the model loads; callback time >50 ms in logs.
**Phase to address:** P1 (architecture: hook isolated from inference); P6 (soak test with sleep/resume and 8 h run).

### Pitfall 4: Stuck modifier keys after synthetic Ctrl+V when the hotkey contains Ctrl/Shift
**What goes wrong:** The user releases (or still holds) Ctrl+Shift+Space while the app injects Ctrl+V. The target app sees Ctrl+Shift+V (paste-as-plain-text / different action), or sees Shift still down -> pastes with unexpected behavior, or Ctrl is left logically stuck (after injection the system thinks Ctrl is up while the user still holds it, or vice versa), causing weird "Ctrl held" behavior until the user taps the key again. Also the hook may see its own injected events and re-trigger the hotkey (feedback loop).
**Why it happens:** `SendInput` merges with the real keyboard state; paste is issued on key-up of Space while Ctrl/Shift are physically still down; toggle mode paste happens before the user lifted modifiers.
**How to avoid:**
1. Before injecting, wait until all hotkey modifiers (Ctrl, Shift, Alt, Win) are physically released (poll `GetAsyncKeyState`, timeout ~500-800 ms, then proceed anyway); or inject key-up events for held modifiers first and restore their down-state afterward if still physically held.
2. Inject Ctrl+V as one atomic `SendInput` batch (4 events), and mark with `dwExtraInfo` magic value; ignore events with that marker in the LL hook.
3. Never paste from within the hook callback; paste on the worker thread after transcription (this naturally gives the user time to release keys, but still verify).
4. Consider a default hotkey without Ctrl+Shift (e.g. a single dedicated key such as Right Alt-less combos, F-keys, or Ctrl+Win) — Ctrl+Shift+Space is the owner's stated default so keep it, but make the modifier-release logic a P2 requirement and test it.
5. Also send a neutral key-up for Win/Alt masks if Start menu / menu bar activation could be triggered (tap of Alt or Win alone activates menus; use the "dummy key 0xFF" trick when swallowing).
**Warning signs:** Pasted text appears as plain-text paste in some apps; Shift stuck while typing next (UPPERCASE text); Ctrl+Space toggles IME afterwards.
**Phase to address:** P2 (injection design); automated test with a scripted target window that records key events.

### Pitfall 5: Ctrl+Shift+Space conflicts (IME, apps, system)
**What goes wrong:** Ctrl+Shift cycles input languages/keyboard layouts (Windows "Alt+Shift" or "Ctrl+Shift" layout switch setting - common for pt-BR/US-International users); Ctrl+Space toggles IME on/off for CJK IMEs; Ctrl+Shift+Space is bound in VS Code (parameter hints), Office, browsers, Slack, JetBrains, Figma. If only passively observed, the foreground app also receives the combo and executes its own action; if swallowed, the app loses its shortcut.
**Why it happens:** Global hotkeys are not exclusive unless `RegisterHotKey` succeeds or the hook returns 1 (swallows). Pressing Ctrl then Shift first triggers layout switch on key-up.
**How to avoid:**
1. Swallow the trigger key (Space) down/up in the hook; for pure `RegisterHotKey`, handle failure `ERROR_HOTKEY_ALREADY_REGISTERED` with a visible error in settings.
2. Detect and warn if the Windows "Switch input language" hotkey is Ctrl+Shift (registry `HKCU\Keyboard Layout\Toggle`); suggest disabling or changing the app hotkey.
3. Hotkey capture UI in settings with conflict detection, plus easy rebind; ship a sensible fallback list (e.g. Ctrl+Alt+Space, F9, Pause, a mouse side button).
4. Suppress the Ctrl/Shift up-events' menu/layout-switch side effects with the dummy-key trick where applicable.
5. Be careful with hold-to-talk: key auto-repeat on held Space generates repeated keydown events; debounce and track a state machine (idle/arming/recording/locked) rather than reacting to every event. Tap-vs-hold threshold (~250-300 ms) must be configurable.
**Warning signs:** Space characters typed into the document when starting recording; keyboard layout flips; IDE popups when pressing hotkey.
**Phase to address:** P1 (state machine + swallow), P3 (hotkey capture UI + conflict warnings).

### Pitfall 6: Pasting into elevated (admin) windows fails silently (UIPI)
**What goes wrong:** A non-elevated process cannot `SendInput` to a window running as admin (Task Manager, admin terminals, installers, some games/anti-cheat apps); the injection is silently dropped, the hotkey hook also does not see keystrokes while an elevated window is focused. User thinks the app is broken.
**Why it happens:** UIPI (User Interface Privilege Isolation) blocks lower-integrity processes from sending input/hooking higher-integrity windows.
**How to avoid:**
1. Detect: compare the foreground window's process elevation (`OpenProcess` + `GetTokenInformation(TokenElevation)`) with ours; if elevated and we are not, do not attempt paste: leave text on clipboard and show "Copiado - cole manualmente (janela de administrador)" in the overlay.
2. Option: run Somning elevated (needs UAC prompt/scheduled task for autostart) or `uiAccess=true` manifest with a signed binary in a secure location — high cost; only for v2 if the owner needs it. Document as a known limitation for v1.
3. Hook-based hotkey will not fire over elevated windows; `RegisterHotKey` does work across integrity levels - another argument to prefer it for activation.
**Warning signs:** Hotkey works everywhere except Task Manager/admin PowerShell; overlay appears, text not pasted, no error.
**Phase to address:** P2 (detect + degrade gracefully), P5 (document elevation story).

### Pitfall 7: Terminals use Ctrl+Shift+V (or different paste), and some apps ignore synthetic Ctrl+V
**What goes wrong:** Windows Terminal, WSL terminals, Git Bash/mintty (Shift+Insert), Alacritty, VS Code integrated terminal, PuTTY, and some remote desktop/VM windows paste differently. Ctrl+V sends a literal ^V (0x16) to a shell. Some games/RDP/Citrix clients ignore SendInput. Multi-line text pasted into a shell may execute commands (bracketed-paste dependent) — a safety risk.
**Why it happens:** Terminal emulators reserve Ctrl+V for the shell; apps differ in paste shortcuts.
**How to avoid:**
1. Per-process paste-method table keyed by foreground exe/class (`WindowsTerminal.exe`, `ConsoleWindowClass`, `mintty`, `Code.exe` + terminal heuristics unreliable): default Ctrl+V; terminals -> Ctrl+Shift+V or Shift+Insert; user-editable in a config file. Shift+Insert works in most terminals and classic Win32 edit controls — consider as the default fallback.
2. Provide "paste mode" setting: Auto / Ctrl+V / Ctrl+Shift+V / Shift+Insert / Copy only.
3. For terminals, strip trailing newline from transcription (never let Whisper output end with "\n") so nothing executes; consider disabling auto-paste into terminals by default.
4. Alternative for robust text injection: `SendInput` with `KEYEVENTF_UNICODE` typed characters (bypasses clipboard, works in terminals but is slow for long text, fails in some apps, and breaks with IME) — keep as optional fallback, not default.
5. Cache the foreground HWND at hotkey-down (not at paste time) and verify it is still the foreground when pasting; if the user switched windows, still paste into the current foreground? Decide explicitly (recommended: restore/target original window only if it still exists; otherwise copy-only + notice).
**Warning signs:** "^V" appears in terminal; text pasted twice; paste works in Notepad but not VS Code terminal / Windows Terminal.
**Phase to address:** P2.

### Pitfall 8: Overlay steals focus or intercepts clicks, so paste goes to the wrong window
**What goes wrong:** Showing the pill activates it, the target app loses focus (caret disappears, selection collapses, Ctrl+V goes to the overlay or nowhere); in browsers the page may blur input and close dropdowns/autocomplete; full-screen apps minimize.
**Why it happens:** Windows default to activating on show; WPF/WinForms/Qt `Show()` activates; click-through not set.
**How to avoid:**
1. Extended styles: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED`; show with `SW_SHOWNOACTIVATE` / `SetWindowPos(SWP_NOACTIVATE)`. In Qt: `Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput | Qt.Tool | Qt.WindowStaysOnTopHint`. In WPF: `ShowActivated=false` plus WS_EX flags.
2. Make overlay not appear in Alt+Tab and taskbar (`WS_EX_TOOLWINDOW`).
3. Automated check: script that focuses a test window with a caret, triggers the overlay, asserts `GetForegroundWindow()` unchanged and that the test window still receives the paste.
4. Topmost over full-screen exclusive games/video won't work (accept; document).
**Warning signs:** Caret vanishes when overlay appears; paste lands nowhere only when overlay is shown; Alt+Tab shows "Somning".
**Phase to address:** P3 (with automated focus test), early spike in P1/P2 because paste correctness depends on it.

### Pitfall 9: Clipboard races, clipboard managers, and failing to restore the user's previous clipboard
**What goes wrong:** (a) `OpenClipboard` fails (`ERROR_ACCESS_DENIED`) because another process (clipboard manager, Windows Clipboard History/Cloud sync, RDP, Office) holds it - the paste silently pastes the OLD clipboard contents. (b) Ctrl+V is sent before the target app reads the new clipboard (apps read lazily, delayed rendering). (c) Restoring the previous clipboard too early (before the target app pasted) pastes the old content; too late loses the "keep text in clipboard" requirement. (d) Clipboard managers (Ditto, Windows Win+V history, Clipboard sync) record the dictated text - privacy leak for sensitive speech; use `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0`, `CanUploadToCloudClipboard=0` formats. (e) Only text restored: previous images/files/rich formats are lost.
**Why it happens:** Clipboard is a global shared resource with no transaction semantics; apps paste asynchronously.
**How to avoid:**
1. Retry `OpenClipboard` with backoff (e.g. 10 attempts x 20-50 ms); verify by reading back (`GetClipboardSequenceNumber` changes, text equals expected) before sending Ctrl+V.
2. Delay after Ctrl+V: for the PRD ("text stays in clipboard") the simple correct design is NOT to restore previous contents at all (explicit requirement: text remains in clipboard). If a "restore previous" setting is added later, enumerate and snapshot all formats (`EnumClipboardFormats`) and restore only after ~300-500 ms and only if `GetClipboardSequenceNumber` shows nobody else changed it.
3. Set the clipboard-history opt-out formats if the owner prefers privacy (optional, setting).
4. Write both `CF_UNICODETEXT` (never rely on ANSI) with correct UTF-16 and a terminating NUL; test accents (ç, ã, õ, é) end to end; avoid `pyperclip`-style shelling out to `clip.exe` (code page mangling).
5. Keep the audio -> text result persisted to a local log/last-result file BEFORE paste so a failed paste never loses the transcript (requirement: never silently lose a recording).
**Warning signs:** Occasional pasting of the previous clipboard; accent characters turned into "?" or mojibake; paste delay inconsistent; works without Win+V history, fails with it.
**Phase to address:** P2.

### Pitfall 10: Overlay transparency, DPI scaling, and multi-monitor placement
**What goes wrong:** Pill renders with black/ugly background or no per-pixel alpha (WinForms `TransparencyKey` jaggies, WPF `AllowsTransparency` software rendering slowness); blurry on 125/150% scaling; wrong position on secondary monitor with different DPI or when taskbar is on a different edge; appears on the wrong monitor (primary vs. the monitor where the focused window is); pill overlaps taskbar; waveform animation janks (30 fps timer on UI thread that is also blocked by transcription).
**Why it happens:** Mixed DPI-awareness (process not Per-Monitor-V2 aware), using virtual-screen coordinates in device pixels vs DIPs, using primary screen bounds not work area, animation on the same thread as heavy work.
**How to avoid:**
1. Declare Per-Monitor V2 DPI awareness in the manifest (or framework equivalent) and compute position from the monitor containing the foreground window (`MonitorFromWindow(GetForegroundWindow())`, `GetMonitorInfo` work area `rcWork`, `GetDpiForMonitor`), bottom-center with a margin above the taskbar.
2. Use layered window with per-pixel alpha (`UpdateLayeredWindow`) or a framework that supports it (Qt frameless + `WA_TranslucentBackground`, WinUI, WPF `AllowsTransparency=True` with small window size).
3. Re-position on each show (monitor layout/DPI may have changed); handle `WM_DPICHANGED`, `WM_DISPLAYCHANGE`.
4. Waveform rendering in its own thread/animation clock; audio-level meter from the capture callback via a smoothed RMS/peak; cap at 30-60 fps; show a "transcribing" spinner animation driven by a timer independent of inference (inference must be out-of-process or threadpool).
5. Test matrix: 100/125/150/200% DPI, two monitors with different DPI, taskbar at bottom/top/auto-hide, dark and light wallpapers. Automated screenshot comparison where possible (`PrintWindow`/`mss`).
**Warning signs:** Blurry pill; pill cut off or on wrong monitor; flicker when animating; high CPU for an idle overlay.
**Phase to address:** P3.

### Pitfall 11: Microphone device changes, exclusive mode, and Windows privacy settings
**What goes wrong:** Default device changes (Bluetooth headset connects, USB mic unplugged) mid-session, the stream dies or silently records silence (-> Pitfall 1 hallucination); the device reports a 44.1/48 kHz shared-mode format, and recording at 16 kHz fails or gets aliased (bad naive resampling); another app holds the mic in exclusive mode (error 0x88890004/"device in use"); Windows Settings -> Privacy -> Microphone disabled for desktop apps yields all-zero audio with no error; Bluetooth headset switches to the low-quality HFP profile (8/16 kHz) when mic opens, ruining music playback and quality; hotkey press has a startup latency (opening the stream takes 100-500 ms) so the first words are clipped.
**Why it happens:** Device streams are bound to endpoint IDs; privacy blocks return silence not errors; shared-mode mix format is fixed by the driver.
**How to avoid:**
1. Capture at the device's native rate and resample with a quality resampler (soxr/`scipy.signal.resample_poly`/`libsamplerate`) to 16 kHz mono float32; do NOT trust a hard-coded 16 kHz open (WASAPI shared mode often rejects it unless `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|SRC_DEFAULT_QUALITY`).
2. Keep the input stream warm or open it immediately on hotkey-down and keep a ~300-500 ms pre-roll ring buffer so the first syllable is not lost; tradeoff: a permanently open mic shows the Windows "mic in use" indicator, so prefer open-on-demand with fast start, and test start latency.
3. Re-enumerate on device change (`IMMNotificationClient` / `sounddevice` re-query) and fall back to the default; store device by name/ID with fallback if missing.
4. Detect dead input: after N ms of recording RMS == 0 exactly (digital silence) -> show "Microfone sem sinal - verifique privacidade/dispositivo" and open `ms-settings:privacy-microphone`.
5. Handle exclusive-mode busy and no-device with explicit overlay errors (requirement). Allow selecting the mic in settings; optionally normalize gain (peak-normalize before STT; low-volume mics hurt WER).
6. Also guard against clipping/DC offset; do not apply aggressive noise suppression before Whisper (it can hurt).
**Warning signs:** Pure silence recordings with no exception; first word missing; pitch-shifted audio (wrong sample rate assumption); crashes on headset connect.
**Phase to address:** P1 (capture + resample + pre-roll), P3 (device selection UI), P6 (hot-plug soak).

### Pitfall 12: First-run model download size and failure handling
**What goes wrong:** large-v3-turbo is ~1.6 GB (fp16 / CT2 float16; ~800 MB-1 GB int8 variants), large-v3 ~3 GB; the first use hangs the app with no feedback; partial/corrupt downloads are treated as valid; HuggingFace rate limits/proxy/AV break downloads; antivirus scans the huge file slowly; disk space insufficient; cache location under a roaming/OneDrive-synced folder; model dir contains non-ASCII characters in the Windows username (`C:\Users\João\...`) which breaks C++ loaders (whisper.cpp/CTranslate2 path handling on some builds (verify)); symlink warnings from huggingface_hub on Windows without Developer Mode.
**Why it happens:** Treating model download as an implementation detail of the library on first transcribe.
**How to avoid:**
1. Dedicated first-run step with progress UI (tray/overlay + settings), resumable (HTTP Range) download, checksum verification (SHA256) before marking as ready, atomic rename from `.part`.
2. Store in `%LOCALAPPDATA%\Somning\models` (not roaming/OneDrive); check free disk space first; set `HF_HUB_DISABLE_SYMLINKS_WARNING=1`/`local_dir_use_symlinks=False` or download via plain HTTPS to a known path.
3. Offer to bundle the model with the installer (large installer) vs. download on first run; for a single-user tool consider shipping the model file next to the installer or doing the download in the installer step. Recommended: download on first run with retry/resume, plus "use local file" option.
4. If model is missing, hotkey press shows a clear overlay error ("Modelo não baixado") and optionally falls back to cloud engine if a key exists; never block the hotkey thread.
5. Pre-test with non-ASCII user paths (create a test user dir) in P5.
**Warning signs:** App "frozen" on first hotkey; model dir with tiny/zero-byte files; HF 401/429 errors in logs; load crashes with garbled path.
**Phase to address:** P1 (model manager, checksum, error states), P5 (installer/first-run verification).

### Pitfall 13: Cold model load latency (and memory) blows the "few seconds" budget
**What goes wrong:** Loading large-v3-turbo on a Ryzen 5600X (CPU int8) takes several seconds (disk read + weights init; ~5-15 s cold, and the first inference is slower due to lazy init/kernels (verify)); if loaded lazily on the first hotkey, the first dictation of the day feels broken. If kept loaded, it holds 1-3 GB RAM permanently (acceptable on 16 GB). Vulkan (whisper.cpp) additionally compiles/caches shaders on first run (could add many seconds).
**Why it happens:** Lazy loading, loading in the hook/UI thread, and no warm-up inference.
**How to avoid:**
1. Preload the model in a background thread/process at app start (and keep resident); run a warm-up transcription on 1 s of silent/synthetic audio (bypassing hallucination gate) so kernels/allocations are initialized.
2. Overlay shows "Carregando modelo..." state; if the user dictates before ready, queue the audio and transcribe when ready (never drop it).
3. Run inference in a separate worker process (also protects the hook thread, tray stays responsive, and a crash in native code does not kill the app); restart worker automatically on failure.
4. Measure and record budgets in automated tests: model load time, RTF (real-time factor) for 5/15/30 s utterances on this exact CPU; choose `compute_type=int8` (or int8_float16 n/a on CPU), `cpu_threads=6` (physical cores, not 12 threads - oversubscription hurts), `num_workers=1`.
5. Optional idle unload with timer (default: never unload).
**Warning signs:** First dictation after login takes >10 s; UI hangs while loading; RAM spike at launch.
**Phase to address:** P1 (benchmark and architecture), P6 (startup timing regression test).

### Pitfall 14: AMD GPU / Vulkan backend instability (RX 7600, no CUDA)
**What goes wrong:** whisper.cpp Vulkan builds can be slower than expected or produce crashes/garbled output/device-lost on some AMD driver versions (RDNA3 + Adrenalin updates), fall back silently to CPU, fail when the GPU is busy with games, or select the wrong adapter (integrated vs discrete); ggml Vulkan quantized kernels differ in speed; first-run shader compile delay; large-v3-turbo on a 7600 may be only modestly faster than 6-core CPU int8 once load/transfer are counted. CTranslate2 (faster-whisper) has NO AMD/Vulkan support on Windows (CUDA only; ROCm Windows not supported for CT2) - so "faster-whisper + AMD GPU" is a dead end. DirectML/ONNX Whisper paths exist but are less mature (verify).
**Why it happens:** Immature Vulkan compute path, driver variance, assuming GPU is always faster.
**How to avoid:**
1. Make the GPU a benchmarked option, not an assumption: benchmark CPU int8 (faster-whisper) vs whisper.cpp Vulkan on the RX 7600 in a P1 spike (load time, RTF, WER on fixtures, stability over 100 runs). Default to whatever wins reliably; CPU is the safe default.
2. Abstract the engine behind an interface (`transcribe(audio) -> text`) so the backend (CT2 CPU / whisper.cpp Vulkan / cloud) is swappable; auto-fallback to CPU if GPU init fails or a transcription exceeds a timeout.
3. Pin and test a known-good driver version; log adapter name and Vulkan device-lost errors; run inference in a separate process so a driver crash does not kill the app.
4. Do not use GPU concurrently with the owner's games/rendering without fallback; set the Vulkan device index explicitly.
**Warning signs:** Different output between CPU and GPU for the same audio; occasional empty outputs or process crashes after driver updates; GPU time not better than CPU.
**Phase to address:** P1 (spike and decision); P6 (soak test).

### Pitfall 15: PyInstaller packaging: AV false positives, onedir size, missing DLLs
**What goes wrong:** (If Python is chosen) PyInstaller `--onefile` exes are frequently flagged by Windows Defender/SmartScreen and others (the bootloader is a known false-positive signature; "Win32/Wacatac", "Trojan:Script"); keyboard hook + clipboard + SendInput + autostart registry key look like a keylogger/stealer to heuristics, making it worse. `--onefile` extracts to `%TEMP%` on every launch (slow start of several seconds with a big CTranslate2/torch-free bundle, antivirus scanning every launch) and breaks when TEMP is cleaned. onedir with CT2 + numpy + onnxruntime + PySide/Qt can be 300 MB-1 GB+; `torch` accidentally bundled adds ~2 GB. Hidden imports/DLLs (CTranslate2, `av`, `onnxruntime`, VC++ runtime, Qt plugins like `platforms/qwindows.dll`) missing only in the frozen build; app works from the dev venv but not frozen; SmartScreen warns on unsigned installer.
**Why it happens:** Unsigned binaries with hooks, onefile extraction, bloated dependency graph.
**How to avoid:**
1. Use onedir (not onefile) wrapped in an installer (Inno Setup/NSIS) or run from a `uv`-managed venv launcher; keep the bundle lean (avoid torch; faster-whisper does not require it; use Silero VAD via ONNX through faster-whisper's bundled model; exclude unused Qt modules).
2. Reproducible build script with a frozen-build smoke test (launch built exe headlessly, transcribe a fixture wav, exit code check) in CI/agent loop - the dev-venv-only test is NOT sufficient.
3. Reduce AV heuristics: build PyInstaller bootloader from source (or use Nuitka / a .NET/Rust native host), add version info/manifest, sign with a code-signing cert if available (the owner is the only user - at minimum add a Defender exclusion step documented in the install notes, and submit the file to Microsoft for false-positive analysis).
4. Consider an architecture where the shell (hotkey/overlay/tray/paste) is a native .NET 10 or Rust app (small, fewer AV false positives, trivial DPI/overlay handling) and STT is a bundled sidecar (whisper.cpp binary or Python worker). Decide in stack research; this pitfall is a strong argument for a native shell.
5. Test install/uninstall on a clean user account/VM, including path with spaces and accents; verify autostart entry (`HKCU\...\Run`) and that it launches without a console window and with working directory set to the app dir.
**Warning signs:** Defender quarantines the exe after build; first launch takes >5 s extraction; "Failed to load Python DLL"; bundle > 1 GB.
**Phase to address:** P5 (packaging) with an early "frozen smoke build" in P1/P2 so packaging problems surface before the end.

### Pitfall 16: Never losing a recording; crash/cancel paths lose audio or transcript
**What goes wrong:** An error in transcription, cloud timeout, or a crash discards the audio; user spoke 60 seconds and gets nothing (explicit requirement: never silently lose a recording). Also: a stuck "recording" state when the key-up event is missed (hook dropped, focus change, lock screen) so the mic stays open indefinitely.
**Why it happens:** Audio kept only in memory; no state-machine timeouts; no persistence.
**How to avoid:**
1. Write each recording to `%LOCALAPPDATA%\Somning\recordings\last.wav` (and rolling last N) before transcribing; delete older ones by retention policy (privacy trade-off: setting, default keep last 5, secure local only).
2. State machine with hard max recording length (e.g. 5 min, configurable), timeouts on transcription, and an Esc/hotkey-again cancel path; on missed key-up (polling `GetAsyncKeyState` shows key up) force stop.
3. "Retry last" tray action and tray "Copy last transcript".
4. Single-instance lock (named mutex) so two instances do not both hook the hotkey.
**Warning signs:** Overlay stays on "listening"; mic indicator stays lit; duplicate pastes (two instances).
**Phase to address:** P1 (state machine + persistence), P2/P3 (tray actions).

## Moderate Pitfalls

### Pitfall 17: Cloud engine mistakes (OpenAI gpt-4o-transcribe / Groq whisper-large-v3-turbo)
**What goes wrong:** 25 MB upload limit exceeded by long WAV (16 kHz mono 16-bit = ~1.9 MB/min so ~13 min; but 48 kHz stereo WAV is 11 MB/min); not setting `language=pt`; API key stored in plaintext in JSON settings; no timeout/retry/backoff -> hangs; Groq/OpenAI rate limits (429); cloud models also hallucinate "Obrigado" on silence (VAD gate must apply before the upload too); `prompt` parameter echo; sending audio to cloud silently when local fails (privacy surprise); gpt-4o-transcribe has no `verbose_json` segment/log-prob filters like Whisper (use the `whisper-1`/Groq endpoints for `no_speech_prob` if needed) (verify).
**Prevention:** Same VAD/duration gate before upload; encode to FLAC/Opus (smaller) or 16 kHz WAV; explicit `language="pt"`; timeouts (connect 5 s, total ~30-60 s) + 1-2 retries with jittered backoff on 429/5xx; store the API key in Windows Credential Manager (`keyring`/DPAPI) not plain JSON; never fall back to cloud automatically without explicit setting; redact keys in logs; show clear overlay error on 401/429/network; keep the audio on failure for retry.
**Phase:** P4.

### Pitfall 18: Missing text normalization / pt-BR output quality issues
**What goes wrong:** Whisper output has no spoken-punctuation handling ("vírgula", "ponto final" appear literally), numbers/ordinals inconsistent, leading space, trailing newline, European vs Brazilian vocabulary, capitalization after paste into a mid-sentence position, proper nouns/jargon (e.g. "Claude Code", English tech terms in pt-BR speech) badly transcribed; code-switching English words mangled.
**Prevention:** Minimal post-processor: strip, collapse whitespace, no trailing newline; optional `initial_prompt` / custom vocabulary (benchmark; may cause hallucination); optional spoken-punctuation mapping as a setting; keep LLM rewriting out of v1 (as planned); lowercase first letter option when pasting mid-sentence is v2. Add fixtures with pt-BR accents and tech words.
**Phase:** P1 (post-processor), v2 for rewriting.

### Pitfall 19: Tray icon and app lifecycle problems
**What goes wrong:** Tray icon disappears after Explorer restart (taskbar recreated) or when shell isn't ready at login autostart; icon hidden in overflow area so users cannot find it; "Quit" leaves worker process/mic stream alive (zombie `python.exe`); console window flashes; settings window opens behind other windows or steals focus mid-dictation; autostart races with audio service/network at login; second instance launches.
**Prevention:** Handle `TaskbarCreated` message to re-add the icon; delayed start (10-20 s) at login or retry; on quit, kill the child worker (job object with `KILL_ON_JOB_CLOSE`), release the mic and unhook; single-instance mutex; use pythonw/`--noconsole`/windowed subsystem; log rotation in `%LOCALAPPDATA%\Somning\logs` with "Open logs" menu.
**Phase:** P3 (tray), P5 (autostart), P6 (soak).

### Pitfall 20: Sleep/lock/fast-user-switch/RDP break hooks and audio
**What goes wrong:** After resume from sleep or unlock, hooks stop firing (see Pitfall 3), audio endpoints change, stream handles invalid; on RDP sessions the audio device differs/redirected.
**Prevention:** Subscribe to `WM_POWERBROADCAST` / `WM_WTSSESSION_CHANGE`, re-init hook + audio on resume/unlock; reopen streams on error with retry; soak test with real sleep/wake cycle where automatable.
**Phase:** P6.

### Pitfall 21: Autonomous (no-human) verification gaps
**What goes wrong:** Because development is 100% agentic, features that need a real foreground app, real mic, or real GUI never get tested; "works in tests" but fails in daily use (focus, UIPI, DPI, clipboard). Synthetic tests exercise unit logic but not Win32 integration.
**Prevention:** Build a Win32 test harness: a scripted target window (Notepad/WinForms test app with a text box and key-event logger), SendInput-driven hotkey press/hold (the hook should treat injected events by design - add a test-only flag since LL hooks can see injected events with the LLKHF_INJECTED flag), audio fixtures piped into a virtual/loopback input or a `--input-file` test mode that bypasses the mic, golden pt-BR WAV fixtures with expected WER thresholds, silence/noise/short-tap fixtures asserting nothing is pasted, screenshot checks of overlay, a frozen-build smoke test. Keep a "real-hardware checklist" for the few things only the owner can confirm and keep it small.
**Phase:** P1 sets up the harness; each phase adds cases.

## Minor Pitfalls

### Pitfall 22: Timing: UI feedback before audio is actually recording
**What goes wrong:** Overlay shows "listening" while the stream has not started (first words clipped), or overlay hides before paste completes.
**Prevention:** Show listening state only after the first audio callback; pre-roll buffer; hide the overlay after paste is issued (and 150 ms), with "done" tick.
**Phase:** P3.

### Pitfall 23: Sound feedback and Windows ducking
**What goes wrong:** Start/stop beeps are recorded by the mic or Windows "communications activity" auto-lowers other apps' volume when mic opens (Sound settings -> Communications -> "Reduce volume by 80%").
**Prevention:** Optional subtle sounds played before stream start / after stop; note the communications ducking setting in docs; open the stream as a normal (non-communications) category.
**Phase:** P3.

### Pitfall 24: Logging sensitive content
**What goes wrong:** Transcripts (potentially passwords, personal content) written to log files and retained recordings.
**Prevention:** Log metadata (duration, RTF, engine, lengths) by default; transcripts only in debug mode; delete audio per retention; document where files live.
**Phase:** P1/P6.

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Inference in the same process/thread as hook/UI | Simple code | Dropped hooks, frozen overlay, crash takes everything | Never |
| `--onefile` PyInstaller | One exe | AV false positives, slow start, temp extraction | Only for throwaway dev builds |
| Autodetect language | Fewer settings | Misdetect on short clips (es/gl), slower | Never in v1 (pt fixed) |
| `pyperclip`/`clip.exe` for clipboard | Quick | Encoding bugs, no retry, no format control | Never; use Win32 API |
| Sleep-based fixed delays for paste | Works on dev machine | Flaky on other apps/load | Only with verification of clipboard sequence number plus configurable delay |
| Hard-coded model path/name | Fast start | Breaks with non-ASCII users, model swaps | Never; config value |
| Plain-text API key in settings.json | Easy | Key leak in backups/logs | Never; use Credential Manager/DPAPI |
| Skipping VAD for simplicity | Less deps | Hallucinated pastes (Pitfall 1) | Never |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| faster-whisper / CTranslate2 | Using `device="auto"` expecting AMD GPU; torch dependency; `cpu_threads` default | CPU int8, `cpu_threads`=physical cores; no GPU on AMD/Windows; `vad_filter=True`, `condition_on_previous_text=False` |
| whisper.cpp Vulkan | Assuming faster than CPU; not pinning adapter | Benchmark on RX 7600; explicit device; fallback to CPU; run as sidecar process |
| WASAPI/sounddevice/PortAudio | Opening 16 kHz directly; blocking in callback | Native rate + resample; callback only enqueues; pre-roll ring buffer |
| Windows clipboard | Single attempt, ANSI text, immediate restore | Retry/backoff, `CF_UNICODETEXT`, verify sequence number |
| SendInput | Interleaved with physical modifiers; not marking own events | Wait for modifier release; atomic batch; `dwExtraInfo` marker |
| Hugging Face Hub downloads | Relying on implicit cache/symlinks | Explicit resumable download + checksum into `%LOCALAPPDATA%` |
| OpenAI/Groq APIs | No language, no timeout, big WAV | `language=pt`, timeouts/retries, compressed audio, VAD gate |
| Windows Startup | Registry Run entry racing shell | Delayed start, single instance, handle `TaskbarCreated` |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Cold model load on first hotkey | 5-15 s first dictation | Preload + warm-up at start | Every login |
| Thread oversubscription (12 threads on 6 cores while UI animates) | Choppy waveform, slow RTF | `cpu_threads`=6, inference in a separate process | During transcription |
| Long utterances (>60 s) in one decode | Loops, memory, latency | VAD chunking <=30 s, max recording length | >1-2 min recordings |
| Waveform draw on UI thread with software transparency | High CPU idle/recording | Layered window, 30 fps cap, small bitmap | Any time on 4K/150% |
| Unbounded recording buffer | RAM growth if stuck recording | Hard max duration, ring buffer | Missed key-up |
| Antivirus scanning model/bundle each launch | Slow start | Install in `%LOCALAPPDATA%`, onedir, exclusion note | Each launch with strict AV |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Global keyboard hook logging keys beyond the hotkey | Keylogger behavior, AV flag, privacy | Only inspect the configured keys; never persist others; mention in docs |
| API key in plaintext/logs | Credential leak | Credential Manager/DPAPI; redact |
| Retained audio/transcripts without bounds | Privacy exposure | Retention limit, setting to disable, local only |
| Auto-pasting multi-line text into terminals | Accidental command execution | Strip newlines for terminals, disable auto-paste there by default |
| Auto-fallback to cloud without consent | Audio leaves machine unexpectedly | Cloud only when explicitly selected |
| Downloading model/update without integrity check | Tampered binary | Pin URL + SHA256 |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| No feedback when nothing was heard | User thinks app broken | Overlay "Nada detectado" briefly |
| Tap-vs-hold threshold too tight/loose | Accidental toggles or PTT stuck | Configurable ~250-300 ms; visible state |
| Silent failure on elevated windows/terminals | Text vanishes | Overlay message + text stays in clipboard |
| Overlay obscures content | Annoyance | Small pill, click-through, fades quickly |
| No cancel | Bad recordings pasted | Esc or re-tap discards (configurable) |
| Error text only in logs | Opaque failures | Short pt-BR overlay messages + tray "Open logs" |

## "Looks Done But Isn't" Checklist

- [ ] **Silence handling:** often missing the no-speech gate - verify a 3 s silent recording and a 0.3 s tap paste nothing and show "Nada detectado"
- [ ] **Hallucination list:** verify pt fixtures with silence + low noise never output "Obrigado."/"Amara.org"
- [ ] **Hotkey robustness:** verify hotkey works after sleep/resume, after 8 h, over high CPU load, and while model is transcribing
- [ ] **Stuck modifiers:** verify with hotkey held during paste that no Ctrl/Shift stays latched (test by typing afterward)
- [ ] **Focus:** verify overlay shows with `GetForegroundWindow()` unchanged and caret retained
- [ ] **Paste matrix:** Notepad, Chrome/Edge text field, VS Code editor, VS Code terminal, Windows Terminal, Word, Slack/Discord, elevated Notepad (expect graceful message)
- [ ] **Accents:** "ação, coração, órgão, você, é, à" survive end to end into clipboard and target
- [ ] **Clipboard:** works with Win+V history on and a clipboard manager running; text remains in clipboard
- [ ] **DPI/multi-monitor:** pill correct at 100/125/150% and on secondary monitor with different DPI
- [ ] **Mic:** unplug/replace device, privacy-denied (all zeros), exclusive-mode busy, Bluetooth headset
- [ ] **Model:** first-run download interrupted and resumed; corrupt file detected; non-ASCII username path
- [ ] **Frozen build:** the packaged exe (not the dev venv) transcribes a fixture; clean-machine install; Defender does not quarantine
- [ ] **Cold start:** time from login to "ready" and from first hotkey to paste within budget
- [ ] **Quit:** no orphaned worker processes or lit mic indicator after Quit
- [ ] **Cloud:** wrong key, offline, 429, long recording >25 MB all produce clear errors and keep the audio

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Hook dead | LOW | Watchdog reinstall hook; tray "Restart listener" |
| Hallucinated paste | LOW | Add phrase to blacklist config; tighten VAD/thresholds; add fixture |
| Repetition loop | LOW | Retry with higher temperature / other model; loop detector |
| Wrong-window paste | MEDIUM | Text stays in clipboard; tray "Copy last transcript" |
| Model corrupt | LOW | Re-download with checksum; "Repair model" tray item |
| GPU backend crash | MEDIUM | Auto-fallback to CPU; setting to force CPU |
| AV quarantine | MEDIUM | Exclusion, rebuild with onedir/native shell, sign |
| Lost recording | MEDIUM | Recover from `recordings/last.wav` via "Retry last" |
| Architecture coupling (inference in UI/hook process) | HIGH | Refactor to worker process; avoid by designing it in P1 |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| 1 Whisper hallucination on silence | P1 | Silence/tap/noise fixtures produce no paste; blacklist unit tests |
| 2 Repetition loops | P1, P6 | Benchmark WER + loop detector tests on pt-BR fixtures |
| 3 LL hook timeout | P1, P6 | Callback p99 <5 ms; sleep/resume + 8 h soak |
| 4 Stuck modifiers | P2 | Harness types after paste; no latched modifiers |
| 5 Hotkey conflicts / IME | P1, P3 | Space not typed; layout-toggle warning; rebind works |
| 6 UIPI elevated windows | P2, P5 | Elevated test window yields overlay notice + clipboard |
| 7 Terminal paste | P2 | Paste matrix incl. Windows Terminal, no stray newline |
| 8 Focus stealing | P3 (spike P1/P2) | Foreground HWND unchanged assertion |
| 9 Clipboard races | P2 | Retry logic tests with a clipboard-holding helper process |
| 10 Overlay DPI/transparency | P3 | Screenshot checks at multiple DPIs/monitors |
| 11 Mic changes / privacy | P1, P3, P6 | Dead-input detection test; hot-plug test |
| 12 Model download | P1, P5 | Interrupted-download and checksum tests |
| 13 Cold load latency | P1, P6 | Startup timing test with budget |
| 14 AMD Vulkan | P1 | Spike benchmark decision; fallback test |
| 15 PyInstaller/AV | P5 (smoke build from P1/P2) | Frozen build smoke test; clean install |
| 16 Lost recordings / stuck state | P1 | Kill-process-mid-transcription retains wav |
| 17 Cloud issues | P4 | Mocked API error matrix |
| 18 Text normalization | P1 | Fixture-based output tests |
| 19 Tray/lifecycle | P3, P5 | Explorer-restart and quit tests |
| 20 Sleep/lock | P6 | Resume test |
| 21 Autonomous verification | P1 onward | Harness exists before feature work lands |

## Sources

Confidence note: compiled from established platform documentation and widely reported community behavior; not freshly web-verified in this pass. Items to confirm during phase-level research are marked (verify).

- Microsoft Learn: LowLevelKeyboardProc / `LowLevelHooksTimeout` (HKCU\Control Panel\Desktop), SendInput + UIPI remarks, RegisterHotKey, Extended Window Styles (WS_EX_NOACTIVATE, WS_EX_TRANSPARENT), clipboard functions and clipboard-history formats (`CanIncludeInClipboardHistory`, `ExcludeClipboardContentFromMonitorProcessing`), Per-Monitor V2 DPI awareness, WM_WTSSESSION_CHANGE, `TaskbarCreated`
- openai/whisper GitHub discussions: hallucinations on silence ("Thanks for watching", "Amara.org"), repetition with large-v3, `condition_on_previous_text`, compression-ratio/no-speech thresholds
- SYSTRAN/faster-whisper README and issues: `vad_filter`, Silero VAD parameters, int8 CPU usage, no AMD GPU path
- ggml-org/whisper.cpp: Vulkan backend notes, `--vad` support, model sizes
- OpenAI speech-to-text docs (25 MB limit, `language`, gpt-4o-transcribe) and Groq speech-to-text docs (rate limits, whisper-large-v3-turbo)
- PyInstaller docs and issue tracker: onefile temp extraction, antivirus false positives for bootloader; Defender false-positive submission portal
- Community projects with similar scope (Wispr-like/OSS dictation tools, e.g. Whispering, Handy, nerd-dictation analogs): lessons about focus-stealing overlays and clipboard paste

---
*Pitfalls research for: Windows push-to-talk Whisper dictation app (pt-BR)*
*Researched: 2026-10-22*
