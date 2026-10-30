#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif

[Setup]
AppId={{C95EB989-2373-476E-924E-E2113706B938}
AppName=Somning
AppVersion={#AppVersion}
AppPublisher=JotaEfi
AppPublisherURL=https://github.com/JotaEfi/somning
AppSupportURL=https://github.com/JotaEfi/somning/issues
DefaultDirName={localappdata}\Somning\app
DefaultGroupName=Somning
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\publish
OutputBaseFilename=Somning-{#AppVersion}-Setup
SetupIconFile=..\src\Somning.Platform.Windows\Branding\somning.ico
UninstallDisplayIcon={app}\Somning.App.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked

[Files]
Source: "fetch-models.ps1"; Flags: dontcopy
Source: "..\publish\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Somning"; Filename: "{app}\Somning.App.exe"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userprograms}\Somning - Baixar modelos"; Filename: "{app}\Baixar-modelos.cmd"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userdesktop}\Somning"; Filename: "{app}\Somning.App.exe"; Tasks: desktopicon; Check: not WizardSilent

[Run]
Filename: "{app}\Somning.App.exe"; Description: "Abrir Somning"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Args: String;
begin
  Result := '';
  WizardForm.PreparingLabel.Caption := 'Baixando e verificando modelos de voz (aproximadamente 1,45 GB). Aguarde; a primeira instalação pode demorar.';
  WizardForm.PreparingLabel.Update;
  ExtractTemporaryFile('fetch-models.ps1');
  Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\fetch-models.ps1') +
    '" -Only ggml-large-v3-turbo-q5_0.bin,ggml-large-v3-turbo-q8_0.bin,ggml-silero-v6.2.0.bin';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Não foi possível iniciar o download dos modelos. Verifique o PowerShell e tente novamente.'
  else if ExitCode <> 0 then
    Result := 'Não foi possível baixar ou verificar os modelos de voz. Confira a conexão com a internet, o espaço em disco e tente instalar novamente.';
end;

{ Only installed program files are removed. Models, logs, settings and recordings
  live outside the application directory and survive uninstall. }
