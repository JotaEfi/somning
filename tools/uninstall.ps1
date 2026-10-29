param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Somning\app'),
    [switch]$PurgeData = $false,
    [switch]$Quiet = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info([string]$Message) {
    if (-not $Quiet) { Write-Host $Message -ForegroundColor Cyan }
}

function Write-Success([string]$Message) {
    if (-not $Quiet) { Write-Host $Message -ForegroundColor Green }
}

$target = [System.IO.Path]::GetFullPath($InstallDir)

# Require our installation marker (or the complete legacy layout) before recursive removal.
if (Test-Path -LiteralPath $target) {
    if ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'InstallDir cannot be a junction or symbolic link.' }
    $marker = Join-Path $target 'installation.json'
    if (Test-Path -LiteralPath $marker) {
        $installed = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($installed.Product -ne 'Somning' -or $installed.InstallDirectory -ine $target) { throw 'Installation marker does not match the requested directory.' }
    } elseif (-not ((Test-Path -LiteralPath (Join-Path $target 'Somning.App.exe')) -and
                   (Test-Path -LiteralPath (Join-Path $target 'engine\Somning.Engine.exe')) -and
                   (Test-Path -LiteralPath (Join-Path $target 'uninstall.ps1')))) {
        throw 'Not a recognized Somning installation; refusing recursive removal.'
    }
}
$targetPrefix = $target.TrimEnd([char]92) + '\'

Write-Info "=== Desinstalador do Somning ==="
Write-Info "Diretório da aplicação: $target"

# 1. Finalizar processos em execução
$running = Get-Process -Name 'Somning.App','Somning.Engine' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Write-Info "Encerrando instâncias ativas do Somning..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 2. Remover atalhos (somente os que apontam para esta instalação)
function Test-OwnedShortcut([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($Path)
        return [bool]($link.TargetPath -and $link.TargetPath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase))
    } catch { return $false }
}
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
foreach ($shortcut in (Join-Path $programs 'Somning.lnk'), (Join-Path $desktop 'Somning.lnk')) {
    if (Test-OwnedShortcut $shortcut) {
        Write-Info "Removendo atalho $shortcut..."
        Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
    }
}

# 3. Remover autostart (somente se apontar para esta instalação)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = try { [string](Get-ItemPropertyValue -Path $runKey -Name 'Somning' -ErrorAction Stop) } catch { $null }
if ($runValue -and $runValue.TrimStart('"').StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Info "Removendo chave de inicialização com o Windows..."
    Remove-ItemProperty -Path $runKey -Name 'Somning' -ErrorAction SilentlyContinue
}

# 4. Remover arquivos da aplicação
if (Test-Path -LiteralPath $target) {
    Write-Info "Removendo binários e bibliotecas..."
    # Se o desinstalador estiver rodando de dentro de $target, pode haver bloqueio do próprio script;
    # contornamos removendo itens filhos primeiro
    $currentScript = $PSCommandPath
    Get-ChildItem -LiteralPath $target | Where-Object { $_.FullName -ne $currentScript } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    # Tenta remover o diretório
    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $target) {
        Write-Warning "Nao foi possivel remover totalmente $target (arquivos em uso?)."
    }
}

# 5. Expurgar dados se solicitado
$dataRoot = Join-Path $env:LOCALAPPDATA 'Somning'
if ($PurgeData) {
    if (Test-Path -LiteralPath $dataRoot) {
        Write-Info "Expurgando histórico, gravações, configurações e modelos..."
        Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Info "Dados do usuário (gravações, logs e configurações) mantidos em $dataRoot"
}

Write-Success "`nSomning foi desinstalado com sucesso."
