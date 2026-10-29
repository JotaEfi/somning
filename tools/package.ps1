param(
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [string]$OutputDir,
    [switch]$WithHarness
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishRoot = [IO.Path]::GetFullPath((Join-Path $root 'publish')).TrimEnd([char]92)
if (-not $OutputDir) {
    $leaf = if ($WithHarness) { 'package-harness' } else { 'package' }
    $OutputDir = Join-Path $publishRoot $leaf
}
$output = [IO.Path]::GetFullPath($OutputDir).TrimEnd([char]92)
if (-not $output.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDir must be a child of this checkout publish directory; refusing recursive cleanup elsewhere.'
}
$ancestor = $output
while ($ancestor -and $ancestor.Length -ge $publishRoot.Length) {
    if (Test-Path -LiteralPath $ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing reparse point in package output: $ancestor"
        }
    }
    $ancestor = Split-Path $ancestor -Parent
}
if ($Configuration -ne 'Release' -and -not $WithHarness) { throw 'Distribution packages require Release configuration.' }
if ($WithHarness) { Write-Warning 'TEST ONLY: this package exposes the test harness; do not distribute.' }
$harnessValue = if ($WithHarness) { 'true' } else { 'false' }
$engineOutput = Join-Path $output 'engine'
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if (-not $version) { throw 'Version is missing from Directory.Build.props.' }
Write-Host "Packaging Somning $version ($Configuration), output: $output"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output,$engineOutput -Force | Out-Null
Push-Location $root
try {
    & dotnet publish src/Somning.App -c $Configuration -r win-x64 --self-contained -o $output "-p:SomningHarness=$harnessValue" -p:PublishSingleFile=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "App publish failed: $LASTEXITCODE" }
    & dotnet publish src/Somning.Engine -c $Configuration -r win-x64 --self-contained -o $engineOutput "-p:SomningHarness=$harnessValue" -p:PublishSingleFile=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Engine publish failed: $LASTEXITCODE" }
} finally { Pop-Location }
foreach ($script in 'install.ps1','uninstall.ps1','fetch-models.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $output -Force
}
foreach ($doc in 'README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md') {
    $path = Join-Path $root $doc
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $output -Force }
}
$docsOutput = Join-Path $output 'docs'
New-Item -ItemType Directory -Path $docsOutput -Force | Out-Null
foreach ($doc in 'PRIVACIDADE.md','INSTALACAO.md','RELEASE-1.0.md') {
    $path = Join-Path $root "docs\$doc"
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $docsOutput -Force }
}
@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -LaunchAfterInstall
pause
'@ | Set-Content -LiteralPath (Join-Path $output 'Instalar.cmd') -Encoding ASCII
@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-models.ps1" -Only ggml-large-v3-turbo-q5_0.bin,ggml-silero-v6.2.0.bin
pause
'@ | Set-Content -LiteralPath (Join-Path $output 'Baixar-modelos.cmd') -Encoding ASCII
$appExe = Join-Path $output 'Somning.App.exe'
$engineExe = Join-Path $engineOutput 'Somning.Engine.exe'
if (-not (Test-Path -LiteralPath $appExe) -or -not (Test-Path -LiteralPath $engineExe)) { throw 'Missing app or engine executable.' }
$manifest = [ordered]@{
    Package = 'Somning'; Version = $version; Configuration = $Configuration; Runtime = 'win-x64'
    Harness = [bool]$WithHarness; BuiltAt = [DateTime]::UtcNow.ToString('o')
    AppExecutable = 'Somning.App.exe'; EngineExecutable = 'engine\Somning.Engine.exe'
    ModelsIncluded = $false; AuthenticodeSigned = $false
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Encoding UTF8
$suffix = if ($WithHarness) { '-harness' } else { '' }
$zip = Join-Path $publishRoot "Somning-$version-win-x64$suffix.zip"
Compress-Archive -LiteralPath $output -DestinationPath $zip -Force -CompressionLevel Optimal
$digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host "Package ready: $zip"
Write-Host "SHA256: $digest"
