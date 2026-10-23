---
schema_version: 1
open_count: 1
waived_count: 0
fixed_count: 0
total_count: 1
last_updated: 2026-10-23T08:53:48.046Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 01 | deviation | tests/Somning.Integration.Tests/Dictation/HarnessDictationTests.cs |  | HarnessDictationTests failed once intermittently in a full-suite run (73/74), cause not captured; passed 74/74 on rerun | open |  | 2026-10-23T08:53:48.046Z |  |

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
  }
]
````
