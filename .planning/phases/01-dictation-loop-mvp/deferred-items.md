# Deferred Items — Phase 01

## From plan 01-09 (out of scope, pre-existing)

- **HoldWatchdogTests fail deterministically (2 tests)** — `Held_chord_3500ms_is_not_cut` and
  `Lost_trigger_up_is_caught_by_modifier_watchdog` throw `InvalidOperationException` from
  `PeriodicTimer.WaitForNextTickAsync` in `tests/Somning.Integration.Tests/Support/HookTriggerLoop.cs:35`.
  Cause: when a key event wins `Task.WhenAny`, the outstanding tick wait is abandoned and the next loop
  iteration calls `WaitForNextTickAsync` again while the previous wait is still pending (PeriodicTimer allows
  only one waiter). Reproduced identically on base commit `cdfdd38` (before any 01-09 change), so not caused
  by 01-09. Fix: keep the pending timer task across iterations and only re-arm it after it completes.
  Plan 01-10's DictationHost must not copy this pattern for its own 50 ms Tick task.

  **Resolved after 01-09** (`fix(01-09)` commit): `HookTriggerLoop` now keeps both pending waits (timer tick and
  channel read) across iterations and re-arms each only after it completes. The abandoned `ReadAsync` could also
  swallow a key event, so both were fixed. Integration suite: 67/67 green.

## From plan 01-10

- **HarnessDictationTests intermittent failure in one full-suite run.** `TapTap_pastes_fixture_into_notepad`
  failed once (31 s) in the first full `Somning.Integration.Tests` run (73/74). It passed in the next full run
  (74/74) and in 5 isolated runs. The failure message was not captured (the output was filtered). The 31 s
  duration matches the 20 s paste poll running out, so either nothing was pasted or the dictation metrics line
  never appeared. Likely suspects: foreground or modifier state left behind by an earlier desktop test. Plan
  01-12/01-13 E2E work should log the dictation outcome/reason in the assertion message and re-check this under
  repeated full-suite runs.

## From plan 01-11

- **`queue_ms` does not include the App-side wait for engine readiness.** `EngineSupervisor` measures `queue_ms`
  from its own `TranscribeAsync` call to the request write. The reducer only dispatches `Transcribe` after
  `EngineReady` (and after recovery admission), so a dictation captured during model load reports `queue_ms` of
  about 0 ms even though it waited around 10 s. The wait does show up in `stop_to_paste_ms`.
  `StartupBeforeReadyTests` therefore asserts `stop_to_paste_ms` against the observed Starting window instead of
  the plan's `queue_ms > 0`, which held only about 1 run in 2. If APP-06/D-23 means queue time to cover the FIFO
  wait, the reducer needs an enqueue timestamp, with queue_ms computed as dispatch time minus that timestamp. This
  is a metrics-definition decision for the 01-12/01-13 measurement work.
- **`TargetPasterTests.Focus_change_inside_same_top_level_window_defers` failed deterministically in the full suite**
  ("could not acquire two-edit test foreground") once the new 01-11 classes changed xUnit's collection shuffle
  (seed 610002936). It runs right after `Foreground_changed_defers_and_leaves_text_on_clipboard` closes two
  Notepads, and Windows briefly refused the foreground change. Fixed in test support: `ForegroundHelper.EnsureForeground`
  now retries its acquisition sequence for up to 1 s. The full suite with the same seed is green afterwards.
- **`RecoveryServiceTests.Early_captures_during_delayed_recovery_are_admitted_once`: one intermittent lost first
  paste in a full-suite run (seed 610002936).** Both live outcomes were `pasted`, but Notepad held only the second
  text ("First live text: ''"). It passed in every isolated or class-level run (6+) and in the next full run
  with the same seed. It is not diagnosed. Suspects: Notepad not processing the first injected Ctrl+V
  (e.g. its OpenClipboard racing the next clipboard write), or a focus/caret effect from earlier desktop tests.
  The assertion now prints the Notepad text, clipboard, every outcome's metrics and the App log. 01-12/01-13
  should capture that evidence under repeated full-suite runs. Same family as the 01-10 TapTap intermittent.
