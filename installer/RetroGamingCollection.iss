#ifndef AppVersion
  #define AppVersion "1.00"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{71D14C3E-26E2-4A42-AE08-B52F6C6FA48D}
AppName=RetroGaming Collection
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=Alt-F4-Delta
AppPublisherURL=https://github.com/Alt-F4-Delta/RetroGamingCollection
AppUpdatesURL=https://github.com/Alt-F4-Delta/RetroGamingCollection/releases
DefaultDirName={localappdata}\Programs\RetroGamingCollection
DefaultGroupName=RetroGaming Collection
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=RetroGamingCollection-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\RetroGaming.exe
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
SetupLogging=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed.flag"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\RetroGaming Collection"; Filename: "{app}\RetroGaming.exe"
Name: "{autodesktop}\RetroGaming Collection"; Filename: "{app}\RetroGaming.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RetroGaming.exe"; Description: "Open RetroGaming Collection"; Flags: nowait postinstall skipifsilent

; User data, emulator directories, ROMs and saves are intentionally not packaged or deleted.
