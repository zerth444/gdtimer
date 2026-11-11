#define AppName "GDTimer"
#define AppVersion "1.0.0"
#define AppPublisher "Zerth"
#define AppExeName "GDTimer.exe"

[Setup]
AppId={{8F3A2C41-9D7B-4E56-A1C2-5B7E9D0F3A64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\GDTimer
DefaultGroupName=GDTimer
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=LICENSE.txt
OutputDir=dist
OutputBaseFilename=GDTimer-Setup-{#AppVersion}
SetupIconFile=icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=GDTimer_SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Start {#AppName} with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "fonts\*.ttf"; DestDir: "{app}\fonts"; Flags: ignoreversion
Source: "fonts\OFL.txt"; DestDir: "{app}\fonts"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('autostart') then
    RegWriteStringValue(HKCU, RunKey, '{#AppName}',
      '"' + ExpandConstant('{app}\{#AppExeName}') + '"');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  value: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKCU, RunKey, '{#AppName}', value) then
      if Pos(ExpandConstant('{app}'), value) > 0 then
        RegDeleteValue(HKCU, RunKey, '{#AppName}');
  end;
end;
