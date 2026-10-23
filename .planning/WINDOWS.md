---
schema_version: 1
open_count: 3
waived_count: 0
fixed_count: 0
total_count: 3
last_updated: 2026-10-23T10:21:00.445Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 01 | deviation | tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs |  | HarnessDictationTests failed once intermittently in a full-suite run (73/74), cause not captured; passed 74/74 on rerun | open |  | 2026-10-23T08:53:48.046Z |  |
| 2 | 01 | deviation | tests/Somning.Integration.Tests/Recovery/RecoveryServiceTests.cs |  | Early_captures_during_delayed_recovery_are_admitted_once lost the first live paste once in a full-suite run (seed 610002936); undiagnosed, evidence now printed | open |  | 2026-10-23T10:20:59.238Z |  |
| 3 | 01 | deviation | src/Somning.App/Engine/EngineSupervisor.cs |  | queue_ms excludes App-side wait for engine readiness; StartupBeforeReadyTests asserts stop_to_paste_ms instead of plan's queue_ms > 0 | open |  | 2026-10-23T10:21:00.445Z |  |

````json
[
  {
    "id": 1,
    "kind": "deviation",
    "phase": "01",
    "file": "tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs",
    "line": null,
    "description": "HarnessDictationTests failed once intermittently in a full-suite run (73/74), cause not captured; passed 74/74 on rerun",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-10-23T08:53:48.046Z",
    "resolved_at": null,
    "milestone": null
  },
  {
    "id": 2,
    "kind": "deviation",
    "phase": "01",
    "file": "tests/Somning.Integration.Tests/Recovery/RecoveryServiceTests.cs",
    "line": null,
    "description": "Early_captures_during_delayed_recovery_are_admitted_once lost the first live paste once in a full-suite run (seed 610002936); undiagnosed, evidence now printed",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-10-23T10:20:59.238Z",
    "resolved_at": null,
    "milestone": null
  },
  {
    "id": 3,
    "kind": "deviation",
    "phase": "01",
    "file": "src/Somning.App/Engine/EngineSupervisor.cs",
    "line": null,
    "description": "queue_ms excludes App-side wait for engine readiness; StartupBeforeReadyTests asserts stop_to_paste_ms instead of plan's queue_ms > 0",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-10-23T10:21:00.445Z",
    "resolved_at": null,
    "milestone": null
  }
]
````
