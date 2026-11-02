#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif

#ifndef Arch
  #define Arch "x64"
#endif
#if Arch == "arm64"
  #define PackageDir "package-arm64"
  #define OutputSuffix "-arm64"
#else
  #define PackageDir "package"
  #define OutputSuffix ""
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
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0
OutputDir=..\publish
OutputBaseFilename=Somning-{#AppVersion}{#OutputSuffix}-Setup
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
Source: "..\publish\{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Somning"; Filename: "{app}\Somning.App.exe"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userprograms}\Somning - Baixar modelos"; Filename: "{app}\Baixar-modelos.cmd"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userdesktop}\Somning"; Filename: "{app}\Somning.App.exe"; Tasks: desktopicon; Check: not WizardSilent

[Run]
Filename: "{app}\Somning.App.exe"; Description: "Abrir Somning"; Flags: nowait postinstall skipifsilent

[Code]
const
  ModelsArguments = '-Only ggml-large-v3-turbo-q5_0.bin,ggml-large-v3-turbo-q8_0.bin,ggml-silero-v6.2.0.bin';

{ Somning is a tray app: it has no window for Windows Restart Manager to close, so silent installs (and updates over a
  running copy) aborted with "applications could not be shut down". Stop the app and its engine ourselves, before any
  file is copied, so both silent and interactive installs and uninstalls replace a running copy cleanly. }
procedure StopRunningSomning();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM Somning.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM Somning.Engine.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { Give Windows a moment to release the file handles of the stopped processes. }
  Sleep(800);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningSomning();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then StopRunningSomning();
end;

{ Runs fetch-models.ps1 in a visible window so the download progress can be followed. Returns True on success. }
function FetchModels(): Boolean;
var
  ExitCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\fetch-models.ps1') + '" ' + ModelsArguments,
      '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then
    Result := (ExitCode = 0);
end;

{ Models are downloaded after the program files are in place. A failed download offers a retry; the partial file
  is kept, so a retry resumes. If the person gives up, the program stays installed and the models can be fetched later. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  { /SEMMODELOS=1 skips the download (used by automated tests). }
  if (CurStep = ssPostInstall) and (ExpandConstant('{param:SEMMODELOS|0}') = '0') then
  begin
    WizardForm.StatusLabel.Caption := 'Baixando os modelos de voz (cerca de 1,45 GB). Acompanhe a barra de progresso na janela que abriu.';
    if WizardSilent then
    begin
      { Silent installs get one attempt and no prompts: a prompt could hang or answer itself on retry. }
      if not FetchModels() then
        Log('Model download failed during silent install. Use the Somning - Baixar modelos shortcut.');
    end
    else
    begin
      while not FetchModels() do
      begin
        if MsgBox('Não foi possível baixar os modelos de voz. Confira a conexão com a internet e o espaço em disco.' + #13#10#13#10 +
                  'Tentar novamente? O download continua de onde parou.', mbError, MB_YESNO) = IDNO then
        begin
          MsgBox('O Somning foi instalado, mas os modelos não foram baixados. Para ditar, use o atalho "Somning - Baixar modelos" no Menu Iniciar.',
                 mbInformation, MB_OK);
          Break;
        end;
      end;
    end;
  end;
end;

{ Only installed program files are removed. Models, logs, settings and recordings
  live outside the application directory and survive uninstall. }
