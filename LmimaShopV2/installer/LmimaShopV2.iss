; ============================================================================
; LmimaShop V2 - Inno Setup installer script
;
; PREREQUISITE: build the self-contained publish folder first:
;     powershell -ExecutionPolicy Bypass -File ..\scripts\publish.ps1
; It produces:
;     ..\desktop\bin\Release\net10.0-windows\publish\
; (the script copies mobile\ into that folder so the embedded server can serve
;  the phone PWA).
;
; Compile with Inno Setup 6:
;     iscc installer\LmimaShopV2.iss
;
; This installer contains NO database, NO backups and NO customer data.
; MySQL Server 8.x is a documented external prerequisite.
; ============================================================================

#define MyAppName "LmimaShop V2"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "LmimaShop"
#define MyAppExeName "LmimaShopV2.Desktop.exe"
#define PublishDir "..\desktop\bin\Release\net10.0-windows\publish"

[Setup]
; A fixed AppId keeps upgrades and uninstall entries consistent.
AppId={{8F4C2D7A-5B1E-4C9A-9A31-2F6D8E7B4C10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LmimaShopV2
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=LmimaShopV2-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
PrivilegesRequired=admin
CloseApplications=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start LmimaShop V2 when Windows starts"; GroupDescription: "Startup:"

[Files]
; Only the published application (including mobile\ served on port 8090).
; Never add a database dump, backups, logs or user configuration here.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Optional "run with Windows" entry, stored per user and removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LmimaShopV2"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsMySqlInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKLM64, 'SOFTWARE\MySQL AB') or
    RegKeyExists(HKLM32, 'SOFTWARE\MySQL AB') or
    RegKeyExists(HKLM64, 'SOFTWARE\WOW6432Node\MySQL AB');
end;

function InitializeSetup(): Boolean;
var
  MessageText: String;
begin
  Result := True;

  if not IsMySqlInstalled() then
  begin
    MessageText :=
      'MySQL Server 8.x was not detected on this computer.' + #13#10 + #13#10 +
      'LmimaShop V2 stores all data in the MySQL database "LmimaShopV2DB".' + #13#10 +
      'No database and no customer data are bundled with this installer.' + #13#10 + #13#10 +
      'Install MySQL Server 8.x before the first run. The application can create' + #13#10 +
      'the database automatically if the MySQL user has CREATE privileges, or you' + #13#10 +
      'can import db\schema.sql manually.' + #13#10 + #13#10 +
      'Continue the installation anyway?';

    if MsgBox(MessageText, mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
