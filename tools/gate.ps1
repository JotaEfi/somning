param([string]$ModelsDir = (Join-Path $env:LOCALAPPDATA 'Somning\models'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$utf8 = New-Object Text.UTF8Encoding($false)
$root = Split-Path $PSScriptRoot -Parent
$report = Join-Path $root '.planning\phases\01-dictation-loop-mvp\GATE-RESULT.md'
$rows = New-Object 'Collections.Generic.List[string]'
$failed = $false
function Format-Number($Value) { return ([double]$Value).ToString('F2', $culture) }
function Read-Message($Reader, [int]$Seconds) {
    $task = $Reader.ReadLineAsync()
    if (-not $task.Wait([TimeSpan]::FromSeconds($Seconds))) { throw 'Protocol read timeout' }
    if ($null -eq $task.Result) { throw 'Worker exited before expected message' }
    return ($task.Result | ConvertFrom-Json)
}
function Get-Tokens([string]$Text) {
    return @(([regex]::Replace($Text.ToLowerInvariant(), '[^\p{L}\p{N}\s]', ' ') -split '\s+') | Where-Object { $_ })
}
function Get-Wer([string]$Expected, [string]$Actual) {
    $a = @(Get-Tokens $Expected); $b = @(Get-Tokens $Actual)
    if ($a.Count -eq 0) { if ($b.Count -eq 0) { return 0.0 }; return 1.0 }
    $previous = @(0..$b.Count)
    for ($i=1; $i -le $a.Count; $i++) {
        $current = New-Object 'int[]' ($b.Count + 1); $current[0] = $i
        for ($j=1; $j -le $b.Count; $j++) {
            $cost = 1; if ($a[$i-1] -eq $b[$j-1]) { $cost = 0 }
            $current[$j] = [Math]::Min([Math]::Min($previous[$j]+1,$current[$j-1]+1),$previous[$j-1]+$cost)
        }
        $previous = $current
    }
    return [double]$previous[$b.Count] / $a.Count
}
function Quote-Argument([string]$Value) {
    # All path arguments below are absolute file/directory paths without trailing slash.
    if ($Value.Contains('"')) { throw 'Quotes are not permitted in paths' }
    return '"' + $Value + '"'
}
try {
    Push-Location $root
    try {
        & dotnet publish src/Somning.Engine -c Release -r win-x64 --self-contained -o publish/engine
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    } finally { Pop-Location }
    # A unique child prevents stale files or concurrent runs contaminating the deployed layout.
    $stage = Join-Path $env:TEMP ('somning-gate\' + [guid]::NewGuid().ToString('N'))
    $engine = Join-Path $stage 'engine'; $audio = Join-Path $stage 'in'
    New-Item -ItemType Directory -Path $engine,$audio -Force | Out-Null
    Copy-Item -Path (Join-Path $root 'publish\engine\*') -Destination $engine -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\tracer\hoje.wav') -Destination (Join-Path $audio 'hoje.wav')
    $expected = [IO.File]::ReadAllText((Join-Path $root 'tests\fixtures\tracer\hoje.txt'), $utf8)
    foreach ($backend in @('vulkan','cpu')) {
        $p = $null; $writer = $null
        try {
            $info = New-Object Diagnostics.ProcessStartInfo
            $info.FileName = Join-Path $engine 'Somning.Engine.exe'
            $info.WorkingDirectory = $stage
            $info.UseShellExecute = $false
            $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
            $info.StandardOutputEncoding = $utf8; $info.StandardErrorEncoding = $utf8
            $info.Arguments = '--backend ' + $backend + ' --model ' +
                (Quote-Argument (Join-Path $ModelsDir 'ggml-large-v3-turbo.bin')) + ' --vad ' +
                (Quote-Argument (Join-Path $ModelsDir 'ggml-silero-v6.2.0.bin')) +
                ' --flash-attn on --allowed-dir ' + (Quote-Argument $audio)
            $info.EnvironmentVariables['PATH'] = 'C:\Windows\system32;C:\Windows'
            $info.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $stage 'no-sdk'
            $info.EnvironmentVariables['DOTNET_ROOT_X64'] = Join-Path $stage 'no-sdk'
            $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
            foreach ($name in @('DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE','VULKAN_SDK','VK_LAYER_PATH')) {
                $info.EnvironmentVariables.Remove($name)
            }
            $p = New-Object Diagnostics.Process; $p.StartInfo = $info
            if (-not $p.Start()) { throw 'Worker failed to start' }
            $null = $p.Handle
            $errors = $p.StandardError.ReadToEndAsync()
            $writer = New-Object IO.StreamWriter($p.StandardInput.BaseStream, $utf8)
            $writer.AutoFlush = $true
            $ready = Read-Message $p.StandardOutput 120
            if ($ready.type -ne 'ready' -or $ready.backend -ne $backend) { throw 'Backend readiness mismatch' }
            if ($backend -eq 'vulkan' -and -not $ready.gpu_in_use) { throw 'Vulkan GPU not in use' }
            $request = @{type='transcribe';id='gate';wav_path=(Join-Path $audio 'hoje.wav');language='pt'}
            $writer.WriteLine(($request | ConvertTo-Json -Compress))
            $result = Read-Message $p.StandardOutput 180
            if ($result.type -ne 'result') { throw ('Transcription response: ' + ($result | ConvertTo-Json -Compress)) }
            if ($result.outcome -ne 'text' -or $result.backend -ne $backend) { throw 'Transcription failed' }
            $wer = Get-Wer $expected $result.text
            if ($wer -gt 0.10) { throw "WER too high: $wer" }
            # Build accent explicitly: PowerShell 5.1 reads BOM-less source using the system codepage.
            $accent = 'portugu' + [char]0xEA + 's'
            if (-not $result.text.Contains($accent)) { throw 'Portuguese accent did not round-trip' }
            $writer.WriteLine('{"type":"shutdown"}')
            if (-not $p.WaitForExit(10000)) { throw 'Shutdown timeout' }
            if ($p.ExitCode -ne 0) { throw "Worker exit $($p.ExitCode)" }
            $rows.Add("| $backend | $(Format-Number $ready.load_ms) | $(Format-Number $ready.warm_ms) | $(Format-Number $result.infer_ms) | $(Format-Number ($wer*100))% | PASS | $($result.text) |")
            Write-Host "GATE PASS $backend"
        } catch {
            $failed = $true
            $rows.Add("| $backend | - | - | - | - | FAIL | $($_.Exception.Message.Replace('|','/')) |")
        } finally {
            if ($p) {
                if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() }
                if ($writer) { $writer.Dispose() }
                $p.Dispose()
            }
        }
    }
} catch { $failed = $true; $rows.Add("| build | - | - | - | - | FAIL | $($_.Exception.Message.Replace('|','/')) |") }
$lines = @('# Windows 10 published engine gate', '',
    ('Date: ' + [DateTime]::UtcNow.ToString('o', $culture)),
    ('OS: ' + [Environment]::OSVersion.Version.ToString()), '',
    'Self-contained win-x64 directory publish copied to TEMP; PATH=C:\Windows\system32;C:\Windows; DOTNET_ROOT/DOTNET_ROOT_X64 nonexistent; DOTNET_MULTILEVEL_LOOKUP=0.',
    '', '| backend | load_ms | warm_ms | infer_ms | WER | status | transcript / failure |',
    '|---|---:|---:|---:|---:|---|---|') + @($rows.ToArray()) + @('',
    'Deployment prerequisites: natives import MSVCP140, VCRUNTIME140, VCRUNTIME140_1, VCOMP140 and vulkan-1.dll from System32. Phase 4 must ship the VC++ redistributable; the installed GPU driver supplies Vulkan.',
    'The transcript is an explicit regression fixture, retained here as gate evidence; operational logs exclude transcript text.')
if ($failed) { $lines += ''; $lines += 'D-16 re-check failed: execution stops; insert a native-rebuild phase' }
[IO.File]::WriteAllLines($report, [string[]]$lines, $utf8)
if ($failed) { Write-Host 'GATE FAIL'; exit 1 }
exit 0

