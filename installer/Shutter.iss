; Shutter installer (Inno Setup 6). Per-user install, no admin rights needed.
#define AppName "Shutter"
#define AppVersion "1.0.1"
#define AppExe "Shutter.exe"

[Setup]
AppId={{B7C3E2A4-5D1F-4F7B-9A3E-6C2D8E0F1A57}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Shutter
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=Shutter-Setup-{#AppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Shutter screenshot tool

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Shutter when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\dist-sc\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Shutter"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Shutter"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillShutter"

[Code]
procedure StopShutter();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Shutter.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeSetup(): Boolean;
begin
  StopShutter();
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopShutter();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // the app can also add its own startup entry from Preferences
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Shutter');
    Dir := ExpandConstant('{userappdata}\Shutter');
    if (not UninstallSilent) and DirExists(Dir) and (MsgBox('Also remove Shutter settings and the screenshot search index?' + #13#10 +
        'Your saved screenshots are never touched.', mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(Dir, True, True, True);
  end;
end;
