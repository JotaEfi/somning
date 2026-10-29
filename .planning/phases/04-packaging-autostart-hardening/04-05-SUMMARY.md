---
phase: 04-packaging-autostart-hardening
plan: 05
subsystem: test-harness
tags: [soak, paste-matrix, watchdog, gap-closure]
requires: [04-04]
provides: [soak-test, soak-report-evaluation, watchdog-liveness-tests, paste-target-matrix]
affects: [04-06]
key-files:
  created:
    - tests/Somning.Integration.Tests/Soak/SoakReport.cs
    - tests/Somning.Integration.Tests/Soak/SoakReportTests.cs
    - tests/Somning.Integration.Tests/Soak/SoakTests.cs
    - tests/Somning.Integration.Tests/Hook/HookWatchdogLivenessTests.cs
    - tests/Somning.Integration.Tests/Paste/PasteTargetCatalog.cs
    - tests/Somning.Integration.Tests/Paste/PasteTargetMatrixTests.cs
  modified: []
decisions:
  - "Clipboard-only delivery is reported by the controller as outcome paste_deferred with reason clipboard_set; the soak asserts that pair"
  - "Word target skips when any WINWORD process already runs, so the owner's instance is never touched"
status: complete
commits: 3
plan_head_before: b608bbe
plan_head_after: 012a17c
actuals:
  tokens: 28000
  tasks: 3
  commits: 3
---

# Phase 4 Plan 05: Soak, watchdog liveness and paste matrix tests Summary

Soak harness (N inject-tap dictations clipboard-only, then one real-hotkey dictation) with a pure, unit-tested `SoakReport.Evaluate`, watchdog liveness integration tests, and a Chrome/Edge/VS Code/Word paste matrix with sentinel clipboard read-back. All compiled; interactive tests were not executed.

## Commits
- 8bb0029: soak report evaluation, report tests, SoakTests (Category=Soak)
- 65d5d34: HookWatchdogLivenessTests (1 headless, 2 desktop)
- 012a17c: PasteTargetCatalog and PasteTargetMatrixTests

## Verification (headless only)
- `dotnet build Somning.sln -warnaserror`: 0 warnings, 0 errors.
- `SoakReportTests`: 11 passed (plan required >= 6).
- `HookWatchdogLivenessTests.Fake_probe_loop_reinstalls_only_after_threshold`: 1 passed (run by exact method name).
- `Somning.Core.Tests`: 307 passed, 0 failed.
- Discovery (`-list full`) shows SoakTests (Category=Soak), the 2 desktop watchdog facts, and PasteTargetMatrixTests.
- grep: no "retry" in SoakTests.cs; SetAutoPasteAsync(false) precedes the first tap and SetAutoPasteAsync(true) precedes the real-hotkey dictation; no `GetProcessesByName`; `--user-data-dir` used for chrome, edge, vscode; missing app => `Assert.Skip` only.

## Compiled but NOT exercised (owner is using the machine; runs in plan 04-06)
- `SoakTests` (starts the App, engine, clipboard, SendInput, Notepad).
- `HookWatchdogLivenessTests.Silently_removed_hook_is_reinstalled_and_probe_is_seen_again` and `Blocked_injection_is_inconclusive_and_causes_no_reinstall` (the latter skips honestly unless an elevated window is foreground).
- `PasteTargetMatrixTests` for chrome, edge, vscode, word (launch, focus, paste, Ctrl+A/Ctrl+C). Launch arguments (data: URL autofocus, Word `/q /n`, VS Code flags) and focus timing are unverified assumptions until run. The Theory's four ids are produced by `PasteTargetCatalog.Ids`; discovery lists the theory as one method, so the per-id rows were not enumerated.
- soak-report.json is written only by a soak run; none exists yet.

## Deviations from Plan
1. **[Process] Not strictly red-first.** The 04-04 production code already existed and the tests are new; the report tests and the fake-loop fact passed on first run. No production defect was found, so no production code changed.
2. **[Rule 3] Verify/list syntax:** runner filters worked as written (`-class`, `-method`); listing uses `-list full`.
3. **Interpretation:** the "blocked injection" desktop fact skips via `Requires.That` unless the foreground window has a higher integrity level than the test process (the only honest way to demonstrate a SendInput block).
4. Read-back keys are sent via a local SendInput helper in the matrix tests rather than extending `HotkeyInjector` (not in the plan's file list). Hook is not running in those tests, so no marker is needed.
5. Plan-head ledger file (protocol 0c) was not created before the first commit; `plan_head_before` is the 04-04 docs commit b608bbe, which is the actual HEAD before this plan.

## Known Stubs
None.

## Threat Flags
None.

## Self-Check: PASSED
All six created files exist; commits 8bb0029, 65d5d34, 012a17c present.
