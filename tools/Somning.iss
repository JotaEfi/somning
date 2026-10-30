#ifndef AppVersion
  #define AppVersion "1.0.1"
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
Source: "..\publish\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Somning"; Filename: "{app}\Somning.App.exe"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userprograms}\Somning - Baixar modelos"; Filename: "{app}\Baixar-modelos.cmd"; WorkingDir: "{app}"; Check: not WizardSilent
Name: "{userdesktop}\Somning"; Filename: "{app}\Somning.App.exe"; Tasks: desktopicon; Check: not WizardSilent

[Run]
Filename: "{app}\Somning.App.exe"; Description: "Abrir Somning"; Flags: nowait postinstall skipifsilent

; Only installed program files are removed. Models, logs, settings and recordings
; live outside {app} and must survive uninstall. No recursive UninstallDelete.
