#ifndef AppVersion
  #define AppVersion "1.0.7"
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

[Registry]
; The app writes this value itself when "Iniciar com o Windows" is on; remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Somning"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Somning.App.exe"; Description: "Abrir Somning"; Flags: nowait postinstall skipifsilent
; Silent update started by the app itself (/RELAUNCH=1): open Somning again when it finishes.
Filename: "{app}\Somning.App.exe"; Parameters: "--tray"; Flags: nowait; Check: ShouldRelaunch

[Code]
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

function ShouldRelaunch(): Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
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

{ Models are downloaded by the installer itself (built-in download page with progress bar and SHA-256 check): no
  PowerShell window ever opens during setup. Files already present and complete are skipped. }
const
  ModelsBase = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/';
  VadUrl = 'https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin';
  Q5Name = 'ggml-large-v3-turbo-q5_0.bin';
  Q5Hash = '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2';
  Q8Name = 'ggml-large-v3-turbo-q8_0.bin';
  Q8Hash = '317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1';
  VadName = 'ggml-silero-v6.2.0.bin';
  VadHash = '2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987';

var
  ModelsPage: TDownloadWizardPage;

function ModelsDir(): String;
begin
  Result := ExpandConstant('{localappdata}\Somning\models');
end;

function ModelPresent(Name, Hash: String): Boolean;
begin
  Result := FileExists(ModelsDir() + '\' + Name) and (GetSHA256OfFile(ModelsDir() + '\' + Name) = Hash);
end;

function OnModelProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard();
begin
  ModelsPage := CreateDownloadPage('Modelos de voz',
    'Baixando os modelos de transcrição (cerca de 1,45 GB). Pode levar alguns minutos.', @OnModelProgress);
end;

procedure QueueModel(Url, Name, Hash: String);
begin
  if not ModelPresent(Name, Hash) then ModelsPage.Add(Url, Name, Hash);
end;

function DownloadModels(): Boolean;
begin
  Result := False;
  ModelsPage.Clear;
  QueueModel(ModelsBase + Q5Name, Q5Name, Q5Hash);
  QueueModel(ModelsBase + Q8Name, Q8Name, Q8Hash);
  QueueModel(VadUrl, VadName, VadHash);
  ModelsPage.Show;
  try
    try
      ModelsPage.Download;
      Result := True;
    except
      Log('Model download failed: ' + GetExceptionMessage);
    end;
  finally
    ModelsPage.Hide;
  end;
end;

{ Moves downloaded files from the temp folder into the models folder. }
function MoveModel(Name, Hash: String): Boolean;
var
  SourceFile, TargetFile: String;
begin
  Result := False;
  if ModelPresent(Name, Hash) then
  begin
    Result := True;
    Exit;
  end;
  SourceFile := ExpandConstant('{tmp}\' + Name);
  TargetFile := ModelsDir() + '\' + Name;
  if not FileExists(SourceFile) then Exit;
  ForceDirectories(ModelsDir());
  if FileExists(TargetFile) and not DeleteFile(TargetFile) then
  begin
    Log('Could not replace model file: ' + TargetFile);
    Exit;
  end;
  if not RenameFile(SourceFile, TargetFile) then
  begin
    if not CopyFile(SourceFile, TargetFile, False) then
    begin
      Log('Could not move or copy model file: ' + Name);
      Exit;
    end;
  end;
  if not FileExists(TargetFile) then
  begin
    Log('Model file is missing after saving: ' + Name);
    Exit;
  end;
  Result := GetSHA256OfFile(TargetFile) = Hash;
  if not Result then
  begin
    Log('Model verification failed after saving: ' + Name);
    DeleteFile(TargetFile);
  end;
end;

function InstallDownloadedModels(): Boolean;
begin
  Result := MoveModel(Q5Name, Q5Hash);
  if not Result then Exit;
  Result := MoveModel(Q8Name, Q8Hash);
  if not Result then Exit;
  Result := MoveModel(VadName, VadHash);
end;

const
  ManualTip = 'Para baixar depois, abra o Menu Iniciar e clique em "Somning - Baixar modelos". ' +
    'Se preferir o PowerShell, cole este comando:' + #13#10#13#10 +
    'powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Somning\app\fetch-models.ps1" -Only ggml-large-v3-turbo-q5_0.bin,ggml-large-v3-turbo-q8_0.bin,ggml-silero-v6.2.0.bin';

{ Models are fetched after the program files are in place. A failed download offers a retry; if the person gives
  up, the program stays installed and the tip above explains how to fetch the models later. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  { /SEMMODELOS=1 skips the download (used by automated tests). }
  if (CurStep = ssPostInstall) and (ExpandConstant('{param:SEMMODELOS|0}') = '0') then
  begin
    if WizardSilent then
    begin
      { Silent installs get one attempt and no prompts: a prompt could hang or answer itself on retry. }
      if DownloadModels() then
      begin
        if not InstallDownloadedModels() then Log('Downloaded models could not be saved and verified. ' + ManualTip);
      end
      else Log('Model download failed during silent install. ' + ManualTip);
    end
    else
    begin
      while not DownloadModels() do
      begin
        if MsgBox('Não foi possível baixar os modelos de voz. Confira a conexão com a internet e o espaço em disco.' + #13#10#13#10 +
                  'Tentar novamente?', mbError, MB_YESNO) = IDNO then
        begin
          MsgBox('O Somning foi instalado, mas os modelos não foram baixados.' + #13#10#13#10 + ManualTip, mbInformation, MB_OK);
          Exit;
        end;
      end;
      if not InstallDownloadedModels() then
        MsgBox('Os modelos foram baixados, mas não puderam ser salvos ou verificados.' + #13#10#13#10 + ManualTip,
          mbError, MB_OK);
    end;
  end;
end;

{ Only installed program files are removed. Models, logs, settings and recordings
  live outside the application directory and survive uninstall. }
