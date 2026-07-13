#define MyAppName "Codex Limit Widget"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyAppProductVersion
  #define MyAppProductVersion "1.0.0.0"
#endif
#ifndef MyAppSourceDir
  #define MyAppSourceDir "..\publish\win-x64\self-contained"
#endif
#ifndef MyAppPackageSuffix
  #define MyAppPackageSuffix "Windows-x64-Full-Setup"
#endif
#ifndef MyAppPackageLabel
  #define MyAppPackageLabel "Full"
#endif
#ifndef MyAppRequiresRuntime
  #define MyAppRequiresRuntime 0
#endif
#ifndef MyAppRequiredRuntimeMajor
  #define MyAppRequiredRuntimeMajor 10
#endif
#define MyAppPublisher "CodexLimitWidget Contributors"
#define MyAppExeName "CodexLimitWidget.App.exe"
#define MyAppIconPath "..\icon.ico"
#define MyAppLicensePath "..\LICENSE"

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinese_simplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "chinese_traditional"; MessagesFile: "Languages\ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "Languages\Japanese.isl"

[Setup]
AppId={{D64D2C37-0B3E-4A5C-9F0C-0E8A0C5D1987}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppProductVersion}
AppVerName={#MyAppName} {#MyAppVersion} ({#MyAppPackageLabel})
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=CodexLimitWidget-{#MyAppVersion}-{#MyAppPackageSuffix}
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
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppLicensePath}"; DestDir: "{app}"; DestName: "LICENSE"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

#if MyAppRequiresRuntime
[Code]
function HasRequiredDotNetRuntime(): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
  RequiredPrefix: String;
begin
  Result := False;
  RequiredPrefix := IntToStr({#MyAppRequiredRuntimeMajor}) + '.';
  if RegGetValueNames(
    HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App',
    Versions
  ) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos(RequiredPrefix, Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasRequiredDotNetRuntime() then
  begin
    Result := MsgBox(
      '.NET {#MyAppRequiredRuntimeMajor} x64 Runtime was not detected.' + #13#10 + #13#10 +
      'Install it from https://dotnet.microsoft.com/download/dotnet/{#MyAppRequiredRuntimeMajor}.0 before running Codex Limit Widget.' + #13#10 + #13#10 +
      'Continue installing the Slim package anyway?',
      mbConfirmation,
      MB_YESNO
    ) = IDYES;
  end;
end;
#endif
