param([string]$Compiler)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $root 'publish/package/package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Run tools/package.ps1 first.' }
$manifest = Get-Content -LiteralPath $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
if ($manifest.Harness -or $manifest.Configuration -ne 'Release') { throw 'Setup requires a Release package without harness.' }
if (-not $Compiler) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler) { throw 'Install Inno Setup 6 or pass -Compiler with the ISCC.exe path.' }
& $Compiler "/DAppVersion=$($manifest.Version)" (Join-Path $PSScriptRoot 'Somning.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$setup = Join-Path $root "publish/Somning-$($manifest.Version)-Setup.exe"
$digest = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
Write-Host "Setup ready: $setup"
Write-Host "SHA256: $digest"
