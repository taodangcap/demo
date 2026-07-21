#define MyAppName "ShowCuePlayer"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ShowCuePlayer"
#define MyAppExeName "ShowCuePlayer.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{8C2A012D-4BA6-4F62-AF96-22AC9E54AB3A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=ShowCuePlayer-Setup-{#MyAppVersion}-x64
SetupIconFile=..\Assets\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM64,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7E1B7-75D0-4CBF-BF25-86D815E0C39B}',
      'pv', Version) or
    RegQueryStringValue(HKCU,
      'Software\Microsoft\EdgeUpdate\Clients\{F1E7E1B7-75D0-4CBF-BF25-86D815E0C39B}',
      'pv', Version);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not WebView2Installed then
    MsgBox(
      'Microsoft Edge WebView2 Runtime was not detected.' + #13#10 + #13#10 +
      'Audio, image and video playback can still be installed, but Karaoke features require WebView2. ' +
      'Install the Evergreen WebView2 Runtime from Microsoft if Karaoke does not open.',
      mbInformation, MB_OK);
end;
