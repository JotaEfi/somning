Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$phase = Join-Path $root '.planning\phases\01-dictation-loop-mvp'
New-Item -ItemType Directory -Path $phase -Force | Out-Null
$reportPath = Join-Path $phase 'E2E-RESULT.md'
$benchResults = Join-Path $phase 'bench-results.jsonl'
$benchReport = Join-Path $phase 'BENCHMARK.md'
$latencyPath = Join-Path $phase '01-12-latency.json'
$oldStrict = [Environment]::GetEnvironmentVariable('SOMNING_STRICT', 'Process')
$runId = [guid]::NewGuid().ToString('N')
$resultsDir = Join-Path $root ('TestResults\phase1-' + $runId)
$steps = New-Object 'Collections.Generic.List[object]'
$allowedSkips = New-Object 'Collections.Generic.List[string]'
$failure = $null
$latency = $null
$revision = $null
$os = $null
$fixtureProvenance = @()
$global:LASTEXITCODE = 0

function Add-Step([string]$Name, [string]$Status, [string]$Detail) {
    $script:steps.Add([pscustomobject]@{ Name=$Name; Status=$Status; Detail=$Detail })
}

function Invoke-Checked([string]$Name, [string]$Executable, [string[]]$Arguments) {
    Write-Host "`n=== $Name ==="
    & $Executable @Arguments
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Add-Step $Name 'FAIL' "exit $code"
        throw "$Name failed with native exit code $code"
    }
    Add-Step $Name 'PASS' 'exit 0'
}

function Invoke-Test([string]$Project, [string]$Class, [string]$Name) {
    $xmlName = ($Name -replace '[^A-Za-z0-9_.-]', '_') + '.xml'
    $args = @('test', '--project', $Project, '--filter', "FullyQualifiedName~$Class",
        '--report-xunit-xml', '--report-xunit-xml-filename', $xmlName, '--results-directory', $resultsDir)
    Invoke-Checked $Name 'dotnet' $args
    $xmlPath = Join-Path $resultsDir $xmlName
    if (-not (Test-Path -LiteralPath $xmlPath)) { throw "$Name produced no structured xUnit XML; filter/report cannot be verified" }
    [xml]$xml = [IO.File]::ReadAllText($xmlPath)
    $assembly = $xml.assemblies.assembly
    if ($null -eq $assembly -or [int]$assembly.total -eq 0) { throw "$Name selected zero tests (empty MTP filter)" }
    $skips = @($assembly.SelectNodes('.//test[@result="Skip"]'))
    foreach ($skip in $skips) {
        $skipName = [string]$skip.name
        $reason = [string]$skip.reason
        if ($Class -eq 'WasapiSmokeTests' -and $reason -match 'no capture device') {
            $allowedSkips.Add("$Name`: $reason")
        } elseif ($Class -eq 'PasteDeferredE2ETests' -and $skipName -match 'Elevated_target_defers' -and $reason -match 'No noninteractive low-integrity App/high-integrity target pair') {
            $allowedSkips.Add("$Name`: optional UIPI environment: $reason")
        } else {
            throw "$Name had an unapproved skip: $skipName ($reason)"
        }
    }
    $detail = "total=$($assembly.total); passed=$($assembly.passed); skipped=$($assembly.skipped); failed=$($assembly.failed)"
    # Replace exit-only step detail with machine-readable reporter counts.
    $script:steps[$script:steps.Count - 1] = [pscustomobject]@{ Name=$Name; Status='PASS'; Detail=$detail }
}

function Get-LatencyEvidence {
    if (-not (Test-Path -LiteralPath $latencyPath)) { throw '01-12-latency.json missing after DictationE2ETests' }
    $data = Get-Content -LiteralPath $latencyPath -Raw | ConvertFrom-Json
    $samples = @($data.samples_ms | ForEach-Object { [long]$_ })
    if ($samples.Count -lt 10) { throw "Warm latency sample count $($samples.Count) is less than 10" }
    $sorted = @($samples | Sort-Object)
    $p50 = $sorted[[Math]::Max(0, [int][Math]::Ceiling(.50 * $sorted.Count) - 1)]
    $p95 = $sorted[[Math]::Max(0, [int][Math]::Ceiling(.95 * $sorted.Count) - 1)]
    if ($data.backend -ne 'vulkan' -or $data.selected -ne 'vulkan-ggml-large-v3-turbo-q5_0.bin-fa=True') {
        throw "Latency evidence does not match selected EngineDefaults: $($data.selected) / $($data.backend)"
    }
    if ($p95 -gt 3000 -or $data.budget_ms -ne 3000) { throw "Warm stop-to-paste p95 $p95 ms exceeds the 3000 ms budget" }
    return [pscustomobject]@{ Samples=$samples; P50=$p50; P95=$p95; Budget=$data.budget_ms; Backend=$data.backend; Selected=$data.selected; Timestamp=$data.measured_at_utc }
}

try {
    Push-Location $root
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
    [Environment]::SetEnvironmentVariable('SOMNING_STRICT', '1', 'Process')
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not read current Git revision' }
    $os = Get-CimInstance Win32_OperatingSystem
    $version = [Environment]::OSVersion.Version.ToString()

    Invoke-Checked 'Debug build' 'dotnet' @('build', 'Somning.sln', '-c', 'Debug')
    Invoke-Test 'tests/Somning.Core.Tests' 'Somning.Core.Tests' 'Core tests'

    $classes = @(
        'EngineWorkerTests','SkeletonRoundTripTests','ClipboardWriterTests','ProtocolMappingTests',
        'KeyboardHookTests','HoldWatchdogTests','TargetPasterTests','IntegrityReaderTests','PasteInjectorTests',
        'SilenceGateTests','FixtureAccuracyTests','SpoolPipelineTests','CaptureConverterTests','AudioFaultTests',
        'WasapiSmokeTests','SidecarStoreTests','SpoolDurabilityTests','EngineSupervisorTests','MetricsLogTests',
        'EffectRunnerTests','HarnessDictationTests','HarnessPipeTests','RecoveryServiceTests','StartupBeforeReadyTests',
        'DictationE2ETests','SilenceE2ETests','ResilienceE2ETests','PasteDeferredE2ETests'
    )
    foreach ($class in $classes) { Invoke-Test 'tests/Somning.Integration.Tests' $class $class }

    # Strict prerequisites apply to tests. Restore the caller's flag before launching standalone published gates.
    [Environment]::SetEnvironmentVariable('SOMNING_STRICT', $oldStrict, 'Process')
    Invoke-Checked 'Engine published gate' 'powershell' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tools/gate.ps1')
    Invoke-Checked 'Published App gate' 'powershell' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tools/gate.ps1','-App','-SkipEngine')
    Invoke-Checked 'Benchmark consistency gate' 'dotnet' @('run','-c','Release','--project','tools/Somning.Bench','--','check','--results',$benchResults,'--report',$benchReport)

    $latency = Get-LatencyEvidence
    $benchRecords = @(Get-Content -LiteralPath $benchResults | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    if ($benchRecords.Count -ne 12) { throw "Benchmark evidence has $($benchRecords.Count) config records; expected all 12" }
    $fixtureManifest = Get-Content -LiteralPath (Join-Path $root 'tests\fixtures\manifest.json') -Raw | ConvertFrom-Json
    $fixtureRow = @($fixtureManifest | Where-Object { $_.file -eq 'tracer/hoje.wav' }) | Select-Object -First 1
    if ($fixtureRow) { $fixtureProvenance = @("generator=$($fixtureRow.generator)", "voice=$($fixtureRow.voice)", $fixtureRow.note) }
    $status = 'PASS'
} catch {
    $failure = $_.Exception.Message
    if (@($steps | Where-Object Status -eq 'FAIL').Count -gt 0) { $status = 'FAIL' } else { $status = 'BLOCKED' }
    Write-Error $failure
} finally {
    [Environment]::SetEnvironmentVariable('SOMNING_STRICT', $oldStrict, 'Process')
    if ((Get-Location).Path -eq $root) { Pop-Location }

    $lineRows = @($steps | ForEach-Object { "| $($_.Name) | $($_.Status) | $($_.Detail.Replace('|','/')) |" })
    if ($lineRows.Count -eq 0) { $lineRows = @('| No checks completed | BLOCKED | verifier did not start checks |') }
    $skipRows = @($allowedSkips | ForEach-Object { '- ' + $_ })
    if ($skipRows.Count -eq 0) { $skipRows = @('- None.') }
    $revisionText = if ($revision) { $revision } else { 'unavailable' }
    $machineText = if ($os) { "$($os.Caption), build $($os.BuildNumber), revision $version" } else { 'unavailable' }
    $latencyBlock = if ($latency) {
        @('', '### Warm stop-to-paste samples', '', ('Selected: `' + $latency.Selected + '`; backend `' + $latency.Backend + '`; budget ' + $latency.Budget + ' ms.'),
          ('Raw samples (ms): `' + ($latency.Samples -join ', ') + '`.'),
          ('Measured p50: **' + $latency.P50 + ' ms**; p95: **' + $latency.P95 + ' ms**; measured at ' + $latency.Timestamp + '.'))
    } else { @('', '### Warm stop-to-paste samples', '', 'BLOCKED: latency artifact was not produced or did not meet selected-default/budget checks.') }
    $failureBlock = if ($failure) { @('', '### Blocker', '', $failure) } else { @() }
    $scStatus = if ($status -eq 'PASS') { 'PASS' } elseif ($status -eq 'FAIL') { 'FAIL (verification incomplete)' } else { 'BLOCKED' }
    $report = @(
        '# Phase 1 acceptance evidence', '',
        ('Execution status: **' + $status + '**.'),
        ('Machine: ' + $machineText + '.'),
        ('Git revision at verifier start: `' + $revisionText + '`. ' + 'Verifier ran sequentially under PowerShell 5.1; `SOMNING_STRICT` was restored at exit.'), '',
        '## SC1-SC5 acceptance mapping', '',
        '| Criterion | Status | Actual evidence |', '|---|---|---|',
        "| SC1 | $scStatus | BENCHMARK.md + 12-config `bench-results.jsonl` checked against EngineDefaults; engine and published App gates. |",
        "| SC2 | $scStatus | DictationE2ETests: real App hotkey, Notepad, clipboard and layout guard; warm samples below. |",
        "| SC3 | $scStatus | SilenceE2ETests: non-speech and short-input clipboard preservation. |",
        "| SC4 | $scStatus | ResilienceE2ETests: worker failure/fallback and App recovery. |",
        "| SC5 | $scStatus | PasteDeferredE2ETests + PasteGateTests + MetricsLogTests; foreground/focus defer, clipboard retention and private metrics schema. |", '',
        '## Sequential checks', '', '| Check | Status | Structured result |', '|---|---|---|'
    ) + $lineRows + @('',
        '## Real WASAPI and desktop limitations', '',
        'WasapiSmokeTests exercises the real default capture device for a bounded one-second capture without speech. Any skip is accepted only for the exact `no capture device` condition.',
        'The optional empirical UIPI test requires a provisioned low-integrity App/high-integrity target pair. PasteGateTests validates policy paths but is not empirical UIPI proof.', '',
        'Allowed environment skips:', '') + $skipRows + @('',
        '## Fixture provenance', '',
        ('Manifest provenance: ' + ($fixtureProvenance -join '; ') + '.'),
        'Benchmark measurements are the committed isolated run matrix (minimum ten runs/config); the App E2E latency samples are generated by this verifier run.'
    ) + $latencyBlock + $failureBlock
    [IO.File]::WriteAllLines($reportPath, [string[]]$report, (New-Object Text.UTF8Encoding($false)))
    if ($resultsDir -and (Test-Path -LiteralPath $resultsDir)) { Remove-Item -LiteralPath $resultsDir -Recurse -Force }
}

if ($status -ne 'PASS') { exit 1 }
Write-Host "Phase 1 verification passed. Evidence: $reportPath"
exit 0
