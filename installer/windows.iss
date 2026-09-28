#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{AB26A0BE-AF98-4CB6-9DF5-414A3F9C1900}
AppName=AI Usage Widget
AppVersion={#AppVersion}
AppPublisher=wernerong
AppPublisherURL=https://github.com/wernerong/ai-usage-widget
AppSupportURL=https://github.com/wernerong/ai-usage-widget/issues
DefaultDirName={localappdata}\Programs\AIUsageWidget
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\source\Assets\widget.ico
UninstallDisplayIcon={app}\AIUsageWidget.exe
OutputDir={#OutputDir}
OutputBaseFilename=AIUsageWidget-{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=no

[Tasks]
Name: startup; Description: "Start AI Usage Widget when I sign in"; GroupDescription: "Options:"
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\AI Usage Widget"; Filename: "{app}\AIUsageWidget.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\AI Usage Widget"; Filename: "{app}\AIUsageWidget.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AIUsageWidget"; ValueData: """{app}\AIUsageWidget.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "AIUsageWidget"; Tasks: not startup; Flags: deletevalue uninsdeletevalue

[Run]
Filename: "{app}\AIUsageWidget.exe"; Description: "Launch AI Usage Widget"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  // Preserve startup preference when upgrading a script or installer deployment.
  if FileExists(ExpandConstant('{localappdata}\Programs\AIUsageWidget\AIUsageWidget.exe')) then
  begin
    if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AIUsageWidget') then
      WizardSelectTasks('startup')
    else
      WizardSelectTasks('!startup');
  end;
end;
