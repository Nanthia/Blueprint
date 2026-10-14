; Blueprint installer (Inno Setup 6). Per-user install, no admin rights needed.
#define AppName "Blueprint"
#define AppVersion "1.1.0"
#define AppExe "Blueprint.exe"

[Setup]
AppId={{B7C3E2A4-5D1F-4F7B-9A3E-6C2D8E0F1A57}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Blueprint
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=Blueprint-Setup-{#AppVersion}
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
VersionInfoDescription=Blueprint screenshot tool

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Blueprint when I sign in to Windows"; GroupDescription: "Startup:"

[InstallDelete]
; leftovers from when the app was called Shutter
Type: files; Name: "{app}\Shutter.exe"
Type: files; Name: "{autoprograms}\Shutter.lnk"
Type: files; Name: "{autodesktop}\Shutter.lnk"

[Files]
Source: "..\dist-sc\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Blueprint"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Blueprint"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillBlueprint"

[Code]
procedure StopBlueprint();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Blueprint.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Shutter.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeSetup(): Boolean;
begin
  StopBlueprint();
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopBlueprint();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // the app can also add its own startup entry from Preferences
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Blueprint');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Shutter');
    Dir := ExpandConstant('{userappdata}\Blueprint');
    if (not UninstallSilent) and DirExists(Dir) and (MsgBox('Also remove Blueprint settings and the screenshot search index?' + #13#10 +
        'Your saved screenshots are never touched.', mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(Dir, True, True, True);
  end;
end;
