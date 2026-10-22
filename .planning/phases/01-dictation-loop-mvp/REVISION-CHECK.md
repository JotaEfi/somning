# Phase 1 interrupted planning revision

Date: 2026-10-22. Status: plan revision restored; implementation acceptance is NOT claimed. The original 01-08 was recovered with `git show HEAD:.planning/phases/01-dictation-loop-mvp/01-08-PLAN.md`; its acceptance scope is now in 01-12/01-13. Roadmap lists 13 plans in 10 waves, 32 tasks. Run all execution and verification sequentially despite independent plans sharing waves.

## Execution shell contract

Every `<automated>` body is Bash source. On this PowerShell 5.1 host execute it with the installed **Git for Windows Bash**, explicitly `C:/Program Files/Git/bin/bash.exe`, passing `-lc` and the complete command body as ONE argument. Do not resolve plain `bash` (it may select WSL), and do not evaluate the body in PowerShell. In PowerShell, the invoking executor uses `& 'C:/Program Files/Git/bin/bash.exe' -lc $command` and fails immediately if `$LASTEXITCODE -ne 0`. `$command` is already-read text, not an interpolated shell fragment. This binding supersedes older ledger language assuming every executor already uses Git Bash. All 32 command bodies passed actual `bash.exe -n -c` syntax checks. This verifies parsing, not the unimplemented commands' success.

PowerShell scripts themselves target actual `powershell.exe` 5.1, checking every native exit internally and restoring changed environment in finally. MTP exit 8 is a failure. Read each task's expected test prerequisites; tests are never silently replaced by zero selections. Engine, GPU, hooks, clipboard and desktop tests never run concurrently.

## Static revision checks

- 13 plan paths exist; every dependency points to an earlier wave. No same-wave `files_modified` overlaps.
- 14/14 Phase 1 requirement IDs occur in plan frontmatter; D-01 through D-23 occur in the plan set.
- 41 numeric threat IDs are unique across the set; reserved T-01-SC repeats intentionally. The duplicated T-01-18 in App wiring was renumbered T-01-44.
- 32 automated command bodies parse in explicitly installed Git Bash.
- `verify plan-structure` and `frontmatter validate --schema plan` are the structural checks. They do not prove implementation, timings, runtime API availability or final phase acceptance.
- Calibration returns factor 1, sample_count 0 and confidence low. Newly completed plans use that calibration, rather than claiming measured implementation cost.
- Independent final delta review should inspect 01-08 benchmark, 01-09 startup admission extension, 01-11 startup/harness, and restored 01-12/01-13 before those plans execute. Parent coordinates that review and the revision commit.

## Goal and requirements audit

All COVERED entries below mean specified by executable tasks, not implemented or empirically passing.

| Source | ID | Required result | Plans | Status |
|---|---|---|---|---|
| GOAL | Phase 1 | Headless real hotkey to microphone/spool to local pt-BR to safe clipboard/paste | 01-13 | COVERED |
| GOAL | SC1 | Actual published Win10 worker gate and full isolated matrix, measured selection | 01,08,13 | COVERED |
| GOAL | SC2 | Tap/hold into real Notepad without leaked hotkey | 03,10,12 | COVERED |
| GOAL | SC3 | Short/silence/noise pastes nothing | 05,12 | COVERED |
| GOAL | SC4 | Worker CPU retry and App crash spool recovery | 06,07,11,12 | COVERED |
| GOAL | SC5 | Safe target deferral and private full timing evidence | 04,09,13 | COVERED |
| REQ | HOTK-01 | Suppressed global chord | 03,10,12,13 | COVERED |
| REQ | HOTK-02 | Tap toggle | 03,09,10,12 | COVERED |
| REQ | HOTK-03 | Hold/release and repeats | 03,09,10,12 | COVERED |
| REQ | AUD-01 | Native device capture converted to 16 kHz mono | 06,10,13 | COVERED |
| REQ | AUD-04 | Durable audio and crash recovery | 06,09,11,12 | COVERED |
| REQ | STT-01 | Local pt-BR punctuation/capitalization and clean WER | 01,05,12 | COVERED |
| REQ | STT-02 | Vulkan and CPU restart boundary | 01,07,12 | COVERED |
| REQ | STT-03 | Duration/VAD/whole-output post-filter | 05,12 | COVERED |
| REQ | STT-04 | Preload/warm and early hotkey retained | 02,07,09,10,11 | COVERED |
| REQ | STT-08 | Measured warm stop-to-paste budget | 08,12,13 | COVERED |
| REQ | OUT-01 | Clipboard text retained | 02,04,12,13 | COVERED |
| REQ | OUT-02 | Original target after physical modifier release | 04,10,12 | COVERED |
| REQ | OUT-03 | Clipboard/manual-paste outcome on unsafe target | 04,13 | COVERED; overlay notice is Phase 2 |
| REQ | APP-06 | Complete structured timings, privacy/redaction | 09,10,13 | COVERED |

## Locked decision audit

| Source | ID | Decision | Plans | Status |
|---|---|---|---|---|
| CONTEXT | D-01 | .NET project boundaries and pure Core | 01,02,09 | COVERED |
| CONTEXT | D-02 | Exact aligned package pins | 01 | COVERED |
| CONTEXT | D-03 | Separate worker, JSONL, spool paths | 01,02 | COVERED |
| CONTEXT | D-04 | CPU restart, once retry, backoff/cap | 07,12 | COVERED |
| CONTEXT | D-05 | Warm ready and queued early dictation | 07,09,10,11 | COVERED |
| CONTEXT | D-06 | pt/threads/conditioning/temperature/prompt | 01,08 | COVERED |
| CONTEXT | D-07 | Minimal hook suppression and pure timing | 03 | COVERED |
| CONTEXT | D-08 | 300 ms semantics, amended modifier/activity watchdog, max length | 03,12 | COVERED |
| CONTEXT | D-09 | Own input marker ignored, test marker processed | 01,03,12 | COVERED |
| CONTEXT | D-10 | Capture starts on press, no idle mic, untrimmed durable spool | 06,10,12 | COVERED |
| CONTEXT | D-11 | Sidecar/recovery/keep-20, saved text and safe clipboard outcome | 06,09,11,12 | COVERED |
| CONTEXT | D-12 | Ordered duration/VAD/full-audio/blocklist/no_speech gate | 05,12 | COVERED; spoken Obrigado alone remains discarded by explicit lock |
| CONTEXT | D-13 | Stop target, focus/IL/modifier gates, atomic tagged paste | 04,10,13 | COVERED |
| CONTEXT | D-14 | No clipboard restore | 02,04,12,13 | COVERED |
| CONTEXT | D-15 | Scripted pinned models and SHA-256 | 01,08 | COVERED |
| CONTEXT | D-16 | Recheck real published Win10 worker, stop on failure | 01,13 | COVERED |
| CONTEXT | D-17 | Full model/backend matrix, >=10 samples, quality/budget winner | 08,12,13 | COVERED |
| CONTEXT | D-18 | pt-BR TTS fixtures and SAPI fallback | 05 | COVERED |
| CONTEXT | D-19 | File-backed harness and optional subjective mic check | 06,10,13 | COVERED |
| CONTEXT | D-20 | Test-only current-user pipe, Release excludes harness | 10,11,13 | COVERED |
| CONTEXT | D-21 | Real-hook Notepad acceptance and desktop guard | 12,13 | COVERED |
| CONTEXT | D-22 | Clean synthetic fixture WER <=10% | 05,12 | COVERED |
| CONTEXT | D-23 | Rolling private logs and authoritative metrics schema | 09,13 | COVERED |

## Research constraint audit

| Source | Area | Adopted constraint | Plans | Status |
|---|---|---|---|---|
| RESEARCH | Responsibility map / stack | Core purity, adapters, pinned .NET/Whisper/NAudio/CsWin32/MTP | 01,02,09,10 | COVERED |
| RESEARCH | Package audit | No new unreviewed edge-tts install; reuse spike binary or SAPI | 01,05 | COVERED |
| RESEARCH | Runtime/native gate | One backend per process, sanitized published gate, FA probe distinction | 01,07,08,13 | COVERED |
| RESEARCH | IPC | Separate responsive control/inference, bounded JSON, cancellation/shutdown | 01,07 | COVERED |
| RESEARCH | Speech | Actual CPU VAD builder option, full untrimmed audio, normalized whole-output filter | 05 | COVERED |
| RESEARCH | Models | Pinned HF URLs/hashes, all variants fetched before matrix completion | 01,08 | COVERED |
| RESEARCH | Durability | Writer+stream ownership, flush/header repair, staged atomic sidecars | 06,09,11 | COVERED |
| RESEARCH | Input/paste | LL callback budget, foreground/focus/IL, physical modifiers, layout assumption test | 03,04,12,13 | COVERED |
| RESEARCH | Measurement | Isolated real-paste matrix, >=10 runs, measured selection and live-App recheck | 08,12,13 | COVERED |
| RESEARCH | Validation | MTP filters, strict required guards, real-device smoke, per-test data/process cleanup | 01,06,12,13 | COVERED |
| RESEARCH | Security domain | User pipe ACL, strict contained paths/reparse rejection, model integrity, redaction | 01,04,09,11,13 | COVERED |
| RESEARCH | Open questions | Defaults decided empirically; retain locked no_speech threshold; automated WASAPI plus subjective optional mic check | 05,06,08,13 | COVERED |

Phase-2 UI/settings/cancel, Phase-3 cloud, Phase-4 installer/soak and CONTEXT deferred ideas remain excluded by source scope.

## All 22 review dispositions

| Review | Disposition | Binding correction / acceptance |
|---|---|---|
| H1 | Addressed | Explicit installed Git Bash for bodies; PS5.1 scripts propagate native exits. All 32 bodies syntax checked. |
| H2 | Addressed | Core owns engine domain DTOs/status/ports; Core boundary tests in 02. |
| H3 | Addressed | Amended modifier/activity watchdog; >3 s real-hook test; lost-up repeat stimulus reconciled in 03. |
| H4 | Addressed | Re-probe focused control and foreground; 04 unit/integration plus 13 deterministic same-window E2E. |
| H5 | Addressed | Explicit disposal ownership, constructor cleanup, exclusive reopen/delete in 06. |
| H6 | Addressed | Persist transcribed text before output; clipboard failure/retryable errors retain recoverable stages; saved text restart test in 11. |
| H7 | Addressed | Sequential execution/verification and isolated benchmark; no same-wave source collisions. |
| H8 | Addressed | Per-id records, separate capture/pending/inflight state, one owner loop, ordered persistence and fault/overlap tests in 09/10. |
| H9 | Addressed | Hook/capture before readiness; pre-hook paths-only snapshot; reducer admission delays dispatch only; two early captures during delayed recovery and before-ready E2E in 09/11. |
| H10 | Addressed | Seq-cursor replay from process start and replayable recovery_complete in 11. |
| M1 | Addressed | VAD processor builder explicitly disables GPU in 05. |
| M2 | Addressed | Finite unpaced file source, paced padding separated, Completion/FramesWritten in 06. |
| M3 | Addressed | Responsive control reader/cancel and bounded shutdown in 01/07. |
| M4 | Addressed | Separator-aware contained canonical paths with sibling/traversal/reparse tests in 01. |
| M5 | Partially accepted; locked tradeoff retained | Sim/Ok/Nao/E ai survive; exact whole-output Obrigado remains rejected per D-12, documented fixture in 05/12. Changing the lock is outside this revision. |
| M6 | Addressed | One normalization for entries and outputs, exact whole-output matching and containing-sentence tests in 05. |
| M7 | Addressed | Keep-20-total including recovered; retention after recovery outcomes, protecting pending IDs in 06/11. |
| M8 | Addressed | RequestWritten plus slow worker for kill; armed asynchronous delivery barrier for focus; metrics method arranges its own dictation in 07/11/12/13. |
| M9 | Addressed | Core result session load/warm, monotonic event stamps, authoritative FieldNames and E2E assertions in 02/09/10/13. |
| M10 | Addressed | Final verifier explicitly runs bounded real WasapiSmokeTests; only no-device skip permitted in 06/13. |
| M11 | Addressed | Per-capture fault signal, first-data deadline, finalized partial spool and one error outcome in 06/09/10. |
| M12 | Addressed | Fetch CSV contract and PS5.1-compatible ProcessStartInfo/UTF8/native exit checks in 01/13. |

## Original checker blockers and residual limits

The four original blockers are covered: PTT watchdog (03), early hotkey before ready (07/10/11), full load/warm timing schema (02/09/13), and original 01-01 scope split into worker 01 and App 02. Original wide plans were split into fixture/benchmark (05/08), reducer/App/recovery (09/10/11), and restored acceptance/final gate (12/13). Benchmark selection checks complete measurements and computed budget/quality winner, not a heading or missing-model placeholder. Research open questions now name adopted plans and preserve D-12.

Remaining execution limits are explicit: optional real elevated-target proof depends on obtaining a lower-IL App/high-IL target without interactive authentication; missing real capture device permits only the mic smoke skip; subjective microphone clipping remains optional. Neither budget success nor final SC1-SC5 PASS is claimed until actual verifier execution. Several inherited tasks span more than five files; structural validators do not enforce context sizing. Executors should preserve their stated contracts and commit bounded task slices rather than silently dropping scope.
