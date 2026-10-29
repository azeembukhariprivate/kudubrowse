; KUDUBROWSE installer (Inno Setup 6)
; Installs for the current Windows user only, so installing never asks for administrator rights.

#ifndef MyAppVersion
  #define MyAppVersion "0.4.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\KUDUBROWSE"
#endif

#define MyAppName "KUDUBROWSE"
#define MyAppExe "KUDUBROWSE.exe"
#define MyAppUrl "https://github.com/azeembukhariprivate/kudubrowse"

[Setup]
AppId={{893DD256-DB89-414A-96D4-2D2DB03B0F9F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Azeem Bukhari
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=KUDUBROWSE-{#MyAppVersion}-setup
SetupIconFile=..\src\KUDUBROWSE\Assets\KUDUBROWSE.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Azeem Bukhari
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "self-test.log,screenshots\*"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Parameters: "--register"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExe}"; Description: "Open KUDUBROWSE now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "KUDUBROWSEUnregister"

[UninstallDelete]
; Saved tabs and the browser profile (cookies, site data, cache).
Type: filesandordirs; Name: "{localappdata}\KUDUBROWSE"

[Registry]
; Remove saved settings when uninstalling.
Root: HKCU; Subkey: "Software\KUDUBROWSE"; Flags: uninsdeletekey dontcreatekey
