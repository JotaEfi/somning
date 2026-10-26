param(
    [string]$PackageDir = (Join-Path $PSScriptRoot '..\publish\package'),
    [string]$ModelsDir = (Join-Path $env:LOCALAPPDATA 'Somning\models'),
    [switch]$SkipE2E
)

# Generates .planning\phases\04-packaging-autostart-hardening\PASTE-MATRIX.md from REAL results only.
# Every row is derived from an executed check; nothing is hardcoded as PASS. Rows that cannot be verified
# without a human or a privileged desktop are reported as SKIPPED or MANUAL, never PASS.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$utf8 = New-Object Text.UTF8Encoding($false)
$reportPath = Join-Path $root '.planning\phases\04-packaging-autostart-hardening\PASTE-MATRIX.md'
$integrationExe = Join-Path $root 'tests\Somning.Integration.Tests\bin\Debug\net10.0-windows\Somning.Integration.Tests.exe'
$coreExe = Join-Path $root 'tests\Somning.Core.Tests\bin\Debug\net10.0\Somning.Core.Tests.exe'
$PackageDir = [IO.Path]::GetFullPath($PackageDir)

$results = New-Object 'Collections.Generic.List[object]'

function Add-Result([string]$Category, [string]$Scenario, [string]$Method, [string]$Status, [string]$Evidence) {
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0}] {1} / {2}: {3}" -f $Status, $Category, $Scenario, $Evidence) -ForegroundColor $color
    $script:results.Add([pscustomobject]@{
        Category = $Category; Scenario = $Scenario; Method = $Method; Status = $Status; Evidence = $Evidence
    })
}

# Runs an xunit v3 in-process runner once and maps its summary line to a status (no retries).
function Invoke-XunitOnce([string]$Exe, [string[]]$Filter, [int]$ExpectedTotal) {
    $output = & $Exe @Filter 2>&1 | Out-String
    $exit = $LASTEXITCODE
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
            Get-Process -Name 'Somning.App', 'Somning.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
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
} finally {
    Get-Process -Name 'Somning.App', 'Somning.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
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
Add-XunitResult 'Paste' 'Notepad (Win32 Edit): Ctrl+V paste, text stays on clipboard' $integrationExe @('-method', '*Pastes_into_notepad_and_keeps_clipboard*') 1 'Real Notepad received the text; clipboard retained' 3
Reset-Notepad
Add-XunitResult 'Paste' 'Foreground window changed before paste: defers, no leakage' $integrationExe @('-method', '*TargetPasterTests.Foreground_changed_defers_and_leaves_text_on_clipboard*') 1 'paste_deferred; text left on clipboard' 3
Reset-Notepad
Add-XunitResult 'Paste' 'Focus moved to another field in same window: defers' $integrationExe @('-method', '*TargetPasterTests.Focus_change_inside_same_top_level_window_defers*') 1 'paste_deferred; no keystrokes injected' 3
Reset-Notepad
Add-XunitResult 'Paste' 'Clipboard changed while waiting for modifiers: defers before injection' $integrationExe @('-method', '*Clipboard_change_during_modifier_wait_defers_before_injection*') 1 'No injection after external copy' 3
Reset-Notepad
Add-XunitResult 'Paste' 'Clipboard-only mode (auto-paste off): never pastes' $integrationExe @('-method', '*Copy_only_never_pastes*') 1 'Clipboard set; zero keystrokes injected' 3

if ($SkipE2E) {
    Add-Result 'Paste' 'Live app E2E: foreground/focus change after capture' 'PasteDeferredE2ETests' 'SKIPPED' '-SkipE2E supplied; not run'
} else {
    Reset-Notepad
    Add-XunitResult 'Paste' 'Live app E2E: foreground change after capture defers' $integrationExe @('-method', '*PasteDeferredE2ETests.Foreground_change_after_capture_defers_paste_and_keeps_transcript_on_clipboard*') 1 'Real app + engine + Notepad; outcome paste_deferred / foreground_changed' 3
    Reset-Notepad
    Add-XunitResult 'Paste' 'Live app E2E: same-window focus change after capture defers' $integrationExe @('-method', '*PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste*') 1 'Real app + engine + Notepad; paste_deferred' 3
}

Reset-Notepad
Add-XunitResult 'Elevation' 'Process integrity level read (self vs. child Notepad, SYSTEM process)' $integrationExe @('-class', '*IntegrityReaderTests*') 2 'Integrity RID read for real processes; does NOT involve an elevated target window' 3
Add-XunitResult 'Elevation' 'Elevated target defers (PasteGate policy)' $coreExe @('-method', '*PasteGateTests.Target_elevated_defers_target_elevated*', '-method', '*PasteGateTests.Integrity_unknown_defers_integrity_unknown*') 2 'Policy unit tests only (decision logic), not empirical UIPI proof'
Add-XunitResult 'Elevation' 'Elevated target window, real UIPI behaviour' $integrationExe @('-method', '*Elevated_target_defers_paste_when_a_real_integrity_pair_is_available*') 1 'Real elevated target'
Add-Result 'Terminal' 'Windows Terminal / console hosts (conhost, WT)' 'none' 'MANUAL' 'No automated test exists for Windows Terminal or console paste; requires a human check (Ctrl+V into Windows Terminal)'

Add-XunitResult 'Resilience' 'Sleep/resume and lock/unlock messages raise events; hook reinstalled' $integrationExe @('-class', '*PowerSessionIntegrationTests*') 3 'WM_POWERBROADCAST / WM_WTSSESSION_CHANGE are POSTED SYNTHETICALLY to the listener window; hook reinstall verified'
Add-Result 'Resilience' 'Real machine sleep -> wake and real workstation lock -> unlock' 'none' 'MANUAL' 'Not automated: a real suspend/lock would interrupt the agent session; synthetic-message tests above are the only coverage'

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
[void]$sb.AppendLine('Requirements: **DIST-01** (per-user unattended install) and **DIST-03** (paste matrix, elevated-target handling, sleep/lock resilience).')
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
[void]$sb.AppendLine('- Windows Terminal / console paste: no automated test; verify manually.')
[void]$sb.AppendLine('- Real UIPI against an elevated window: the integration test is skipped (no low/high integrity pair is provisioned unattended); only the PasteGate decision policy is unit tested.')
[void]$sb.AppendLine('- Real sleep/resume and lock/unlock: only synthetic window messages are tested.')
[void]$sb.AppendLine('- Clean-machine install (no .NET SDK installed) was not performed; the app is self-contained and was run from an installed copy on this development machine.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('*Generated by `tools/verify-matrix.ps1`.*')

[IO.File]::WriteAllText($reportPath, $sb.ToString(), $utf8)
Write-Host "Report written to: $reportPath" -ForegroundColor Green

if ($nFail -gt 0) { throw "$nFail matrix check(s) failed." }
Write-Host "`n=== Matrix finished: $overall ===" -ForegroundColor Green
