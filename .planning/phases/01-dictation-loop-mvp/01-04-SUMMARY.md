---
phase: 01-dictation-loop-mvp
plan: "04"
subsystem: output
tags: [windows, clipboard, sendinput, focus, integrity, notepad]
requires:
  - phase: 01-02
    provides: "STA clipboard writer and delivery result contract"
  - phase: 01-03
    provides: "Global hook and tagged input markers"
provides:
  - "Target-aware paste with foreground, process, focused HWND, modifier and integrity checks"
  - "Copy-only output for recovery and explicit clipboard/paste outcomes"
  - "Controlled recovery from a partially accepted SendInput batch"
affects: [01-09, 01-10, 01-12, 01-13]
actuals:
  tokens: 10651
  tasks: 2
  commits: 4
  plan_head_before: 95c81c45b88d72b649b735f8966f898d57d05372
  plan_head_after: 94273768c89824d80ab4062df51c9504a5d98df9
tech-stack:
  added: []
  patterns: ["pure paste safety gate", "injectable input and clipboard seams", "owned foreground test windows"]
key-files:
  created:
    - src/Somning.Platform.Windows/Paste/ForegroundProbe.cs
    - src/Somning.Platform.Windows/Paste/ModifierWaiter.cs
    - src/Somning.Platform.Windows/Paste/PasteInjector.cs
    - src/Somning.Platform.Windows/Paste/TargetPaster.cs
    - tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs
    - tests/Somning.Integration.Tests/Paste/IntegrityReaderTests.cs
    - tests/Somning.Integration.Tests/Paste/PasteInjectorTests.cs
    - tests/Somning.Integration.Tests/Support/ForegroundHelper.cs
    - tests/Somning.Integration.Tests/Support/NotepadDriver.cs
    - tests/Somning.Integration.Tests/Support/TwoEditWindow.cs
  modified:
    - src/Somning.Core/Output/PasteGate.cs
    - src/Somning.Platform.Windows/Clipboard/ClipboardWriter.cs
key-decisions:
  - "Recheck the clipboard sequence after modifier waiting and target probing immediately before Ctrl+V; defer as clipboard_changed if another process copied meanwhile."
  - "After a partial SendInput prefix, send tagged key-ups only for keys left down by accepted events, retry only an unaccepted cleanup suffix, and expose cleanup failure."
  - "Use injectable clipboard, modifier, and input sender ports so race and partial-send behavior can be verified without affecting the active desktop."
requirements-completed: [OUT-01, OUT-02, OUT-03]
coverage:
  - id: D1
    description: "Unicode transcript is pasted into the captured Notepad edit control and remains on the clipboard; recovery can copy without pasting."
    requirement: OUT-01
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs#Pastes_into_notepad_and_keeps_clipboard"
        status: pass
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs#Copy_only_never_pastes"
        status: pass
    human_judgment: false
  - id: D2
    description: "Unsafe foreground, focus, clipboard-sequence, integrity, or partial-input conditions defer delivery with a reason."
    requirement: OUT-03
    verification:
      - kind: unit
        ref: "tests/Somning.Core.Tests/Output/PasteGateTests.cs"
        status: pass
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs"
        status: pass
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/IntegrityReaderTests.cs"
        status: pass
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/PasteInjectorTests.cs"
        status: pass
    human_judgment: false
  - id: D3
    description: "A real same-window focused-control change is detected before paste."
    requirement: OUT-02
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs#Focus_change_inside_same_top_level_window_defers"
        status: pass
    human_judgment: false
duration: 45min
completed: 2026-10-23
status: complete
---

# Phase 1 Plan 4: Safe foreground paste Summary

**Transcribed text now reaches only the foreground process and focused HWND captured at stop, with clipboard persistence and a reasoned defer path when safety checks fail.**

## Performance

- **Duration:** About 45 minutes; this continuation did not record a precise start timestamp.
- **Completed:** 2026-10-23
- **Tasks:** 2
- **Files changed:** 12 plan-owned implementation and test files.

## Accomplishments

- `TargetPaster` writes normalized text, waits up to 1.5 seconds for Ctrl/Shift/Alt/Win keys to be released, captures the current foreground target again, checks the pure `PasteGate`, and verifies the clipboard sequence immediately before injecting one four-event Ctrl+V batch.
- `ForegroundProbe` reports HWND, process/thread identity, focus-query result and target integrity RID. `IntegrityReader` reads and caches Somning's integrity level and returns unknown for inaccessible target processes.
- `CopyOnlyAsync` supports startup recovery without synthesizing input. Clipboard failures and unsafe targets return explicit outcomes while the transcript remains available for manual paste.
- A partially inserted input prefix is balanced with tagged key-ups for only its still-down keys. A partial cleanup retries only the remaining key-ups, and the result reports when cleanup remains incomplete.
- Integration fixtures own their Notepad or two-edit test window. The real paste-marker hook test was also corrected in a separate Plan 03 test commit to paste only into its own Notepad.

## Task Commits

1. **Task 1: Notepad target paste tracer and platform adapters** - `8554344` (feat; Core paste contracts and table tests were committed before this continuation).
2. **Task 2: Pure paste gate, integrity checks and same-window focus test** - `9427376` (feat; includes the remaining adapter implementations and Plan 04 integration tests).

The measured plan ledger contains four commits from `95c81c4` through `9427376`. This also includes the intervening Plan 03 state commit and the separately committed owned-Notepad fix for its paste-marker test. The execution summary commit is separate and excluded from `actuals.commits`.

## Executed checks

- Git Bash `dotnet test --project tests/Somning.Core.Tests --filter-class "*.PasteGateTests"`: 10 passed, no failures or skips.
- Git Bash `dotnet test --project tests/Somning.Integration.Tests --filter-class "*.PasteInjectorTests"`: 5 passed, no failures or skips.
- Strict desktop `TargetPasterTests`: 5 passed, no failures or skips. This includes actual paste into an owned Notepad, foreground-change deferral, clipboard-only output, a clipboard sequence change during the modifier wait, and a same-window focus change.
- Strict desktop `IntegrityReaderTests`: 2 passed, no failures or skips.
- Strict desktop `KeyboardHookTests`: 5 passed, no failures or skips, including the Plan 03 test correction that confines Ctrl+V to a test-owned edit control.
- C# review approved after fixing the startup-failure path to destroy a partially created two-edit window. Final generic review found no remaining findings.
- `git diff --check` passed before the implementation commit.

## Decisions Made

- HWND focus identity catches control changes within one top-level window. It cannot distinguish individual DOM fields hosted inside one browser render widget; this remains the documented Phase 1 limitation (T-01-28).
- The clipboard sequence check runs directly before `SendInput`, after all asynchronous waiting and target probing, to preserve a user's newer copy.
- Partial-input cleanup follows the accepted event prefix and never sends key-ups for keys whose down event was not inserted.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing safety] Guarded clipboard sequence changes during modifier waiting**
- **Found during:** Task 1 (target paste tracer)
- **Issue:** The user or another application could replace the clipboard while the asynchronous modifier wait was running, causing Ctrl+V to paste content unrelated to this transcript.
- **Fix:** Compare the current clipboard sequence to `SequenceAfter` immediately before input and return `paste_deferred / clipboard_changed` on mismatch.
- **Files modified:** `src/Somning.Platform.Windows/Clipboard/ClipboardWriter.cs`, `src/Somning.Platform.Windows/Paste/TargetPaster.cs`, `tests/Somning.Integration.Tests/Paste/TargetPasterTests.cs`
- **Verification:** Controlled writer test proves no paste injection occurs after an external copy.
- **Committed in:** `9427376`

**2. [Rule 2 - Missing safety] Released keys left down by partial SendInput**
- **Found during:** Task 2 (final review)
- **Issue:** A short SendInput prefix could leave an injected modifier or V key logically down.
- **Fix:** Track outstanding key-down events in the inserted prefix, send tagged key-ups in reverse order, retry only any unaccepted cleanup suffix, and surface cleanup failure.
- **Files modified:** `src/Somning.Platform.Windows/Paste/PasteInjector.cs`, `src/Somning.Platform.Windows/Paste/TargetPaster.cs`, `tests/Somning.Integration.Tests/Paste/PasteInjectorTests.cs`
- **Verification:** Five injected-sender tests cover prefixes 1/2/3, incomplete cleanup, and suffix retry without touching desktop input.
- **Committed in:** `9427376`

**3. [Rule 1 - Test safety] Kept the Plan 03 paste-marker test inside its owned Notepad**
- **Found during:** Plan 04 review of the shared real-input tests
- **Issue:** The test sent Ctrl+V to whatever app was foreground and could paste user clipboard contents into an unrelated editor.
- **Fix:** Give the test a test-owned Notepad edit control, place a known test string on the clipboard, and assert that exact text lands there.
- **Files modified:** `tests/Somning.Integration.Tests/Hook/KeyboardHookTests.cs`
- **Verification:** Strict `KeyboardHookTests` passed 5/5.
- **Committed in:** `f7f64a7` (separate Plan 03 test correction)

**4. [Rule 1 - Test lifecycle] Destroyed partially created two-edit test windows on startup failure**
- **Found during:** C# review
- **Issue:** A failure after creating the top-level HWND but before both Edit controls existed could leak a visible test window.
- **Fix:** Track the HWND outside the startup try block and destroy it on the owning thread in `finally`.
- **Files modified:** `tests/Somning.Integration.Tests/Support/TwoEditWindow.cs`
- **Verification:** C# reviewer confirmed the failure path is resolved; strict `TargetPasterTests` passed 5/5.
- **Committed in:** `9427376`

## Known Limitations

- Win32 `GetGUIThreadInfo` cannot tell two DOM fields apart when both are represented by one browser render HWND. The short stop-to-paste interval and top-level/focus HWND checks are the Phase 1 mitigation.

## Self-Check: PASSED

- Implementation files and tests exist; required implementation commits `8554344` and `9427376` are ancestors of HEAD.
- The measured plan ledger records base `95c81c45b88d72b649b735f8966f898d57d05372`, head `94273768c89824d80ab4062df51c9504a5d98df9`, and four commits.
- All listed test filters completed successfully with zero skipped tests.
