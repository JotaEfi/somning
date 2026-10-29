param(
    [string]$Configuration = 'Release',
    [string]$OutputDir,
    [switch]$WithHarness
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) {
    $leaf = if ($WithHarness) { 'package-harness' } else { 'package' }
    $OutputDir = Join-Path $root "publish\$leaf"
}
$harnessArgs = @()
if ($WithHarness) {
    $harnessArgs = @('-p:SomningHarness=true')
    Write-Host "WARNING: harness package exposes the somning-test pipe. FOR TEST USE ONLY - never install or distribute it." -ForegroundColor Red
}
$output = [System.IO.Path]::GetFullPath($OutputDir)
$engineOutput = Join-Path $output 'engine'

Write-Host "=== Packaging Somning ($Configuration) ===" -ForegroundColor Cyan
Write-Host "Target output: $output"

if (Test-Path -LiteralPath $output) {
    Write-Host "Cleaning existing package directory..."
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Path $output,$engineOutput -Force | Out-Null

Push-Location $root
try {
    Write-Host "`n[1/4] Publishing Somning.App (win-x64 self-contained)..." -ForegroundColor Yellow
    & dotnet publish src/Somning.App -c $Configuration -r win-x64 --self-contained -o $output $harnessArgs --nologo
    if ($LASTEXITCODE -ne 0) { throw "Somning.App publish failed with exit code $LASTEXITCODE" }

    Write-Host "`n[2/4] Publishing Somning.Engine (win-x64 self-contained)..." -ForegroundColor Yellow
    & dotnet publish src/Somning.Engine -c $Configuration -r win-x64 --self-contained -o $engineOutput $harnessArgs --nologo
    if ($LASTEXITCODE -ne 0) { throw "Somning.Engine publish failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

Write-Host "`n[3/4] Copying deployment and management scripts..." -ForegroundColor Yellow
$installScriptSource = Join-Path $PSScriptRoot 'install.ps1'
$uninstallScriptSource = Join-Path $PSScriptRoot 'uninstall.ps1'

if (Test-Path -LiteralPath $installScriptSource) {
    Copy-Item -LiteralPath $installScriptSource -Destination (Join-Path $output 'install.ps1') -Force
}
if (Test-Path -LiteralPath $uninstallScriptSource) {
    Copy-Item -LiteralPath $uninstallScriptSource -Destination (Join-Path $output 'uninstall.ps1') -Force
}

# Verify key binary outputs
$appExe = Join-Path $output 'Somning.App.exe'
$engineExe = Join-Path $engineOutput 'Somning.Engine.exe'

if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Packaging error: Somning.App.exe was not found in $output"
}
if (-not (Test-Path -LiteralPath $engineExe)) {
    throw "Packaging error: Somning.Engine.exe was not found in $engineOutput"
}

Write-Host "`n[4/4] Writing package manifest..." -ForegroundColor Yellow
$manifest = @{
    Package = "Somning"
    Configuration = $Configuration
    Runtime = "win-x64"
    Harness = [bool]$WithHarness
    BuiltAt = [DateTime]::UtcNow.ToString("o")
    AppExecutable = "Somning.App.exe"
    EngineExecutable = "engine\Somning.Engine.exe"
    AppSizeMB = [Math]::Round((Get-Item $appExe).Length / 1MB, 2)
    EngineSizeMB = [Math]::Round((Get-Item $engineExe).Length / 1MB, 2)
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Encoding UTF8

Write-Host "`n=== Package Complete ===" -ForegroundColor Green
Write-Host "Somning packaged successfully at: $output"
