#define MyAppName "Codex Limit Widget"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "CodexLimitWidget Contributors"
#define MyAppExeName "CodexLimitWidget.exe"
#define MyAppSourceDir "..\publish\release"
#define MyAppIconPath "..\icon.ico"
#define MyAppLicensePath "..\LICENSE"
#define DotNetDownloadURL "https://dotnet.microsoft.com/download/dotnet/10.0"

[Setup]
AppId={{D64D2C37-0B3E-4A5C-9F0C-0E8A0C5D1987}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=CodexLimitWidget-{#MyAppVersion}-Setup
SetupIconFile={#MyAppIconPath}
LicenseFile={#MyAppLicensePath}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppLicensePath}"; DestDir: "{app}"; DestName: "LICENSE"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function HasDotNet10DesktopRuntime: Boolean;
var
  BasePath: string;
  FindRec: TFindRec;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BasePath) then Exit;
  if FindFirst(BasePath + '\10.*', FindRec) then
  begin
    try
      Result := (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not HasDotNet10DesktopRuntime then
    if MsgBox('This app requires .NET 10 Desktop Runtime. Open the official download page?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', '{#DotNetDownloadURL}', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;
