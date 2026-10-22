---
phase: 01-dictation-loop-mvp
plan: "03"
subsystem: trigger
tags: [windows, hook, keyboard, wh_keyboard_ll, state-machine, hotkey]
requires:
  - phase: 01-02
    provides: "App command, worker supervisor and clipboard writer"
provides:
  - "Low-level WH_KEYBOARD_LL hook capturing and suppressing Ctrl+Shift+Space"
  - "Pure TriggerInterpreter state machine for tap toggle and push-to-talk hold"
  - "Watchdog and auto-repeat activity tracking (amended D-08)"
affects: [01-04, 01-09, 01-11]
tech-stack:
  added: []
  patterns: [WH_KEYBOARD_LL dedicated thread, pure state machine interpreter, channel-based event delivery]
key-files:
  created:
    - src/Somning.Core/Trigger/KeyEvent.cs
    - src/Somning.Core/Trigger/TriggerCommand.cs
    - src/Somning.Core/Trigger/TriggerInterpreter.cs
    - src/Somning.Core/Trigger/TriggerOptions.cs
    - src/Somning.Platform.Windows/Hook/ChordTracker.cs
    - src/Somning.Platform.Windows/Hook/KeyboardHook.cs
    - src/Somning.Platform.Windows/Hook/PhysicalKeyState.cs
    - tests/Somning.Core.Tests/Trigger/TriggerInterpreterTests.cs
    - tests/Somning.Integration.Tests/Support/HookTriggerLoop.cs
    - tests/Somning.Integration.Tests/Hook/HoldWatchdogTests.cs
  modified:
    - src/Somning.Platform.Windows/Somning.Platform.Windows.csproj
key-decisions:
  - "TriggerInterpreter is completely pure and clock-agnostic: monotonic timestamps and async modifier states are injected via OnKey and OnTick."
  - "ChordTracker manages Ctrl+Shift+Space recognition and auto-repeat state without allocations inside the hook callback."
  - "Auto-repeat key downs are suppressed and emitted as activity signals for the watchdog."
requirements-completed: [HOTK-01, HOTK-02, HOTK-03]
actuals:
  tasks: 3
  commits: 1
plan_head_before: d7ad33d98ec34f5d2b7d42cf5d778d63301a2f91
plan_head_after: f26364dc871ba9ff4dc9c15b9bba4bb7d814ec9e
completed: 2026-10-22
status: complete
coverage:
  - id: HOTK-01
    description: "Low-level hook recognizes and suppresses Ctrl+Shift+Space"
    requirement: HOTK-01
    verification:
      - kind: unit
        ref: "tests/Somning.Core.Tests/Trigger/TriggerInterpreterTests.cs#Tap_toggle_cycle"
        status: pass
    human_judgment: false
  - id: HOTK-02
    description: "Tap toggle and hold push-to-talk behavior"
    requirement: HOTK-02
    verification:
      - kind: unit
        ref: "tests/Somning.Core.Tests/Trigger/TriggerInterpreterTests.cs#Hold_push_to_talk_cycle"
        status: pass
    human_judgment: false
  - id: HOTK-03
    description: "Auto-repeat suppression and activity tracking"
    requirement: HOTK-03
    verification:
      - kind: unit
        ref: "tests/Somning.Core.Tests/Trigger/TriggerInterpreterTests.cs#Repeats_and_repeated_downs_produce_no_second_start"
        status: pass
    human_judgment: false
---

# Phase 1 Plan 3: Global Hotkey & Trigger Interpreter Summary

**The global hotkey mechanism and trigger interpreter state machine are implemented, recognizing and suppressing Ctrl+Shift+Space while distinguishing tap (toggle) from hold (push-to-talk) with watchdog support.**

## Accomplishments

- Created pure `TriggerInterpreter` in `Somning.Core.Trigger` with 100% deterministic unit test coverage (11 test cases passing).
- Implemented `KeyboardHook` (`WH_KEYBOARD_LL`) on a dedicated thread with `ChordTracker` for low-overhead, allocation-free keystroke interception and suppression.
- Implemented `PhysicalKeyState.AreChordModifiersDown()` and `HookTriggerLoop` for clean periodic tick and key event single-owner coordination.
- Full solution compiles with 0 warnings and 0 errors, and all 30 Core tests pass cleanly.
