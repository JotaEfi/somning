# Feature Research

**Domain:** Windows push-to-talk voice dictation utility (hotkey -> mic -> Whisper STT pt-BR -> clipboard + auto Ctrl+V)
**Researched:** 2026-10-22
**Confidence:** MEDIUM (hotkey/cancel behavior from vendor docs and search; clipboard-restore and some Handy/Whispering/VoiceInk details come from training knowledge and secondary sources, flagged below)

## Feature Landscape

### Table Stakes (Users Expect These)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Global hotkey, works in any app | Core of every product (Wispr, Superwhisper, Handy, OpenWhispr, Win+H) | MEDIUM | Win32 RegisterHotKey cannot detect key-up, so hold mode needs a low-level keyboard hook (WH_KEYBOARD_LL) or polling GetAsyncKeyState. Pick a combo that does not collide with app shortcuts (Ctrl+Shift+Space is fine). |
| Hold = push-to-talk (release stops) | Wispr Flow default is hold; Handy, OpenWhispr (Windows), Superwhisper all offer it | MEDIUM | Needs key-up detection (see above). |
| Tap = toggle (hands-free) | Wispr (double-tap hands-free), Superwhisper toggle, Handy toggle mode, OpenWhispr tap-to-talk default | MEDIUM | Hybrid single-key tap/hold is an open feature request on Superwhisper (~300 ms threshold), so it is uncommon but a known-good design. Rule: press down starts recording immediately; if released < ~300 ms treat it as a tap and keep recording (toggle); if held longer, release stops. Next tap stops. |
| Recording indicator overlay | Every competitor shows something; users must know the mic is live | MEDIUM | Bottom-center pill, topmost, WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW + WS_EX_TRANSPARENT (click-through) so focus never leaves the target field. Never steal focus, or paste lands in the wrong place. |
| Live waveform/level meter driven by mic RMS | Confirms the mic works and is hearing speech (Wispr, Superwhisper, WhatsApp style) | MEDIUM | ~30-60 fps, RMS/peak from the capture callback, smoothed, bar count ~20-40. Flat line = "no input" signal for the user. |
| Distinct states: listening / transcribing / error | Users need to know if it is processing vs dead | LOW | Transcribing = animated dots/spinner or waveform morph. Error = short red message, auto-dismiss after a few seconds. |
| Cancel with Esc during recording (and during transcribing) | Wispr: Esc cancels by default, works even with modifiers held, and swallows the key while active. Superwhisper: dedicated cancel shortcut. VoiceInk has a cancel key. | MEDIUM | Esc must be captured (hook) only while recording, otherwise it breaks Esc in every other app. Discard audio, do not paste, hide pill. Cancel during transcribing: drop the result when it arrives (cancel token). |
| Accidental-tap guard (min duration) | Wispr discards very short sessions (secondary source: < ~0.5 s saves nothing); Superwhisper cancels < 30 s immediately without confirmation | LOW | Drop clips < ~300-500 ms of audio, or with no speech energy (simple VAD/RMS gate), silently (pill flashes and vanishes, no error). Also protects against Whisper hallucinations on silence ("Obrigado por assistir"). |
| Auto-paste into focused field | The whole point (Wispr, Superwhisper, Handy paste on release) | MEDIUM | Set clipboard, SendInput Ctrl+V. Pitfalls: Ctrl+Shift modifiers still physically held when hold-to-talk releases (must wait for/release modifiers or paste becomes Ctrl+Shift+V); elevated (admin) target windows ignore SendInput from non-elevated process (UIPI); terminals may need Ctrl+Shift+V or Shift+Insert. |
| Text stays in clipboard | Explicit owner requirement; also acts as a safety net if paste fails | LOW | This deliberately differs from the common "save and restore previous clipboard" behavior (see Anti-Features). Competitors' restore behavior: not confirmed in sources; Handy and Wispr have historically offered a "paste last transcript" fallback, which is the same safety-net idea. |
| Local Whisper STT, offline | Handy, VoiceInk, Superwhisper, OpenWhispr, Whispering all offer local | HIGH | Model choice and runtime are STACK.md concerns. Language fixed to pt-BR (pass language="pt" to Whisper) to avoid misdetection. |
| System tray icon + menu | Handy, OpenWhispr, Wispr all live in tray | LOW | Settings, open logs, quit. Icon can reflect state (idle/recording). |
| Settings persisted (hotkey, mic, engine, key) | Expected | LOW | JSON in %APPDATA%. API key should be protected (DPAPI), not plain text. |
| Microphone device selection | All competitors | LOW | Default device plus dropdown; handle device unplug gracefully. |
| Start with Windows | Utility apps live at login | LOW | HKCU Run key or startup shortcut. |
| Never lose a recording on failure | Owner requirement | MEDIUM | Keep last audio (and last text) on disk/memory until the next successful run; allow retry. Wispr and others expose "paste last transcript". |
| Error feedback (no mic, model missing, API fail) | Owner requirement | LOW | Overlay message plus log file. |
| Basic punctuation and capitalization in output | Whisper produces it natively; users judge accuracy on it | LOW | See Punctuation section below. |

### Differentiators (Competitive Advantage)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Hybrid tap/hold on a single hotkey | Most apps make you pick a mode or use two keys; Superwhisper users are requesting exactly this | MEDIUM | Already in PROJECT.md. Biggest UX differentiator for v1. Needs threshold constant (~250-300 ms) and a state machine. |
| Optional cloud engine (Groq whisper-large-v3-turbo / OpenAI gpt-4o-transcribe) | Fast and highest quality on demand; local-only CPU is slow on a Ryzen 5600X without CUDA | MEDIUM | Same interface as local engine. Cloud is opt-in, key stored in settings. |
| Start/stop sounds (short soft beeps) | Audible confirmation when you are not looking at the screen (Superwhisper and Wispr offer sound cues) | LOW | Two tiny WAVs, off/on in settings. Beware: the start sound can be captured by the mic if played at record start; play before opening the stream or trim the first ~150 ms. Treat as optional polish. |
| Spoken-punctuation / voice commands ("nova linha", "vírgula") | Wispr/Windows Voice Typing support commands; helps users who dictate for code or structured text | MEDIUM | Small pt-BR post-process map applied to the transcript. Whisper mostly punctuates on its own, so value is mainly "nova linha / novo parágrafo". Defer unless asked. |
| Custom vocabulary / initial prompt | Better handling of names and jargon (Wispr dictionary, Superwhisper vocabulary) | LOW | Whisper `initial_prompt` string in settings. Cheap and effective; also helps steer pt-BR orthography. |
| Simple post-filters (strip hallucinated phrases, trim whitespace, trailing-space option) | Clean output, avoids junk on silence | LOW | Blocklist of known Whisper pt-BR hallucinations. |
| Paste-last-transcript hotkey / tray item | Recovery if paste went to the wrong window (Wispr ships this) | LOW | Cheap because last text is retained anyway. |
| Paste method fallback (Ctrl+V, Shift+Insert, Ctrl+Shift+V, per-app) | Terminals and some apps reject Ctrl+V | MEDIUM | Start with Ctrl+V only; add per-process override only when a real app fails. |
| Max-duration safety (e.g. auto-stop at 5 min) | Prevents runaway toggle sessions | LOW | Shows a warning in pill near the limit. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Restore the previous clipboard after paste | Common in commercial apps, avoids clobbering user's clipboard | Conflicts with the owner requirement (text stays in clipboard). Restore also needs a timing delay after Ctrl+V (target app reads clipboard asynchronously; restoring too early pastes the OLD content) and cannot faithfully round-trip non-text formats (images, files). | Keep transcript in clipboard. Optionally a v2 toggle "restore clipboard" with a delay of ~200-500 ms and text-only support. |
| Live streaming partial text on screen | Feels responsive (Wispr/Windows Voice Typing do it) | Lower accuracy, much more complexity (chunking, revisions, typing into the target field repeatedly). Already out of scope. | Waveform only, transcribe on stop. |
| LLM rewriting / "make it formal" / app-aware tone (Wispr Flow's headline feature) | Polished output | Cost, latency, privacy, prompt tuning, and not needed for pt-BR dictation core value. | v2 opt-in, cloud only. |
| Multi-language and auto language detection | Seems flexible | Whisper auto-detect misfires on short clips (pt-BR becomes pt-PT or Spanish); owner is pt-BR only. | Fixed `pt`, language stored as a config value for later. |
| Transcription history UI / search | Competitors have it | A whole UI surface; not core. | Plain log file plus "paste last transcript". |
| Typing text by simulated keystrokes instead of paste | Avoids touching the clipboard | Slow for long text, breaks on accents/IME, drops characters, per-app quirks | Clipboard + Ctrl+V. |
| Always-on listening / wake word | "Hands-free" | Privacy, battery, false triggers, constant CPU | Hotkey only. |
| Elaborate settings window (themes, many overlay options) | Customization | Bloat contradicts "functional and simple" | Minimal settings: hotkey, engine, key, mic, auto-paste, sounds, start with Windows. |
| Speaker diarization, meeting transcription, file import | Superwhisper/VoiceInk have these | Different product | Out of scope. |
| Auto-update, telemetry, accounts | SaaS norms | Single personal user; adds risk and surface | None. Manual installer upgrades. |
| Separate "cancel" confirmation dialog | Superwhisper asks confirmation on long recordings | Extra friction for a single user | Esc cancels instantly; last audio retained for a few seconds if undo is wanted later. |

## Feature Dependencies

```
Global hotkey (low-level hook)
    ├──requires──> Key-up detection
    │                   └──enables──> Hold = push-to-talk
    │                                      └──enables──> Hybrid tap/hold state machine
    └──enables──> Esc-to-cancel (hook active only while recording)

Mic capture (16 kHz mono)
    ├──enables──> Waveform overlay (RMS from same stream)
    ├──enables──> Min-duration / silence gate
    └──requires──> Mic device selection (settings)

Min-duration / silence gate ──protects──> STT engine (no hallucinations on silence)

STT engine abstraction (local | cloud)
    ├──requires──> Model present / API key in settings
    └──enables──> Transcribing state in overlay

Transcript ──> post-processing (trim, hallucination filter, vocab prompt)
    └──> Clipboard write ──> SendInput Ctrl+V  (requires modifiers released, target not elevated)
              └──enables──> Paste-last-transcript

Overlay (no-activate window) ──conflicts──> anything that steals focus (would break auto-paste target)
Restore-clipboard ──conflicts──> Text stays in clipboard
Tray icon ──hosts──> Settings, logs, quit, paste-last
```

### Dependency Notes

- **Hybrid tap/hold requires low-level key hook:** RegisterHotKey fires only on press; without key-up you cannot do push-to-talk.
- **Esc-to-cancel requires a conditional hook:** register/swallow Esc only in the recording and transcribing states; Wispr captures Esc only while dictation is active, which is the same approach.
- **Auto-paste requires focus preservation:** the overlay must be WS_EX_NOACTIVATE and click-through; also record the foreground HWND at start so a window switch mid-recording can be detected (optional warning).
- **Auto-paste requires releasing held modifiers:** with Ctrl+Shift+Space hold mode, the user may still hold Ctrl/Shift at release; wait for key-up of modifiers (or send synthetic key-ups) before sending Ctrl+V.
- **Min-duration gate protects STT:** Whisper hallucinates on silence and tiny clips.
- **Restore-clipboard conflicts with keep-in-clipboard:** pick one; v1 = keep.

## UX Details (for requirements)

**Overlay / indicator**
- Pill ~180-260 x 40-48 px, bottom-center above the taskbar (use work area, not screen height), rounded, dark translucent, subtle fade/scale in (~120-150 ms), fade out on finish. Multi-monitor: show on the monitor of the foreground window (or cursor).
- Listening: animated bars from live RMS; optional elapsed timer. Transcribing: bars collapse to a pulsing/dot animation. Error: icon + short message ~3 s. Success: hide immediately on paste (no "done" toast needed; optionally a brief check).
- Click-through, not focusable, no taskbar button, never takes keyboard focus. Render with a layered window or WebView/GPU surface; must not block the capture thread.

**Hotkey modes**
- Table of competitor defaults: Wispr = hold PTT, double-tap hands-free; Superwhisper = separate toggle and PTT bindings (single modifier keys allowed); Handy/OpenWhispr = toggle or PTT selectable (OpenWhispr default tap-to-talk, PTT Windows-only). Somning = single combo, hybrid. Include an on-screen hint of the mode in the overlay for first runs only (not needed for single user).
- Edge cases: key auto-repeat while held (ignore repeats), hotkey pressed while already transcribing (ignore or queue; recommend ignore with a subtle shake), hotkey pressed during error display.

**Cancel and accidental taps**
- Esc cancels instantly with no confirmation, discards audio, no paste. Wispr: Esc is swallowed and works with extra modifiers held. Min duration: discard sessions < ~0.4 s or whose peak RMS is below a threshold, silently.

**Sounds/feedback**
- Optional, default OFF or very quiet; visual overlay is the primary feedback. Provide start, stop, cancel/error cues if enabled.

**Clipboard behavior**
- Write text (CF_UNICODETEXT) with retry (clipboard can be locked by other apps), then Ctrl+V. Consider flagging clipboard content as excluded from Windows clipboard history/cloud sync (ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0) only if the owner does NOT want it in Win+V history; owner said he wants manual re-paste, so leave history on.

**Punctuation handling**
- Whisper large-v3-turbo emits punctuation and casing itself; no extra model needed. Use initial_prompt with a well-punctuated pt-BR sentence to bias towards punctuated output. Do not add automatic spoken-command parsing in v1. Normalize: trim, collapse repeated whitespace, optionally drop trailing period for very short (<3 words) utterances if it annoys, optional trailing space toggle. Remove known silence hallucinations.

## MVP Definition

### Launch With (v1)

- [ ] Global hotkey with hybrid tap/hold state machine (default Ctrl+Shift+Space)
- [ ] Mic capture 16 kHz mono, device selection
- [ ] Bottom-center no-focus pill with live waveform + transcribing + error states
- [ ] Esc cancel
- [ ] Min-duration and silence gate (no accidental-tap junk, no hallucinations)
- [ ] Local Whisper pt-BR transcription (engine abstraction in place)
- [ ] Clipboard write + auto Ctrl+V with modifier handling; text stays in clipboard
- [ ] Tray icon (settings, logs, quit) and persisted settings
- [ ] Failure handling that keeps last recording/text (paste-last-transcript via tray)
- [ ] Packaged installer, start with Windows

### Add After Validation (v1.x)

- [ ] Cloud engine (Groq/OpenAI) with API key — if local latency on the 5600X is too slow, promote to v1
- [ ] Start/stop sounds
- [ ] Custom vocabulary / initial prompt setting
- [ ] Per-app paste method fallback (when a real app fails)
- [ ] Paste-last-transcript global hotkey

### Future Consideration (v2+)

- [ ] Additional languages
- [ ] Spoken punctuation / voice commands
- [ ] LLM post-processing, app-aware formatting
- [ ] Optional clipboard restore mode
- [ ] Transcription history

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Global hotkey + hybrid tap/hold | HIGH | MEDIUM | P1 |
| Mic capture + waveform pill | HIGH | MEDIUM | P1 |
| Local Whisper pt-BR | HIGH | HIGH | P1 |
| Clipboard + auto-paste | HIGH | MEDIUM | P1 |
| Esc cancel | HIGH | LOW-MEDIUM | P1 |
| Min-duration / silence gate | HIGH | LOW | P1 |
| Error states, keep last audio | HIGH | MEDIUM | P1 |
| Tray + settings | MEDIUM | LOW | P1 |
| Installer + autostart | MEDIUM | MEDIUM | P1 |
| Cloud engine | MEDIUM | MEDIUM | P2 |
| Sounds | LOW | LOW | P2 |
| Vocabulary prompt | MEDIUM | LOW | P2 |
| Paste-last hotkey | MEDIUM | LOW | P2 |
| Voice commands | LOW | MEDIUM | P3 |
| LLM cleanup | MEDIUM | HIGH | P3 |
| History UI | LOW | MEDIUM | P3 |

## Competitor Feature Analysis

| Feature | Wispr Flow | Superwhisper | Handy / OpenWhispr / VoiceInk | Somning approach |
|---------|------------|--------------|-------------------------------|------------------|
| Hotkey mode | Hold PTT; double-tap hands-free | Separate toggle and PTT; single modifier keys | Toggle or PTT (OpenWhispr default tap, PTT Windows only) | One combo, tap=toggle, hold=PTT |
| Cancel | Esc default, works with modifiers, swallowed | Dedicated cancel shortcut; instant < 30 s, confirm for longer | Cancel key (VoiceInk) | Esc, instant |
| Short-press handling | Hint shows actual shortcut; very short sessions (<~0.5 s, secondary source) discarded | n/a | n/a | Silent discard < ~0.4 s / silence |
| Indicator | Floating pill/bar near bottom with waveform | Recording window / mini indicator | Overlay / tray icon state | Bottom-center no-focus pill with waveform |
| Paste | Auto-insert, paste-last shortcut | Auto-paste | Auto-paste on release | Clipboard + Ctrl+V, text kept |
| Clipboard restore | Not documented in sources found | Not documented | Not documented | No restore (owner wants text kept) |
| Engine | Cloud | Local + cloud | Local (whisper.cpp), some cloud | Local default + optional cloud |
| Post-processing | LLM cleanup, dictionary, snippets | Modes with LLM | Optional AI enhancement | None in v1 |
| Punctuation | Auto via model + commands | Auto via model | Auto via model | Whisper native, prompt-biased |

## Sources

- Wispr Flow hotkey docs: https://docs.wisprflow.ai/articles/2612050838-supported-unsupported-keyboard-hotkey-shortcuts (HIGH for hotkey and Esc behavior)
- Wispr Flow UX reference (secondary, compiled): https://cdn.jsdelivr.net/npm/@agent-native/core@0.124.6/corpus/templates/clips/desktop/design-refs/wispr-ux.md (LOW for the <0.5 s figure)
- Superwhisper shortcuts docs: https://ai.superwhisper.com/docs/get-started/settings-shortcuts (MEDIUM)
- Superwhisper feature request, tap/hold single hotkey: https://superwhisper.userjot.com/board/p/same-hotkey-for-push-to-talk-and-toggle-auto-detect-by (MEDIUM)
- OpenWhispr docs: https://docs.openwhispr.com/guides/dictation (MEDIUM)
- Handy / VoiceInk directory listings: https://openalternative.co/alternatives/voiceink (LOW, secondary)
- Win32 behavior (RegisterHotKey no key-up, UIPI, WS_EX_NOACTIVATE, clipboard formats) and Whisper silence hallucinations: training knowledge, MEDIUM; verify in STACK/PITFALLS phases.
- Gaps: clipboard-restore behavior of each competitor not confirmed; Whispering not found in search.

---
*Feature research for: Windows push-to-talk voice dictation (pt-BR)*
*Researched: 2026-10-22*
