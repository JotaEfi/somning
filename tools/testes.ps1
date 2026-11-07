<#
.SYNOPSIS
    Roda os testes do Somning escolhendo onde rodar os testes de área de trabalho.

.DESCRIPTION
    Os testes marcados com Category=Desktop usam a área de trabalho de verdade: colocam janelas em primeiro plano,
    apertam o atalho, colam com Ctrl+V e usam a área de transferência. Se você mexer no PC enquanto eles rodam,
    vocês dois disputam teclado e foco e os testes falham à toa.

    1  Local, sem os testes de área de trabalho. Pode usar o PC à vontade enquanto roda.
    2  Igual ao 1 e, em paralelo, os testes de área de trabalho numa máquina Windows do GitHub Actions.
       Requer o gh autenticado e o commit atual enviado (push) ao GitHub. Lá não há GPU, então os testes
       de desempenho (Category=Performance: metas de latência e memória) ficam de fora; só a opção 3 os roda.
    3  Local, todos os testes. Use só quando estiver longe do computador.

.EXAMPLE
    powershell -NoProfile -File tools/testes.ps1            # pergunta qual opção
    powershell -NoProfile -File tools/testes.ps1 -Opcao 2
#>
param(
    [ValidateSet('1', '2', '3')][string]$Opcao
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$integration = Join-Path $root 'tests\Somning.Integration.Tests\bin\Release\net10.0-windows\Somning.Integration.Tests.exe'
$core = Join-Path $root 'tests\Somning.Core.Tests\bin\Release\net10.0\Somning.Core.Tests.exe'
$workflow = 'testes-desktop.yml'

function Read-Opcao {
    Write-Host ''
    Write-Host 'Como rodar os testes de área de trabalho (foco, teclado, área de transferência)?'
    Write-Host '  1  Pular: rodar só o resto, localmente (pode usar o PC)'
    Write-Host '  2  No GitHub Actions, em paralelo com o resto local (pode usar o PC; sem os testes de desempenho)'
    Write-Host '  3  Localmente, junto com o resto (NÃO use o PC até terminar)'
    while ($true) {
        $answer = (Read-Host 'Opção [1/2/3]').Trim()
        if ($answer -in '1', '2', '3') { return $answer }
        Write-Host 'Digite 1, 2 ou 3.'
    }
}

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "`n== $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name falhou (código $LASTEXITCODE)." }
}

function Start-GitHubDesktopRun {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'O gh (GitHub CLI) não está instalado.' }
    # Windows PowerShell 5.1 turns any native stderr line into a terminating error under 'Stop'.
    $ErrorActionPreference = 'Continue'
    $branch = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
    $head = (git -C $root rev-parse HEAD).Trim()
    git -C $root fetch --quiet origin $branch 2>&1 | Out-Null
    $remote = git -C $root rev-parse --verify --quiet "origin/$branch" 2>&1 | Select-Object -First 1
    $remote = if ($LASTEXITCODE -eq 0 -and $remote) { "$remote".Trim() } else { '' }
    if ($remote -ne $head) {
        throw "O commit atual ($($head.Substring(0, 7))) não está no GitHub. Dê push em '$branch' antes de usar a opção 2."
    }
    if (git -C $root status --porcelain) {
        Write-Warning 'Há alterações não commitadas: o GitHub vai testar só o que já está no commit enviado.'
    }
    $before = Get-Date
    gh workflow run $workflow --ref $branch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível disparar o workflow no GitHub.' }
    # The run appears a few seconds after dispatch; pick the newest one created after we asked.
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        $runs = gh run list --workflow $workflow --branch $branch --event workflow_dispatch --limit 5 --json databaseId,createdAt,url | ConvertFrom-Json
        $run = $runs | Where-Object { [DateTime]$_.createdAt -ge $before.ToUniversalTime().AddSeconds(-5) } | Select-Object -First 1
        if ($run) {
            Write-Host "Testes de área de trabalho rodando no GitHub: $($run.url)"
            return $run.databaseId
        }
    }
    throw 'O workflow foi disparado, mas a execução não apareceu no GitHub.'
}

if (-not $Opcao) { $Opcao = Read-Opcao }

$githubRun = $null
if ($Opcao -eq '2') { $githubRun = Start-GitHubDesktopRun }

Invoke-Step 'Compilar (Release)' { dotnet build (Join-Path $root 'Somning.sln') -c Release --nologo -v quiet }
# Debug: the integration tests launch the app and engine built with the test harness.
Invoke-Step 'Compilar (Debug)' { dotnet build (Join-Path $root 'Somning.sln') -c Debug --nologo -v quiet }
Invoke-Step 'Testes do Core' { & $core -noColor }

$filter = if ($Opcao -eq '3') { @() } else { @('-notrait', 'Category=Desktop') }
$localFailed = $false
try {
    Invoke-Step 'Testes de integração' { & $integration -noColor -longRunning 120 @filter }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    $localFailed = $true
}

$githubFailed = $false
if ($githubRun) {
    Write-Host "`n== Aguardando os testes de área de trabalho no GitHub" -ForegroundColor Cyan
    gh run watch $githubRun --exit-status --interval 30
    if ($LASTEXITCODE -ne 0) {
        $githubFailed = $true
        Write-Host 'Testes de área de trabalho falharam no GitHub. Detalhes:' -ForegroundColor Red
        gh run view $githubRun --log-failed | Select-Object -Last 60
    }
}

Write-Host ''
if ($localFailed -or $githubFailed) {
    Write-Host 'Resultado: FALHOU.' -ForegroundColor Red
    exit 1
}
if ($Opcao -eq '1') { Write-Host 'Resultado: aprovado (testes de área de trabalho não rodaram).' -ForegroundColor Green }
else { Write-Host 'Resultado: aprovado.' -ForegroundColor Green }
exit 0
