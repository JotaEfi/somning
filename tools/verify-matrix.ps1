param(
    [string]$PackageDir = (Join-Path $PSScriptRoot '..\publish\package'),
    [string]$ModelsDir = (Join-Path $env:LOCALAPPDATA 'Somning\models'),
    [switch]$SkipE2E,
    [switch]$ConfirmInteractive,
    [switch]$SelfCheck,
    [int]$SoakIterations = 50
)

# Generates .planning\phases\04-packaging-autostart-hardening\PASTE-MATRIX.md from REAL results only.
# Every row is derived from an executed check; nothing is hardcoded as PASS. Rows that cannot be verified
# without a human or a privileged desktop are reported as SKIPPED or MANUAL, never PASS.
#
# This script drives the REAL desktop (keyboard, clipboard, windows, Notepad, browsers). It refuses to run
# without -ConfirmInteractive. -SelfCheck is the only non-interactive mode: it sends no input and writes no report.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$utf8 = New-Object Text.UTF8Encoding($false)
$phaseDir = Join-Path $root '.planning\phases\04-packaging-autostart-hardening'
$reportPath = Join-Path $phaseDir 'PASTE-MATRIX.md'
$soakReportPath = Join-Path $phaseDir 'SOAK-REPORT.json'
$integrationExe = Join-Path $root 'tests\Somning.Integration.Tests\bin\Debug\net10.0-windows\Somning.Integration.Tests.exe'
$coreExe = Join-Path $root 'tests\Somning.Core.Tests\bin\Debug\net10.0\Somning.Core.Tests.exe'
$PackageDir = [IO.Path]::GetFullPath($PackageDir)

# Every row runs exactly once so the report reflects first-run results (no retries, nothing hidden).
$EvidenceAttempts = 1

# Mirrors tests/Somning.Integration.Tests/Paste/PasteTargetCatalog.cs (App Paths registry, then fallback locations).
function Find-App([string]$Id) {
    $pf = [Environment]::GetFolderPath('ProgramFiles'); $pf86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $lad = [Environment]::GetFolderPath('LocalApplicationData')
    $map = @{
        chrome = @{ Exe = 'chrome.exe'; Paths = @("$pf\Google\Chrome\Application\chrome.exe", "$pf86\Google\Chrome\Application\chrome.exe", "$lad\Google\Chrome\Application\chrome.exe") }
        edge   = @{ Exe = 'msedge.exe'; Paths = @("$pf86\Microsoft\Edge\Application\msedge.exe", "$pf\Microsoft\Edge\Application\msedge.exe") }
        vscode = @{ Exe = 'Code.exe'; Paths = @("$lad\Programs\Microsoft VS Code\Code.exe", "$pf\Microsoft VS Code\Code.exe") }
        word   = @{ Exe = 'winword.exe'; Paths = @("$pf\Microsoft Office\root\Office16\WINWORD.EXE", "$pf86\Microsoft Office\root\Office16\WINWORD.EXE", "$pf\Microsoft Office\Office16\WINWORD.EXE") }
    }
    $entry = $map[$Id]
    foreach ($hive in 'HKLM:', 'HKCU:') {
        $key = Join-Path $hive ('SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\' + $entry.Exe)
        $p = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue)
        $prop = if ($p) { $p.PSObject.Properties['(default)'] } else { $null }
        if ($prop -and $prop.Value) {
            $candidate = ([string]$prop.Value).Trim('"')
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    return ($entry.Paths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

if ($SelfCheck) {
    Write-Host '=== verify-matrix self-check (non-interactive; no input is sent, no report is written) ===' -ForegroundColor Cyan
    $problems = @()
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($MyInvocation.MyCommand.Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) { $problems += ($parseErrors | ForEach-Object { 'parse error: ' + $_.Message }) }
    else { Write-Host 'Script parses cleanly.' }
    foreach ($item in @(@('integration test exe', $integrationExe), @('core test exe', $coreExe),
                        @('package.ps1 (harness package script)', (Join-Path $PSScriptRoot 'package.ps1')),
                        @('install.ps1', (Join-Path $PSScriptRoot 'install.ps1')))) {
        if (Test-Path -LiteralPath $item[1]) { Write-Host ("Found {0}" -f $item[0]) } else { $problems += ("missing {0}: {1}" -f $item[0], $item[1]) }
    }
    foreach ($id in 'chrome', 'edge', 'vscode', 'word') {
        $path = Find-App $id
        if ($path) { Write-Host ("App {0}: installed ({1})" -f $id, $path) } else { Write-Host ("App {0}: not installed" -f $id) }
    }
    if ($problems) { $problems | ForEach-Object { Write-Host "SELF-CHECK PROBLEM: $_" -ForegroundColor Red }; exit 1 }
    Write-Host 'Self-check OK.' -ForegroundColor Green
    exit 0
}

if (-not $ConfirmInteractive) {
    Write-Host 'tools/verify-matrix.ps1 drives the REAL desktop: it sends keyboard input, replaces the clipboard, opens and closes Notepad,' -ForegroundColor Yellow
    Write-Host 'browsers, VS Code and Word windows, and installs/uninstalls a temporary copy of the app. Do not run it while the machine is in use.' -ForegroundColor Yellow
    Write-Host 'Re-run with -ConfirmInteractive to proceed, or with -SelfCheck for a non-interactive check.' -ForegroundColor Yellow
    exit 3
}

# Refuse to start when another verify-matrix.ps1 is already running: two runs would fight over the desktop, the clipboard,
# the hook and the installed copies (a hung earlier run is a zombie that must be killed first).
$otherRuns = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessId -ne $PID -and $_.Name -match '^(powershell|pwsh)\.exe$' -and $_.CommandLine -and
    $_.CommandLine -match 'verify-matrix\.ps1' -and $_.CommandLine -notmatch '-SelfCheck' })
if ($otherRuns.Count -gt 0) {
    Write-Host ('Another verify-matrix.ps1 is already running (PID ' + (($otherRuns | ForEach-Object { $_.ProcessId }) -join ', ') + '); refusing to start. Stop it first.') -ForegroundColor Red
    exit 4
}

# The named mutex is the real single-instance protection (the process enumeration above is only a diagnostic and is racy).
# It is held for the whole run and released in the final finally.
$createdNew = $false
$runMutex = New-Object Threading.Mutex($false, 'Global\Somning.VerifyMatrix.Run', [ref]$createdNew)
$mutexAcquired = $false
try { $mutexAcquired = $runMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $mutexAcquired = $true }
if (-not $mutexAcquired) {
    Write-Host 'Another verify-matrix.ps1 run holds the Global\Somning.VerifyMatrix.Run mutex; refusing to start.' -ForegroundColor Red
    exit 4
}

# Never touch the owner's own Somning: the matrix installs and kills only copies it started itself. If the owner's app is running
# (it holds the harness pipe name, the hook and the clipboard-driven tests would collide with it), refuse to start at all.
$userSomning = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -in 'Somning.App.exe', 'Somning.Engine.exe' })
if ($userSomning.Count -gt 0) {
    Write-Host ('A Somning.App/Somning.Engine instance is already running (PID ' + (($userSomning | ForEach-Object { $_.ProcessId }) -join ', ') + '); refusing to start. Quit it first.') -ForegroundColor Red
    $runMutex.ReleaseMutex(); exit 5
}

$results = New-Object 'Collections.Generic.List[object]'

function Add-Result([string]$Category, [string]$Scenario, [string]$Method, [string]$Status, [string]$Evidence) {
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0}] {1} / {2}: {3}" -f $Status, $Category, $Scenario, $Evidence) -ForegroundColor $color
    $script:results.Add([pscustomobject]@{
        Category = $Category; Scenario = $Scenario; Method = $Method; Status = $Status; Evidence = $Evidence
    })
}

# Kills processes left behind by paste-target tests (temp profile directories are named somning-paste-<marker>).
# Multi-process apps (VS Code, Chrome, Edge) detach children from the launcher; those children can outlive the test exe.
function Stop-OrphanTestProcesses {
    try {
        Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.IndexOf('somning-paste-', [StringComparison]::OrdinalIgnoreCase) -ge 0
        } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch { Write-Host "Orphan cleanup warning: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# Stops Somning.App/Somning.Engine processes whose EXECUTABLE lives under one of the given roots (copies this run installed or the
# Debug build the tests launch). The owner's own installation under %LOCALAPPDATA%\Somning\app is never matched.
function Stop-SomningUnder([string[]]$Roots) {
    $roots = @($Roots | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') + '\' })
    if ($roots.Count -eq 0) { return }
    try {
        Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object { $_.Name -in 'Somning.App.exe', 'Somning.Engine.exe' -and $_.ExecutablePath } | ForEach-Object {
            $exePath = $_.ExecutablePath
            if ($roots | Where-Object { $exePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
    } catch { Write-Host "Somning cleanup warning: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# Kills a process tree by PID without letting native stderr become a PowerShell error; returns the taskkill exit code.
function Stop-ProcessTree([int]$ProcessId) {
    & cmd.exe /c "taskkill /PID $ProcessId /T /F >nul 2>&1"
    return $LASTEXITCODE
}

# Reads at most the last $MaxChars characters of a file that another (orphaned) process may still hold open.
function Read-TailBounded([string]$Path, [int]$MaxChars = 1000000) {
    try {
        $fs = New-Object IO.FileStream($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $bytes = [Math]::Min($fs.Length, [long]($MaxChars * 4))
            [void]$fs.Seek(-$bytes, [IO.SeekOrigin]::End)
            $buffer = New-Object byte[] $bytes
            $read = $fs.Read($buffer, 0, $buffer.Length)
            return [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        } finally { $fs.Dispose() }
    } catch { return '' }
}

# Runs a native exe with stdout/stderr redirected to FILES (never pipes) and a hard timeout. Orphaned grandchildren that inherit
# the handles cannot hold this script (we only wait for the exe itself), and native stderr can never become a PowerShell error.
function Invoke-NativeBounded([string]$Exe, [string[]]$Arguments, [int]$TimeoutSeconds = 600) {
    $outFile = Join-Path $env:TEMP ('somning-native-' + [guid]::NewGuid().ToString('N') + '.out')
    $errFile = Join-Path $env:TEMP ('somning-native-' + [guid]::NewGuid().ToString('N') + '.err')
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
    $timedOut = $false; $exit = -1; $killFailed = $false
    $proc = Start-Process -FilePath $Exe -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $null = $proc.Handle
    try {
        $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
        if (-not $exited) {
            $timedOut = $true
            $killCode = Stop-ProcessTree $proc.Id
            $exited = $proc.WaitForExit(10000)
            if (-not $exited) { $killFailed = $true }   # no further unconditional wait: report it instead of hanging
            elseif ($killCode -ne 0) { Write-Host "taskkill exit $killCode but the process is gone" -ForegroundColor Yellow }
        }
        if ($exited) { $exit = $proc.ExitCode }
    } finally {
        Stop-OrphanTestProcesses
    }
    $text = ''
    foreach ($f in @($outFile, $errFile)) {
        if (Test-Path -LiteralPath $f) {
            $text += (Read-TailBounded $f) + "`n"
            Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
        }
    }
    return [pscustomobject]@{ Exit = $exit; Output = $text; TimedOut = ($timedOut -or $killFailed); KillFailed = $killFailed }
}

# Runs an xunit v3 in-process runner once and maps its summary line to a status (no retries).
function Invoke-XunitOnce([string]$Exe, [string[]]$Filter, [int]$ExpectedTotal) {
    $native = Invoke-NativeBounded $Exe $Filter 600
    $output = $native.Output
    $exit = $native.Exit
    if ($native.TimedOut) { return [pscustomobject]@{ Status = 'FAIL'; Counts = 'runner timed out after 600 s and was killed' } }
    $m = [regex]::Match($output, 'Total:\s*(\d+),\s*Errors:\s*(\d+),\s*Failed:\s*(\d+),\s*Skipped:\s*(\d+)')
    if (-not $m.Success) { return [pscustomobject]@{ Status = 'FAIL'; Counts = "no xunit summary (exit $exit)" } }
    $total = [int]$m.Groups[1].Value; $errors = [int]$m.Groups[2].Value
    $failed = [int]$m.Groups[3].Value; $skipped = [int]$m.Groups[4].Value
    $counts = "Total=$total Errors=$errors Failed=$failed Skipped=$skipped"
    if ($exit -ne 0 -or $errors -gt 0 -or $failed -gt 0) { $st = 'FAIL'; $counts += " (exit $exit)" }
    elseif ($total -lt $ExpectedTotal) { $st = 'FAIL'; $counts += " - expected at least $ExpectedTotal tests, filter matched too few" }
    elseif ($skipped -gt 0) { $st = 'SKIPPED' }
    else { $st = 'PASS' }
    return [pscustomobject]@{ Status = $st; Counts = $counts }
}

# Desktop tests depend on acquiring the foreground window from a background session, which is intermittently denied
# by Windows. Attempts > 1 retries FAIL/SKIPPED and records every attempt in the evidence (nothing is hidden).
function Add-XunitResult([string]$Category, [string]$Scenario, [string]$Exe, [string[]]$Filter, [int]$ExpectedTotal, [string]$PassNote, [int]$Attempts = 1) {
    $method = (Split-Path $Exe -Leaf) + ' ' + ($Filter -join ' ')
    if (-not (Test-Path -LiteralPath $Exe)) {
        Add-Result $Category $Scenario $method 'FAIL' "Test runner not built: $Exe"
        return
    }
    $history = @()
    $last = $null
    for ($n = 1; $n -le $Attempts; $n++) {
        if ($n -gt 1) { Stop-Process -Name notepad -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1 }
        $last = Invoke-XunitOnce $Exe $Filter $ExpectedTotal
        $history += "attempt ${n}: $($last.Status)"
        if ($last.Status -eq 'PASS') { break }
    }
    $evidence = $last.Counts
    if ($last.Status -eq 'PASS') { $evidence += ". $PassNote" }
    elseif ($last.Status -eq 'SKIPPED') { $evidence += ' - test skipped, behaviour NOT verified' }
    if ($Attempts -gt 1) { $evidence += ' [' + ($history -join ', ') + ']' }
    Add-Result $Category $Scenario $method $last.Status $evidence
}

# Runs an xunit v3 runner once with child-process-only environment variables and returns the per-test results
# parsed from the runner's own xUnit XML output (never class-level counts). Environment is restored afterwards.
function Invoke-XunitXml([string]$Exe, [string[]]$Filter, [hashtable]$ChildEnv = @{}) {
    if (-not (Test-Path -LiteralPath $Exe)) {
        return [pscustomobject]@{ Ran = $false; Exit = -1; Tests = @(); Note = "Test runner not built: $Exe" }
    }
    $xmlPath = Join-Path $env:TEMP ('somning-matrix-' + [guid]::NewGuid().ToString('N') + '.xml')
    $saved = @{}
    foreach ($k in $ChildEnv.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k, 'Process'); [Environment]::SetEnvironmentVariable($k, [string]$ChildEnv[$k], 'Process') }
    try {
        $native = Invoke-NativeBounded $Exe (@($Filter) + @('-preEnumerateTheories', '-result-xml', $xmlPath)) 600
        $exit = $native.Exit
        if ($native.TimedOut) { return [pscustomobject]@{ Ran = $true; Exit = $exit; Tests = @(); Note = 'runner timed out after 600 s and was killed' } }
    } finally {
        foreach ($k in $ChildEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k], 'Process') }
    }
    if (-not (Test-Path -LiteralPath $xmlPath)) { return [pscustomobject]@{ Ran = $true; Exit = $exit; Tests = @(); Note = "runner produced no XML result (exit $exit)" } }
    try {
        [xml]$doc = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
        $globalErrors = @($doc.SelectNodes('//errors/error') | ForEach-Object { [string]$_.type + ': ' + [string]$_.name })
        foreach ($a in @($doc.SelectNodes('//assembly'))) {
            $n = 0
            if ($a.Attributes['errors'] -and [int]::TryParse([string]$a.Attributes['errors'].Value, [ref]$n) -and $n -gt 0) { $globalErrors += "assembly reported $n error(s)" }
        }
        $tests = @($doc.SelectNodes('//test') | ForEach-Object {
            $reason = ''
            if ($_.SelectSingleNode('reason')) { $reason = $_.SelectSingleNode('reason').InnerText }
            elseif ($_.SelectSingleNode('failure/message')) { $reason = $_.SelectSingleNode('failure/message').InnerText }
            [pscustomobject]@{ Name = [string]$_.name; Result = [string]$_.result; Reason = $reason }
        })
    } catch {
        return [pscustomobject]@{ Ran = $true; Exit = $exit; Tests = @(); Note = "unparsable runner XML: $($_.Exception.Message)" }
    } finally { Remove-Item -LiteralPath $xmlPath -Force -ErrorAction SilentlyContinue }
    # A run whose exit code is non-zero without ANY failed test or global error is unexplained: no row may be approved from it.
    $anyFailed = @($tests | Where-Object { $_.Result -eq 'Fail' }).Count -gt 0
    $note = ''
    if ($globalErrors.Count -gt 0) { $note = 'runner reported global errors: ' + ($globalErrors -join '; ') }
    elseif ($exit -ne 0 -and -not $anyFailed) { $note = "runner exit code $exit with no failed test in the XML (unexplained)" }
    return [pscustomobject]@{ Ran = $true; Exit = $exit; Tests = $tests; Note = $note; Untrusted = [bool]$note }
}

# Maps one executed test result (Pass/Fail/Skip) to a matrix status; no result = FAIL (never silently PASS).
function Convert-TestStatus($Test, [string]$PassNote, [string]$MissingNote, $Run = $null) {
    if ($null -eq $Test) { return [pscustomobject]@{ Status = 'FAIL'; Evidence = $MissingNote } }
    # Never approve a row from a run the runner itself flagged (global errors, or exit != 0 with nothing failed).
    if ($Run -and $Run.PSObject.Properties['Untrusted'] -and $Run.Untrusted -and $Test.Result -ne 'Fail') {
        return [pscustomobject]@{ Status = 'FAIL'; Evidence = "test result '$($Test.Result)' not trusted: $($Run.Note)" }
    }
    $short = $Test.Reason.Trim(); if ($short.Length -gt 300) { $short = $short.Substring(0, 300) + '...' }
    switch ($Test.Result) {
        'Pass' { return [pscustomobject]@{ Status = 'PASS'; Evidence = $PassNote } }
        'Skip' { return [pscustomobject]@{ Status = 'SKIPPED'; Evidence = "skipped, behaviour NOT verified: $short" } }
        default { return [pscustomobject]@{ Status = 'FAIL'; Evidence = "failed: $short" } }
    }
}

try {
Write-Host '=== Somning Paste Matrix & Deployment Verifier ===' -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# Part 1: unattended install / run / autostart / uninstall on a space + non-ASCII path (DIST-01)
# -----------------------------------------------------------------------------
Write-Host "`n--- Part 1: Installer lifecycle (space + non-ASCII path) ---" -ForegroundColor Yellow

$stageDir = Join-Path $env:TEMP ('Somning Teste ' + [string][char]0x6E2C + [string][char]0x8A66 + ' a' + [string][char]0xE7 + [string][char]0xE3 + 'o ' + [guid]::NewGuid().ToString('N'))
$installAppDir = [IO.Path]::GetFullPath((Join-Path $stageDir 'app'))   # normalizes 8.3 short names (ADMINI~1) like install.ps1 does
$programsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsPath 'Somning.lnk'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$dataLogs = Join-Path $env:LOCALAPPDATA 'Somning\logs'
$sentinel = Join-Path $dataLogs ('matrix-sentinel-' + [guid]::NewGuid().ToString('N') + '.txt')

# Preserve any real user state this test would otherwise clobber.
$shortcutBackup = $null
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcutBackup = Join-Path $env:TEMP ('Somning.lnk.' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $shortcutPath -Destination $shortcutBackup -Force
}
$runBackup = (Get-ItemProperty -Path $runKeyPath -Name 'Somning' -ErrorAction SilentlyContinue)
$runBackupValue = if ($null -ne $runBackup) { $runBackup.Somning } else { $null }

$installOk = $false
$harnessInstallDir = $null
$debugBinDir = Split-Path $integrationExe -Parent   # the Debug build the tests launch (never the owner's installed copy)
try {
    $installScript = Join-Path $PSScriptRoot 'install.ps1'
    $installedApp = Join-Path $installAppDir 'Somning.App.exe'
    $installedEngine = Join-Path $installAppDir 'engine\Somning.Engine.exe'
    $installedUninstall = Join-Path $installAppDir 'uninstall.ps1'

    try {
        & $installScript -SourceDir $PackageDir -InstallDir $installAppDir -StartMenuShortcut -StartWithWindows -Unattended
        $missing = @($installedApp, $installedEngine, $installedUninstall, $shortcutPath) | Where-Object { -not (Test-Path -LiteralPath $_) }
        if ($missing) { throw ('Missing after install: ' + ($missing -join ', ')) }
        $installOk = $true
        Add-Result 'Deployment' 'Unattended install, space + non-ASCII path' 'tools/install.ps1 -Unattended' 'PASS' "App, engine, uninstall.ps1 and Start Menu shortcut present under '$installAppDir'"
    } catch {
        Add-Result 'Deployment' 'Unattended install, space + non-ASCII path' 'tools/install.ps1 -Unattended' 'FAIL' $_.Exception.Message
    }

    if ($installOk) {
        try {
            $target = [Somning.ShellLinkWriter]::GetTarget($shortcutPath)
            if ($target -ne $installedApp) { throw "Shortcut target '$target' != '$installedApp'" }
            Add-Result 'Deployment' 'Start Menu shortcut target' 'IShellLinkW.GetPath (Unicode)' 'PASS' 'Shortcut points to the installed Somning.App.exe'
        } catch { Add-Result 'Deployment' 'Start Menu shortcut target' 'IShellLinkW.GetPath (Unicode)' 'FAIL' $_.Exception.Message }

        try {
            $runValue = (Get-ItemProperty -Path $runKeyPath -Name 'Somning').Somning
            if ($runValue -ne ('"' + $installedApp + '" --tray')) { throw "Unexpected Run value: $runValue" }
            Add-Result 'Deployment' 'Autostart Run key (install -StartWithWindows)' 'HKCU Run value' 'PASS' 'Quoted exe path with --tray written'
        } catch { Add-Result 'Deployment' 'Autostart Run key (install -StartWithWindows)' 'HKCU Run value' 'FAIL' $_.Exception.Message }

        # Installed self-contained binary: real transcription of a fixture, compared with the expected text.
        try {
            $fixture = Join-Path $root 'tests\fixtures\tracer\hoje.wav'
            $expected = [IO.File]::ReadAllText((Join-Path $root 'tests\fixtures\tracer\hoje.txt'), $utf8).Trim()
            Add-Type -AssemblyName System.Windows.Forms
            [Windows.Forms.Clipboard]::SetText('somning-matrix-sentinel')
            $psi = New-Object Diagnostics.ProcessStartInfo
            $psi.FileName = $installedApp
            $psi.WorkingDirectory = $installAppDir
            $psi.Arguments = '--transcribe-file "' + $fixture + '" --backend cpu'
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.EnvironmentVariables['SOMNING_MODELS_DIR'] = $ModelsDir
            $p = [Diagnostics.Process]::Start($psi)
            if (-not $p.WaitForExit(180000)) { $p.Kill(); throw 'Installed app transcription timed out' }
            if ($p.ExitCode -ne 0) { throw "Installed app exited with code $($p.ExitCode)" }
            $actual = [Windows.Forms.Clipboard]::GetText().Trim()
            if ($actual -ne $expected) { throw "Clipboard transcription mismatch. Expected '$expected', got '$actual'" }
            Add-Result 'Deployment' 'Installed self-contained app transcribes' 'installed Somning.App.exe --transcribe-file hoje.wav --backend cpu' 'PASS' 'Exit 0; clipboard text equals tests/fixtures/tracer/hoje.txt (engine launched from installed engine\ dir)'
        } catch { Add-Result 'Deployment' 'Installed self-contained app transcribes' 'installed Somning.App.exe --transcribe-file' 'FAIL' $_.Exception.Message }

        # Tray launch smoke: process stays alive and spawns the engine from the installed directory.
        $tray = $null
        try {
            if (Get-Process -Name 'Somning.App' -ErrorAction SilentlyContinue) { throw 'Another Somning.App instance is running; cannot smoke-test tray launch' }
            $tray = Start-Process -FilePath $installedApp -ArgumentList '--tray' -WorkingDirectory $installAppDir -PassThru
            $engineSeen = $false
            for ($i = 0; $i -lt 40 -and -not $engineSeen; $i++) {
                Start-Sleep -Milliseconds 500
                if ($tray.HasExited) { throw "Tray app exited early with code $($tray.ExitCode)" }
                $engineSeen = [bool](Get-Process -Name 'Somning.Engine' -ErrorAction SilentlyContinue | Where-Object {
                    try { $_.MainModule.FileName -like ($installAppDir + '\engine\*') } catch { $false } })
            }
            if (-not $engineSeen) { throw 'Engine process from installed engine\ directory was not observed within 20 s' }
            Start-Sleep -Seconds 2
            if ($tray.HasExited) { throw "Tray app exited after engine start (code $($tray.ExitCode))" }
            Add-Result 'Deployment' 'Installed app launches with --tray' 'Start-Process installed Somning.App.exe --tray' 'PASS' 'Process alive; Somning.Engine.exe started from installed engine\ directory'
        } catch { Add-Result 'Deployment' 'Installed app launches with --tray' 'Start-Process installed Somning.App.exe --tray' 'FAIL' $_.Exception.Message }
        finally {
            # Only the process tree THIS run started (the installed copy), never the owner's own Somning.
            if ($tray -and -not $tray.HasExited) { [void](Stop-ProcessTree $tray.Id) }
            Stop-SomningUnder @($installAppDir)
            Start-Sleep -Milliseconds 500
        }

        # Uninstall: runs the installed copy, must remove app + shortcut + Run key, and keep user data.
        try {
            New-Item -ItemType Directory -Path $dataLogs -Force | Out-Null
            Set-Content -LiteralPath $sentinel -Value 'keep' -Encoding ASCII
            & $installedUninstall -InstallDir $installAppDir -Quiet
            $problems = @()
            if (Test-Path -LiteralPath $installAppDir) { $problems += 'application directory still exists' }
            if (Test-Path -LiteralPath $shortcutPath) { $problems += 'Start Menu shortcut still exists' }
            if ($null -ne (Get-ItemProperty -Path $runKeyPath -Name 'Somning' -ErrorAction SilentlyContinue)) { $problems += 'Run key still present' }
            if (-not (Test-Path -LiteralPath $sentinel)) { $problems += 'user data (logs) was deleted' }
            if ($problems) { throw ($problems -join '; ') }
            Add-Result 'Deployment' 'Uninstall removes app, shortcut, Run key; keeps user data' 'installed uninstall.ps1 -Quiet' 'PASS' 'App dir, shortcut and Run value removed; sentinel file in %LOCALAPPDATA%\Somning\logs preserved'
        } catch { Add-Result 'Deployment' 'Uninstall removes app, shortcut, Run key; keeps user data' 'installed uninstall.ps1 -Quiet' 'FAIL' $_.Exception.Message }
    }

    # -------------------------------------------------------------------------
    # Part 1b: the INSTALLED harness-enabled binary passes the Phase 1 Notepad E2E (DIST-01 gap).
    # Runs after the first install lifecycle was uninstalled and its rows were recorded. The harness package is TEST ONLY:
    # it is installed to a second staged space + non-ASCII directory, WITHOUT a Start Menu shortcut and WITHOUT autostart,
    # and always uninstalled in finally. Shortcut/Run key backups from Part 1 are restored by the outer finally.
    # -------------------------------------------------------------------------
    Write-Host "`n--- Part 1b: Installed harness binary passes Phase 1 E2E into Notepad ---" -ForegroundColor Yellow
    $stageDir2 = Join-Path $env:TEMP ('Somning Harness ' + [string][char]0x6E2C + [string][char]0x8A66 + ' a' + [string][char]0xE7 + [string][char]0xE3 + 'o ' + [guid]::NewGuid().ToString('N'))
    $harnessInstallDir = [IO.Path]::GetFullPath((Join-Path $stageDir2 'app'))
    $e2eScenarios = @(
        @{ Method = '*DictationE2ETests.TapTap_cmd_curto_delivers_to_notepad_without_layout_or_hotkey_leaks*'; Scenario = 'Installed binary passes Phase 1 E2E into Notepad (tap-tap)' },
        @{ Method = '*DictationE2ETests.HoldRelease_longo_numeros_requires_full_hold_and_delivers*'; Scenario = 'Installed binary passes Phase 1 E2E into Notepad (hold-release)' }
    )
    $harnessInstalled = $false
    try {
        $harnessDir = Join-Path $root 'publish\package-harness'
        $harnessExe = Join-Path $harnessDir 'Somning.App.exe'
        $stale = -not (Test-Path -LiteralPath $harnessExe)
        if (-not $stale) {
            $builtAt = (Get-Item -LiteralPath $harnessExe).LastWriteTimeUtc
            $newest = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -File -Include *.cs, *.csproj, *.xaml, *.resx -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            $props = Get-Item -LiteralPath (Join-Path $root 'Directory.Build.props') -ErrorAction SilentlyContinue
            if (($newest -and $newest.LastWriteTimeUtc -gt $builtAt) -or ($props -and $props.LastWriteTimeUtc -gt $builtAt)) { $stale = $true }
        }
        if ($stale) {
            Write-Host 'Harness package missing or older than sources; building it with tools/package.ps1 -WithHarness...'
            & (Join-Path $PSScriptRoot 'package.ps1') -WithHarness
        }
        & (Join-Path $PSScriptRoot 'install.ps1') -SourceDir $harnessDir -InstallDir $harnessInstallDir -StartMenuShortcut:$false -StartWithWindows:$false -Unattended
        $installedHarnessApp = Join-Path $harnessInstallDir 'Somning.App.exe'
        if (-not (Test-Path -LiteralPath $installedHarnessApp)) { throw "Harness install did not produce $installedHarnessApp" }
        $harnessInstalled = $true
    } catch {
        foreach ($s in $e2eScenarios) {
            Add-Result 'Deployment' $s.Scenario 'tools/package.ps1 -WithHarness + install.ps1' 'FAIL' ("harness package install failed: " + $_.Exception.Message)
        }
    }
    if ($harnessInstalled) {
        try {
            foreach ($s in $e2eScenarios) {
                Stop-Process -Name notepad -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300
                $run = Invoke-XunitXml $integrationExe @('-method', $s.Method) @{ SOMNING_APP_EXE = $installedHarnessApp; SOMNING_MODELS_DIR = $ModelsDir }
                $method = 'Somning.Integration.Tests.exe ' + $s.Method + ' (SOMNING_APP_EXE=installed)'
                $test = $null
                if ($run.Tests.Count -eq 1) { $test = $run.Tests[0] }
                $missing = if ($run.Note) { $run.Note } else { "expected exactly 1 test for the filter, matched $($run.Tests.Count) (exit $($run.Exit))" }
                $mapped = Convert-TestStatus $test ("Real Notepad received the dictated text using installed app '" + $installedHarnessApp + "'") $missing $run
                Add-Result 'Deployment' $s.Scenario $method $mapped.Status ($mapped.Evidence + " [installed path: $installedHarnessApp]")
            }
        } finally {
            Stop-SomningUnder @($harnessInstallDir, $debugBinDir)
            Start-Sleep -Milliseconds 500
            try { & (Join-Path $harnessInstallDir 'uninstall.ps1') -InstallDir $harnessInstallDir -Quiet } catch { Write-Host "Harness uninstall warning: $($_.Exception.Message)" -ForegroundColor Yellow }
        }
    }
    if (Test-Path -LiteralPath $stageDir2) { Remove-Item -LiteralPath $stageDir2 -Recurse -Force -ErrorAction SilentlyContinue }
} finally {
    Stop-SomningUnder @($installAppDir, $harnessInstallDir, $debugBinDir)
    if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue }
    # Restore real user state.
    if ($shortcutBackup) {
        Copy-Item -LiteralPath $shortcutBackup -Destination $shortcutPath -Force
        Remove-Item -LiteralPath $shortcutBackup -Force -ErrorAction SilentlyContinue
    } elseif (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue }
    if ($null -ne $runBackupValue) { Set-ItemProperty -Path $runKeyPath -Name 'Somning' -Value $runBackupValue }
    else { Remove-ItemProperty -Path $runKeyPath -Name 'Somning' -ErrorAction SilentlyContinue }
}

# -----------------------------------------------------------------------------
# Part 2: paste / policy / resilience matrix, each row backed by named xunit tests (DIST-03)
# -----------------------------------------------------------------------------
Write-Host "`n--- Part 2: Paste matrix and resilience (xunit-backed) ---" -ForegroundColor Yellow

function Reset-Notepad { Stop-Process -Name notepad -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300 }

Reset-Notepad
Add-XunitResult 'Paste' 'Notepad (Win32 Edit): Ctrl+V paste, text stays on clipboard' $integrationExe @('-method', '*Pastes_into_notepad_and_keeps_clipboard*') 1 'Real Notepad received the text; clipboard retained' $EvidenceAttempts
Reset-Notepad
Add-XunitResult 'Paste' 'Foreground window changed before paste: defers, no leakage' $integrationExe @('-method', '*TargetPasterTests.Foreground_changed_defers_and_leaves_text_on_clipboard*') 1 'paste_deferred; text left on clipboard' $EvidenceAttempts
Reset-Notepad
Add-XunitResult 'Paste' 'Focus moved to another field in same window: defers' $integrationExe @('-method', '*TargetPasterTests.Focus_change_inside_same_top_level_window_defers*') 1 'paste_deferred; no keystrokes injected' $EvidenceAttempts
Reset-Notepad
Add-XunitResult 'Paste' 'Clipboard changed while waiting for modifiers: defers before injection' $integrationExe @('-method', '*Clipboard_change_during_modifier_wait_defers_before_injection*') 1 'No injection after external copy' $EvidenceAttempts
Reset-Notepad
Add-XunitResult 'Paste' 'Clipboard-only mode (auto-paste off): never pastes' $integrationExe @('-method', '*Copy_only_never_pastes*') 1 'Clipboard set; zero keystrokes injected' $EvidenceAttempts

if ($SkipE2E) {
    Add-Result 'Paste' 'Live app E2E: foreground/focus change after capture' 'PasteDeferredE2ETests' 'SKIPPED' '-SkipE2E supplied; not run'
} else {
    Reset-Notepad
    Add-XunitResult 'Paste' 'Live app E2E: foreground change after capture defers' $integrationExe @('-method', '*PasteDeferredE2ETests.Foreground_change_after_capture_defers_paste_and_keeps_transcript_on_clipboard*') 1 'Real app + engine + Notepad; outcome paste_deferred / foreground_changed' $EvidenceAttempts
    Reset-Notepad
    Add-XunitResult 'Paste' 'Live app E2E: same-window focus change after capture defers' $integrationExe @('-method', '*PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste*') 1 'Real app + engine + Notepad; paste_deferred' $EvidenceAttempts
}

Reset-Notepad
Add-XunitResult 'Elevation' 'Process integrity level read (self vs. child Notepad, SYSTEM process)' $integrationExe @('-class', '*IntegrityReaderTests*') 2 'Integrity RID read for real processes; does NOT involve an elevated target window' $EvidenceAttempts
Add-XunitResult 'Elevation' 'Elevated target defers (PasteGate policy)' $coreExe @('-method', '*PasteGateTests.Target_elevated_defers_target_elevated*', '-method', '*PasteGateTests.Integrity_unknown_defers_integrity_unknown*') 2 'Policy unit tests only (decision logic), not empirical UIPI proof'
Add-XunitResult 'Elevation' 'Elevated target window, real UIPI behaviour' $integrationExe @('-method', '*Elevated_target_defers_paste_when_a_real_integrity_pair_is_available*') 1 'Real elevated target'
Add-Result 'Terminal' 'Windows Terminal / console hosts (conhost, WT)' 'none' 'MANUAL' 'No automated test exists for Windows Terminal or console paste; see MANUAL-CHECKLIST.md item 1'

Add-XunitResult 'Resilience' 'Sleep/resume and lock/unlock messages raise events; hook reinstalled' $integrationExe @('-class', '*PowerSessionIntegrationTests*') 3 'WM_POWERBROADCAST / WM_WTSSESSION_CHANGE are POSTED SYNTHETICALLY to the listener window; hook reinstall verified' $EvidenceAttempts
Add-Result 'Resilience' 'Real machine sleep -> wake and real workstation lock -> unlock' 'none' 'MANUAL' 'Not automated: a real suspend/lock would interrupt the agent session; see MANUAL-CHECKLIST.md items 3 and 4'
Add-Result 'Elevation' 'Real UIPI against an elevated target window' 'none' 'MANUAL' 'No low/high integrity pair is provisioned unattended; see MANUAL-CHECKLIST.md item 2'
Add-Result 'Deployment' 'Clean-machine install without .NET SDK, and logon autostart after reboot' 'none' 'MANUAL' 'Needs a clean profile/VM and a reboot; see MANUAL-CHECKLIST.md items 5 and 6'

# --- Hook liveness watchdog (desktop facts). Per-test results from the runner XML; SKIPPED stays SKIPPED. ---
Reset-Notepad
$wd = Invoke-XunitXml $integrationExe @('-class', '*HookWatchdogLivenessTests*')
foreach ($spec in @(
    @{ Name = 'Silently_removed_hook_is_reinstalled_and_probe_is_seen_again'; Scenario = 'Watchdog: silently removed keyboard hook is reinstalled and probe is seen again'; Pass = 'Real hook silently removed, watchdog reinstalled it, F24 probe seen again' },
    @{ Name = 'Blocked_injection_is_inconclusive_and_causes_no_reinstall'; Scenario = 'Watchdog: blocked probe injection is inconclusive and causes no reinstall'; Pass = 'Probe injection blocked by a higher-integrity foreground window; no reinstall storm' },
    @{ Name = 'Fake_probe_loop_reinstalls_only_after_threshold'; Scenario = 'Watchdog: headless fake probe loop reinstalls only after threshold'; Pass = 'Headless fake loop honoured the reinstall threshold' })) {
    $t = $wd.Tests | Where-Object { $_.Name -like ('*' + $spec.Name + '*') } | Select-Object -First 1
    $missing = if ($wd.Note) { $wd.Note } else { "no result for $($spec.Name) (runner exit $($wd.Exit))" }
    $m = Convert-TestStatus $t $spec.Pass $missing $wd
    Add-Result 'Watchdog' $spec.Scenario ('Somning.Integration.Tests.exe -class *HookWatchdogLivenessTests* :: ' + $spec.Name) $m.Status $m.Evidence
}

# --- Paste target applications: one row per Theory case, mapped from that case's own result. ---
Reset-Notepad
$pasteRun = Invoke-XunitXml $integrationExe @('-method', '*PasteTargetMatrixTests.Pastes_into_target_application*') @{}
$pasteTests = $pasteRun.Tests
foreach ($app in @(@('chrome', 'Chrome'), @('edge', 'Edge'), @('vscode', 'VS Code'), @('word', 'Word'))) {
    $id = $app[0]
    # xunit v3 renders the theory data as (id: \"chrome\") with backslash-escaped quotes, so the match tolerates the backslash.
    $t = $pasteTests | Where-Object { $_.Name -match ('id:\s*[^"]?"' + [regex]::Escape($id) + '[^"]?"') } | Select-Object -First 1
    $missing = if ($pasteRun.Note) { $pasteRun.Note } else { "no result for target '$id' in the runner output (exit $($pasteRun.Exit))" }
    $m = Convert-TestStatus $t ($app[1] + ' received the pasted sentinel text via Ctrl+V and clipboard read-back matched') $missing $pasteRun
    if ($m.Status -eq 'SKIPPED' -and $t.Reason -match 'not installed') { $m = [pscustomobject]@{ Status = 'SKIPPED'; Evidence = 'not installed on this machine' } }
    Add-Result 'Paste' ('Application target: ' + $app[1]) ('PasteTargetMatrixTests.Pastes_into_target_application(' + $id + ')') $m.Status $m.Evidence
}

# --- Soak: SoakIterations clipboard-only dictations + one real-hotkey dictation. The report is produced by the test. ---
Reset-Notepad
$soakWritten = Join-Path $phaseDir 'soak-report.json'
foreach ($stalePath in @($soakWritten, $soakReportPath)) { if (Test-Path -LiteralPath $stalePath) { Remove-Item -LiteralPath $stalePath -Force } }
$soakRun = Invoke-XunitXml $integrationExe @('-trait', 'Category=Soak') @{ SOMNING_SOAK_N = [string]$SoakIterations }
$soakTest = $soakRun.Tests | Where-Object { $_.Name -like '*Fifty_dictations_then_real_hotkey_dictation_stay_healthy*' } | Select-Object -First 1
try {
    if (-not (Test-Path -LiteralPath $soakWritten)) { throw 'soak test did not write soak-report.json' }
    $soakJson = Get-Content -LiteralPath $soakWritten -Raw -Encoding UTF8 | ConvertFrom-Json
    # Normalise the file name casing to SOAK-REPORT.json via a temp name (the test writes soak-report.json).
    $tmpName = Join-Path $phaseDir ('soak-report.' + [guid]::NewGuid().ToString('N') + '.tmp')
    Move-Item -LiteralPath $soakWritten -Destination $tmpName -Force
    Move-Item -LiteralPath $tmpName -Destination $soakReportPath -Force
    $hGrow = $soakJson.finalHandles - $soakJson.baselineHandles
    $bGrow = $soakJson.finalPrivateBytes - $soakJson.baselinePrivateBytes
    $ev = "requested=$($soakJson.requested) completed=$($soakJson.completed) failures=$($soakJson.failures) handleDelta=$hGrow privateByteDelta=$bGrow " +
          "hookInstalls=$($soakJson.hookInstallCountStart)->$($soakJson.hookInstallCountEnd) watchdogReinstalls=$($soakJson.watchdogReinstalls) finalRealHotkeyOk=$($soakJson.finalRealHotkeyOk) result=$($soakJson.result)"
    if ($soakJson.result -ne 'PASS') { $ev += ' reasons=' + (($soakJson.reasons) -join '; ') }
    $status = if ($soakJson.result -eq 'PASS' -and $soakTest -and $soakTest.Result -eq 'Pass' -and -not $soakRun.Untrusted) { 'PASS' }
              elseif ($soakTest -and $soakTest.Result -eq 'Skip') { 'SKIPPED' } else { 'FAIL' }
    if ($status -eq 'FAIL' -and $soakJson.result -eq 'PASS') { $ev += ' (report says PASS but the test did not pass)' }
    Add-Result 'Soak' "Soak: $SoakIterations dictations + real-hotkey dictation, no leaks or failures" 'SoakTests.Fifty_dictations_then_real_hotkey_dictation_stay_healthy; SOAK-REPORT.json' $status $ev
} catch {
    $why = $_.Exception.Message
    if ($soakTest -and $soakTest.Result -eq 'Skip') { Add-Result 'Soak' "Soak: $SoakIterations dictations + real-hotkey dictation" 'SoakTests' 'SKIPPED' ("skipped, no report written: " + $soakTest.Reason.Trim()) }
    else { Add-Result 'Soak' "Soak: $SoakIterations dictations + real-hotkey dictation" 'SoakTests' 'FAIL' ("missing or unparsable soak report: $why " + $soakRun.Note) }
}

# -----------------------------------------------------------------------------
# Part 3: report
# -----------------------------------------------------------------------------
Write-Host "`n--- Part 3: Writing PASTE-MATRIX.md ---" -ForegroundColor Yellow

$count = { param($s) @($results | Where-Object { $_.Status -eq $s }).Count }
$nPass = & $count 'PASS'; $nFail = & $count 'FAIL'; $nSkip = & $count 'SKIPPED'; $nManual = & $count 'MANUAL'
$overall = if ($nFail -gt 0) { 'FAIL' } elseif ($nSkip + $nManual -gt 0) { "PASS for all automated checks ($nSkip skipped, $nManual manual/unverified)" } else { 'PASS' }

$dateStr = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
$osVer = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').ProductName + ' (' + (Get-CimInstance Win32_OperatingSystem).Version + ')'

function Format-Cell([string]$Text) { return $Text.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ') }

$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine('# Somning Paste Matrix & Deployment Hardening Report')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("**Date**: $dateStr  ")
[void]$sb.AppendLine("**Environment**: $osVer, win-x64  ")
[void]$sb.AppendLine("**Overall**: $overall  ")
[void]$sb.AppendLine("**Counts**: PASS=$nPass FAIL=$nFail SKIPPED=$nSkip MANUAL=$nManual  ")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Requirements: **DIST-01** (per-user unattended install; installed binary passes the Phase 1 E2E), **DIST-02** (retained) and **DIST-03** (soak, hook watchdog, paste matrix incl. Chrome/Edge/VS Code/Word, elevated-target handling, sleep/lock resilience).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('**Attempts per row**: 1 (no retries; first-run results). Soak iterations requested: '+$SoakIterations+'.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Every row below was produced by an executed check in `tools/verify-matrix.ps1`. Status meanings: PASS = executed and passed; FAIL = executed and failed; SKIPPED = test exists but was skipped (behaviour not verified); MANUAL = no automated check exists, a human must verify.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Category | Scenario | Check | Status | Evidence |')
[void]$sb.AppendLine('|----------|----------|-------|:------:|----------|')
foreach ($r in $results) {
    [void]$sb.AppendLine(('| {0} | {1} | `{2}` | **{3}** | {4} |' -f (Format-Cell $r.Category), (Format-Cell $r.Scenario), (Format-Cell $r.Method), $r.Status, (Format-Cell $r.Evidence)))
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Known limits (not verified by this run)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Items below remain MANUAL (see `MANUAL-CHECKLIST.md`); SKIPPED rows above (for example an application that is not installed) are also NOT verified.')
[void]$sb.AppendLine('- Windows Terminal / console paste: no automated test (checklist item 1).')
[void]$sb.AppendLine('- Real UIPI against an elevated window: only the PasteGate decision policy is unit tested (checklist item 2).')
[void]$sb.AppendLine('- Real sleep/resume and lock/unlock: only synthetic window messages are tested (checklist items 3 and 4).')
[void]$sb.AppendLine('- Clean-machine install without the .NET SDK and logon autostart after reboot (checklist items 5 and 6).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('*Generated by `tools/verify-matrix.ps1`.*')

[IO.File]::WriteAllText($reportPath, $sb.ToString(), $utf8)
Write-Host "Report written to: $reportPath" -ForegroundColor Green

if ($nFail -gt 0) { throw "$nFail matrix check(s) failed." }
Write-Host "`n=== Matrix finished: $overall ===" -ForegroundColor Green
} finally {
    # Never leave test-launched browsers/editors or runner children behind (they hold inherited handles and profiles).
    Stop-OrphanTestProcesses
    try { $runMutex.ReleaseMutex() } catch { }
}
