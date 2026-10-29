---
phase: 04-packaging-autostart-hardening
plan: 04
subsystem: hook-reliability
tags: [watchdog, keyboard-hook, harness, packaging, gap-closure]
requires: [04-02]
provides: [hook-liveness-watchdog, headless-inject-tap, harness-package, SOMNING_APP_EXE]
affects: [04-05, 04-06]
key-files:
  created:
    - src/Somning.Core/Trigger/HookLivenessPolicy.cs
    - src/Somning.Core/Trigger/HookLivenessWatchdog.cs
    - src/Somning.Core/Ports/IHookLivenessProbe.cs
    - src/Somning.Platform.Windows/Hook/HookLivenessProbe.cs
    - tests/Somning.Core.Tests/Trigger/HookLivenessPolicyTests.cs
    - tests/Somning.Core.Tests/Trigger/HookLivenessWatchdogTests.cs
  modified:
    - src/Somning.Platform.Windows/Hook/KeyboardHook.cs
    - src/Somning.Platform.Windows/Input/InputMarkers.cs
    - src/Somning.App/Composition/AppComposition.cs
    - src/Somning.App/Program.cs
    - src/Somning.App/TestHarness/HarnessPipeServer.cs
    - Directory.Build.props
    - tools/package.ps1
    - tests/Somning.Integration.Tests/Support/RepoPaths.cs
    - tests/Somning.Integration.Tests/Support/AppHarnessDriver.cs
    - .planning/REQUIREMENTS.md
decisions:
  - "KeyboardHook.InstallCount and ProbeSeenCount made public (App assembly has no InternalsVisibleTo) for harness status"
status: complete
commits: 2
plan_head_before: 4a11d18e136c5cf007f835bd1770287e2a6da64b
plan_head_after: 9364acd
actuals:
  tokens: 30000
  tasks: 2
  commits: 2
---

# Phase 4 Plan 04: Hook liveness watchdog and harness plumbing Summary

Periodic F24-probe watchdog that reinstalls a silently dropped WH_KEYBOARD_LL hook (pure, unit-tested policy with storm limiting), plus headless harness plumbing (inject-tap, set-auto-paste, status counters), a `-WithHarness` package, `SOMNING_APP_EXE`, and honest DIST-01/DIST-03 bookkeeping.

## Commits
- 1be8931: feat(04-04) hook liveness watchdog (policy, watchdog, probe, hook marker, silent-removal seam, composition wiring)
- 9364acd: feat(04-04) harness inject-tap/set-auto-paste/status counters, harness package switch, SOMNING_APP_EXE, REQUIREMENTS

## Verification
- `dotnet build Somning.sln -warnaserror`: 0 warnings, 0 errors.
- HookLiveness tests: 11 passed (6 policy, 5 watchdog). Full Core.Tests: 307 passed, 0 failed.
- `tools/package.ps1 -WithHarness -OutputDir <temp>` succeeded; manifest `Harness: true`; `Somning.App.exe --test-harness` printed "requires --fixture <wav>" (harness present, exit before any startup). Temp dir removed.
- Probe marker check precedes the Paste/Mask check in `OnKeyboard` (lines 134 vs 139).

## Not exercised (headless-only constraint)
The F24 SendInput probe, the real silent-removal reinstall (`SimulateSilentRemovalForTests`), `inject-tap`, and `set-auto-paste` over the live pipe were compiled but NOT run: they need a live app / OS input, deferred to plans 04-05/04-06 per the owner's no-interactive-run rule. `SimulateSilentRemovalForTests` has no caller yet. The default (no switch) package was not rebuilt; its harness-free behavior is confirmed only by inspection (no `-p:SomningHarness` unless `-WithHarness`).

## Deviations from Plan
1. **[Rule 3 - Verify syntax]** None needed for the MTP filter: `dotnet test --project ... -- --filter-class '*HookLiveness*'` worked as written.
2. **[Rule 3 - Blocking]** xUnit1051 analyzer (warnaserror) required `TestContext.Current.CancellationToken` in tests; CS4004 forbade `await` in an unsafe class, so `SendInput` moved into a private unsafe helper in `HookLivenessProbe`.
3. **[Rule 3 - Blocking]** `InstallCount`/`ProbeSeenCount` changed from internal to public since App cannot see Platform internals.
4. **Interpretation:** `SOMNING_HOOK_WATCHDOG_MS` override is compiled only under `SOMNING_TEST_HARNESS` (satisfies "only under harness"; minimum 50 ms).
5. The watchdog reinstall callback reuses `ReinstallHookAfterWake`, which swallows exceptions, so `ReinstallCount` counts attempts requested.

## Known Stubs
None.

## Threat Flags
None beyond the plan's threat model (T-04-01..03 mitigated as specified).

## Self-Check: PASSED
Created files exist; commits 1be8931 and 9364acd present.
