param(
    [string]$Compiler,
    [ValidateSet('x64','arm64')][string]$Arch = 'x64'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packageDir = if ($Arch -eq 'x64') { 'package' } else { "package-$Arch" }
$manifestPath = Join-Path $root "publish/$packageDir/package-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Run tools/package.ps1 -Arch $Arch first." }
$manifest = Get-Content -LiteralPath $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
if ($manifest.Harness -or $manifest.Configuration -ne 'Release') { throw 'Setup requires a Release package without harness.' }
if ($manifest.Runtime -ne "win-$Arch") { throw "The package at publish/$packageDir is $($manifest.Runtime), not win-$Arch." }
if (-not $Compiler) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler) { throw 'Install Inno Setup 6 or pass -Compiler with the ISCC.exe path.' }
& $Compiler "/DAppVersion=$($manifest.Version)" "/DArch=$Arch" (Join-Path $PSScriptRoot 'Somning.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$suffix = if ($Arch -eq 'x64') { '' } else { "-$Arch" }
$setup = Join-Path $root "publish/Somning-$($manifest.Version)$suffix-Setup.exe"
$digest = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
Write-Host "Setup ready: $setup"
Write-Host "SHA256: $digest"
