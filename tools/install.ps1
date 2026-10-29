param(
    [string]$SourceDir,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Somning\app'),
    [switch]$StartMenuShortcut = $true,
    [switch]$DesktopShortcut = $false,
    [switch]$StartWithWindows = $false,
    [switch]$LaunchAfterInstall = $false,
    [switch]$Unattended = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info([string]$Message) {
    if (-not $Unattended) { Write-Host $Message -ForegroundColor Cyan }
}

function Write-Success([string]$Message) {
    if (-not $Unattended) { Write-Host $Message -ForegroundColor Green }
}

# Resolve source directory
if (-not $SourceDir) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Somning.App.exe')) {
        $SourceDir = $PSScriptRoot
    } elseif (Test-Path -LiteralPath (Join-Path $PSScriptRoot '..\publish\package\Somning.App.exe')) {
        $SourceDir = Join-Path $PSScriptRoot '..\publish\package'
    } else {
        # Check if we can build the package
        $packageScript = Join-Path $PSScriptRoot 'package.ps1'
        if (Test-Path -LiteralPath $packageScript) {
            Write-Info "Pacote não encontrado. Executando empacotamento prévio..."
            & $packageScript
            $SourceDir = Join-Path $PSScriptRoot '..\publish\package'
        } else {
            throw "Diretório de origem do pacote não pôde ser localizado."
        }
    }
}

$source = [System.IO.Path]::GetFullPath($SourceDir)
$target = [System.IO.Path]::GetFullPath($InstallDir)

# Validate before stopping processes or copying into an arbitrary custom location.
$sourcePrefix = $source.TrimEnd([char]92) + '\'
$targetPrefix = $target.TrimEnd([char]92) + '\'
if ($sourcePrefix.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $targetPrefix.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and install directories must be separate, non-nested directories.'
}
if (Test-Path -LiteralPath $target) {
    if ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'InstallDir cannot be a junction or symbolic link.' }
    if (@(Get-ChildItem -LiteralPath $target -Force).Count -gt 0 -and
        -not (Test-Path -LiteralPath (Join-Path $target 'Somning.App.exe'))) {
        throw 'InstallDir must be empty or an existing Somning installation.'
    }
}

Write-Info "=== Instalador do Somning ==="
Write-Info "Origem: $source"
Write-Info "Destino: $target"

$sourceApp = Join-Path $source 'Somning.App.exe'
$sourceEngine = Join-Path $source 'engine\Somning.Engine.exe'
if (-not (Test-Path -LiteralPath $sourceApp)) {
    throw "Arquivo executável principal não encontrado em $source"
}
if (-not (Test-Path -LiteralPath $sourceEngine)) {
    throw "Arquivo executável do engine não encontrado em $source\engine"
}

# 1. Parar instâncias em execução
$running = Get-Process -Name 'Somning.App','Somning.Engine' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Write-Info "Encerrando instâncias em execução..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 2. Criar diretório de destino e copiar binários
Write-Info "Copiando arquivos da aplicação..."
if (-not (Test-Path -LiteralPath $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

if ($source.TrimEnd([char]92) -ieq $target.TrimEnd([char]92)) {
    throw "Origem e destino sao o mesmo diretorio: $source"
}

# Copia arquivos preservando subdiretorios (ex: engine\). LiteralPath evita que '[' ou ']' no caminho sejam tratados como curingas.
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse -Force

# Layout de dados do usuario (preservado pelo desinstalador)
$dataRoot = Join-Path $env:LOCALAPPDATA 'Somning'
foreach ($sub in 'models','recordings','logs') {
    New-Item -ItemType Directory -Path (Join-Path $dataRoot $sub) -Force | Out-Null
}

# Garantir que o uninstall.ps1 está presente no diretório de destino
$uninstallSource = Join-Path $PSScriptRoot 'uninstall.ps1'
if (-not (Test-Path -LiteralPath $uninstallSource)) {
    $uninstallSource = Join-Path $source 'uninstall.ps1'
}
if (Test-Path -LiteralPath $uninstallSource) {
    Copy-Item -LiteralPath $uninstallSource -Destination (Join-Path $target 'uninstall.ps1') -Force
}

# 3. Criar Atalhos
# WScript.Shell usa APIs ANSI e falha/corrompe caminhos com caracteres fora da code page (ex: nomes de usuario
# acentuados ou CJK). Usamos IShellLinkW (Unicode) via interop COM.
if (-not ('Somning.ShellLinkWriter' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace Somning {
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
    public static class ShellLinkWriter {
        public static void Create(string lnkPath, string target, string workingDir, string description) {
            IShellLinkW link = (IShellLinkW)new ShellLink();
            link.SetPath(target);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(lnkPath, true);
            Marshal.FinalReleaseComObject(link);
        }
        public static string GetTarget(string lnkPath) {
            IShellLinkW link = (IShellLinkW)new ShellLink();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(lnkPath, 0);
            var sb = new System.Text.StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            Marshal.FinalReleaseComObject(link);
            return sb.ToString();
        }
    }
}
"@
}

$description = 'Somning - Ditado por voz pt-BR com Whisper'
$installedApp = Join-Path $target 'Somning.App.exe'

if ($StartMenuShortcut) {
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $shortcutPath = Join-Path $programs 'Somning.lnk'
    Write-Info "Criando atalho no Menu Iniciar: $shortcutPath"
    [Somning.ShellLinkWriter]::Create($shortcutPath, $installedApp, $target, $description)
}

if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
    $shortcutPath = Join-Path $desktop 'Somning.lnk'
    Write-Info "Criando atalho na Area de Trabalho: $shortcutPath"
    [Somning.ShellLinkWriter]::Create($shortcutPath, $installedApp, $target, $description)
}

# 4. Iniciar com o Windows (DIST-02)
if ($StartWithWindows) {
    Write-Info "Configurando início automático com o Windows (HKCU Run)..."
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $runKey -Name 'Somning' -Value "`"$installedApp`" --tray"
}

# 5. Marcador da instalação (usado pelo desinstalador para validar o diretório).
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($installedApp).ProductVersion
if (-not $version) { $version = '1.0.0' }
@{ Product = 'Somning'; InstallDirectory = $target; Version = $version } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'installation.json') -Encoding UTF8

# 6. Inicialização pós-instalação se solicitado
if ($LaunchAfterInstall) {
    Write-Info "Iniciando Somning..."
    Start-Process -FilePath $installedApp -ArgumentList '--tray' -WorkingDirectory $target -WindowStyle Hidden
}

Write-Success "`nSomning instalado com sucesso em $target"
