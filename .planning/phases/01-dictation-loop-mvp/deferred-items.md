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
