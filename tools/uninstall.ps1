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

Write-Info "=== Desinstalador do Somning ==="
Write-Info "Diretório da aplicação: $target"

# 1. Finalizar processos em execução
$running = Get-Process -Name 'Somning.App','Somning.Engine' -ErrorAction SilentlyContinue
if ($running) {
    Write-Info "Encerrando instâncias ativas do Somning..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 2. Remover atalhos
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$menuShortcut = Join-Path $programs 'Somning.lnk'
if (Test-Path -LiteralPath $menuShortcut) {
    Write-Info "Removendo atalho do Menu Iniciar..."
    Remove-Item -LiteralPath $menuShortcut -Force -ErrorAction SilentlyContinue
}

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$desktopShortcut = Join-Path $desktop 'Somning.lnk'
if (Test-Path -LiteralPath $desktopShortcut) {
    Write-Info "Removendo atalho da Área de Trabalho..."
    Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
}

# 3. Remover registro de autostart
Write-Info "Removendo chave de inicialização com o Windows..."
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Somning' -ErrorAction SilentlyContinue

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
