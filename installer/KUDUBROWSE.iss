; KUDUBROWSE installer (Inno Setup 6)
; Installs for the current Windows user only, so installing never asks for administrator rights.

#ifndef MyAppVersion
  #define MyAppVersion "0.6.0"
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

[Code]
const
  WebView2Client = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function FoundIn(Root: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root, WebView2Client, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

{ Pages are shown by Microsoft Edge WebView2, which comes with Windows 11 and up-to-date Windows 10. }
function HasWebView2: Boolean;
begin
  Result := FoundIn(HKLM32) or FoundIn(HKCU);
  if not Result and IsWin64 then Result := FoundIn(HKLM64);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not HasWebView2 then
    if SuppressibleMsgBox('KUDUBROWSE shows web pages with Microsoft Edge WebView2, which isn''t on this PC yet.' + #13#10#13#10 +
      'Open Microsoft''s page to get it? Setup stops now; run it again after installing WebView2.' + #13#10 +
      '(Choose No to install KUDUBROWSE anyway.)', mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
    end;
end;

{ Bookmarks, history, passwords, open tabs and settings are only removed when the person says so. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if SuppressibleMsgBox('Also remove your bookmarks, history, saved passwords, open tabs and settings from this PC?' + #13#10#13#10 +
      'Choose No to keep them for if you install KUDUBROWSE again.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\KUDUBROWSE'), True, True, True);
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\KUDUBROWSE');
    end;
end;
