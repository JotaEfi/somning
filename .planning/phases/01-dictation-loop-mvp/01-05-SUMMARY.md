---
phase: 01-dictation-loop-mvp
plan: "05"
subsystem: transcription
tags: [windows, whisper, silero-vad, pt-br, postfilter, fixtures]
requires:
  - phase: 01-02
    provides: "Engine worker protocol, inference host and CPU model test support"
provides:
  - "Speech and non-speech pt-BR audio fixtures with validated manifest"
  - "CPU Silero VAD and duration gate before transcription"
  - "Whole-output hallucination filter and fixture accuracy coverage"
affects: [01-08, 01-12, 01-13]
actuals:
  tokens: 8156
  tasks: 2
  commits: 2
  plan_head_before: c7ab642d9b96b7c0dca99df4e3127a454e139a58
  plan_head_after: d0eb8ef648c1a64d59c5ebf67c9b5f9aab09f623
tech-stack:
  added: []
  patterns: ["CPU-only Silero VAD", "single normalized whole-output filter", "manifest-validated integration fixtures"]
key-files:
  created:
    - tools/make-fixtures/make_fixtures.ps1
    - tools/make-fixtures/requirements.txt
    - tools/make-fixtures/README.md
    - tests/fixtures/manifest.json
    - src/Somning.Core/Text/PostFilter.cs
    - src/Somning.Core/Text/HallucinationBlocklist.cs
    - src/Somning.Engine/Speech/VadGate.cs
    - src/Somning.Engine/Speech/SpeechGate.cs
    - tests/Somning.Core.Tests/Text/PostFilterTests.cs
    - tests/Somning.Integration.Tests/Engine/FixtureAccuracyTests.cs
    - tests/Somning.Integration.Tests/Engine/SilenceGateTests.cs
  modified:
    - src/Somning.Engine/WhisperHost.cs
    - src/Somning.Engine/InferenceExecutor.cs
    - tests/Somning.Integration.Tests/Engine/FixtureManifest.cs
    - tests/Somning.Integration.Tests/Engine/EngineIntegrationCollection.cs
key-decisions:
  - "Keep VAD on CPU and use it only to gate; accepted clips are transcribed in full without trimming."
  - "Normalize blocklist entries and output once, and match only whole normalized output."
  - "Use Edge-TTS at -10% rate for the committed speech fixtures; preserve a documented SAPI fallback without running package installation."
  - "Serialize engine-backed fixture test classes so they do not load multiple large CPU models concurrently."
requirements-completed: [STT-01, STT-03]
coverage:
  - id: D1
    description: "Locked clean and noisy/quiet speech fixtures meet transcription quality expectations."
    requirement: STT-01
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Engine/FixtureAccuracyTests.cs#Clean_fixtures_meet_quality_target"
        status: pass
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Engine/FixtureAccuracyTests.cs#Non_clean_speech_remains_usable"
        status: pass
    human_judgment: false
  - id: D2
    description: "Duration and VAD gates reject non-speech; legitimate short utterances still produce text."
    requirement: STT-03
    verification:
      - kind: integration
        ref: "tests/Somning.Integration.Tests/Engine/SilenceGateTests.cs"
        status: pass
    human_judgment: false
  - id: D3
    description: "Post-filter uses one normalized representation and applies whole-output blocklist, no-letter and no-speech rules."
    requirement: STT-03
    verification:
      - kind: unit
        ref: "tests/Somning.Core.Tests/Text/PostFilterTests.cs"
        status: pass
    human_judgment: false
metrics:
  duration: "about 1 hour"
  completed: 2026-10-23
  status: complete
---

# Phase 1 Plan 5: Transcription Quality Summary

**Portuguese speech fixtures, CPU Silero gating and whole-output filtering now protect the transcription path and have strict engine coverage.**

## Performance

- **Duration:** About 1 hour; the exact start timestamp was not captured.
- **Completed:** 2026-10-23
- **Tasks:** 2
- **Files modified or created:** 27 across the two task commits

## Accomplishments

- Added 13 16 kHz mono PCM WAV fixtures and a manifest covering clean, noisy, quiet, short, blocklisted and non-speech cases. The fixture directory totals 1,981,970 bytes, below the 5 MB limit.
- Added a duration-first gate and CPU Silero VAD. VAD rejects without trimming accepted audio; post-transcription filtering prevents empty or blocklisted text from reaching results.
- Added manifest shape/count validation, whole-output PostFilter tests and real CPU engine accuracy and silence tests.

## Task Commits

1. **Task 1: Fixture set and engine speech gate** - `e5a58e9` (feat)
2. **Task 2: PostFilter and transcription accuracy tests** - `d0eb8ef` (test)

**Plan metadata:** To be recorded by the phase orchestrator.

## Files Created/Modified

- `tools/make-fixtures/` - Reproducible Edge-TTS/SAPI and ffmpeg fixture generation; no package install is performed.
- `tests/fixtures/` - Speech, non-speech and existing tracer inputs with explicit expected outcomes and generator metadata.
- `src/Somning.Core/Text/` - Shared text normalization and whole-output hallucination filtering.
- `src/Somning.Engine/Speech/` - Duration/VAD gate and CPU VAD implementation.
- `src/Somning.Engine/InferenceExecutor.cs`, `src/Somning.Engine/WhisperHost.cs` - Apply the gate before and after transcription and manage VAD lifetime.
- `tests/Somning.Core.Tests/Text/PostFilterTests.cs` and `tests/Somning.Integration.Tests/Engine/` - Filter, manifest, accuracy and silence-gate coverage; engine-backed test classes share a non-parallel collection.

## Decisions Made

- VAD runs with `WithUseGpu(false)` and gates only; it never removes audio segments before Whisper sees the input.
- The prompt was verified from the source as UTF-8: `Olá, tudo bem? Vamos começar a reunião às 10h.` (including `á` U+00E1, `ç` U+00E7, `ã` U+00E3 and `à` U+00E0). `WhisperHost` continues to pass `EngineDefaults.InitialPrompt`; no prompt change was needed.
- Whole-output matching deliberately drops an utterance consisting only of `Obrigado.`, while prefix and substring matches remain eligible for output.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed PowerShell 5.1 UTF-8 fixture generation**
- **Found during:** Task 1 accuracy investigation.
- **Issue:** The BOM-less generator was parsed as ANSI by Windows PowerShell, so Portuguese text was synthesized as mojibake and caused high WER.
- **Fix:** Saved the script with a UTF-8 BOM and explicitly read the existing tracer text as UTF-8. The regenerated manifest now contains the intended accented codepoints.
- **Files modified:** `tools/make-fixtures/make_fixtures.ps1`, `tests/fixtures/manifest.json`, speech fixtures.
- **Verification:** Corrected strict accuracy tests passed for all clean and non-clean fixture rows.
- **Committed in:** `e5a58e9`.

**2. [Rule 2 - Missing Critical] Prevented vacuous silence tests and concurrent model loads**
- **Found during:** Task 1 review.
- **Issue:** Case-insensitive JSON parsing alone did not map snake_case manifest keys, which could leave expected outcomes null and silently select zero empty fixtures. Independent test classes could also load multiple large CPU models concurrently.
- **Fix:** Added explicit JSON property names and manifest count/required-field/empty-fixture validation; placed both fixture test classes in a non-parallel xUnit collection.
- **Files modified:** `tests/Somning.Integration.Tests/Engine/FixtureManifest.cs`, `SilenceGateTests.cs`, `FixtureAccuracyTests.cs`, `EngineIntegrationCollection.cs`.
- **Verification:** Strict gate tests passed and exercised all five empty fixtures; both final code reviews approved.
- **Committed in:** `e5a58e9`, `d0eb8ef`.

**3. [Plan ordering - TDD] Task 1 already included the PostFilter implementation**
- **Found during:** Task 2.
- **Issue:** Task 2 is marked TDD, but Task 1 explicitly creates the complete PostFilter before Task 2 adds its tests. The PostFilter tests therefore passed on first execution; a RED commit would require undoing already committed Task 1 behavior.
- **Resolution:** Kept the planned task boundary and separate test commit. No failing test or implementation change was fabricated; all 24 PostFilter assertions passed.
- **Files modified:** `tests/Somning.Core.Tests/Text/PostFilterTests.cs`, `tests/Somning.Integration.Tests/Engine/FixtureAccuracyTests.cs`.
- **Committed in:** `d0eb8ef`.

**Total deviations:** 3 (2 automatic correctness fixes and 1 plan-ordering adjustment). The plan's engine behavior and acceptance thresholds remain unchanged.

## Executed checks

- `dotnet test --project tests/Somning.Core.Tests --filter-class "*.PostFilterTests"` - passed, 24/24.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter-class "*.FixtureAccuracyTests"` - passed, 2/2; checks all clean and non-clean speech rows on the local CPU model.
- `SOMNING_STRICT=1 dotnet test --project tests/Somning.Integration.Tests --filter-class "*.SilenceGateTests"` - passed, 4/4, 0 skipped; exercises all five expected-empty fixtures and three speech cases.
- Engine and Integration project builds - passed, 0 warnings and 0 errors.
- Fixture size/count checks - passed, 1,981,970 bytes across 13 WAV files.
- `git diff --check` - passed.
- C# and generic final reviews - approved with no remaining findings.

## Issues Encountered

The initial accuracy failure came from corrupted synthesis input rather than an engine/model regression. After correcting PowerShell's source decoding, strict accuracy passed without changing the WER threshold or expected speech text. The default prompt literal was separately verified as correctly encoded, so it remains unchanged.

## User Setup Required

None. The generator uses the pre-existing Edge-TTS executable or built-in SAPI and does not install packages. `requirements.txt` pins Edge-TTS 7.3.1 for reproducibility; the README requires human legitimacy verification before anyone installs it.

## Next Phase Readiness

The CPU transcription-quality and silence-gate slice is ready for downstream integration and end-to-end proof. The existing prompt and locked accuracy thresholds remain unchanged.

## Self-Check: PASSED

- Both task commits exist and are ancestors of the current branch head.
- All implementation and fixture files listed above are present.
- The summary is the only plan documentation file created by this task; `.planning/STATE.md`, ROADMAP and configuration remain untouched for the orchestrator.

---
*Phase: 01-dictation-loop-mvp*
*Plan: 05*
*Completed: 2026-10-23*
