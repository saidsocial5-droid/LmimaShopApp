[Setup]
AppName=LmimaShop
AppVersion=1.0.0
AppPublisher=LmimaShop Inc.
DefaultDirName={autopf}\LmimaShop
DefaultGroupName=LmimaShop
OutputBaseFilename=LmimaShop_Installer
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "الاختصارات:"

[Files]
Source: "out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "schema.sql"; DestDir: "{app}"; Flags: ignoreversion
Source: "LmimaShop.apk"; DestDir: "{app}"; Flags: ignoreversion
Source: "LmimaShopMobile\*"; DestDir: "{app}\LmimaShopMobile"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LmimaShop"; Filename: "{app}\LmimaShopApp.exe"
Name: "{autodesktop}\LmimaShop"; Filename: "{app}\LmimaShopApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LmimaShopApp.exe"; Description: "تشغيل برنامج كاشير LmimaShop"; Flags: nowait postinstall skipifsilent
