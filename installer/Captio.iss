; Captio per-user installer (Inno Setup 6). Build it with scripts/build-installer.ps1, which publishes
; the app and passes AppVersion, SourceDir and OutputDir.

#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
; Never change AppId: it is how Windows recognises upgrades and the uninstall entry.
AppId={{525CE012-BFFB-46D5-8218-E353FBF822C1}
AppName=Captio
AppVersion={#AppVersion}
AppVerName=Captio {#AppVersion}
AppPublisher=emph4sis-dev
AppPublisherURL=https://github.com/emph4sis-dev/captio
AppSupportURL=https://github.com/emph4sis-dev/captio/issues
AppUpdatesURL=https://github.com/emph4sis-dev/captio/releases
AppCopyright=Copyright (c) 2026 emph4sis-dev
VersionInfoVersion={#AppVersion}

; Per user, no administrator rights: installs under %LOCALAPPDATA%\Programs like modern apps.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Captio
DisableDirPage=auto
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

LicenseFile=..\LICENSE
SetupIconFile=..\src\Captio\Assets\Captio.ico
UninstallDisplayIcon={app}\Captio.exe
UninstallDisplayName=Captio
WizardStyle=modern
OutputDir={#OutputDir}
OutputBaseFilename=Captio-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

; Captio must not be running while its files are replaced or removed.
AppMutex=Local\Captio.SingleInstance
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Start each upgrade from a clean folder so no file from an older .NET runtime is left behind.
Type: filesandordirs; Name: "{app}\*"

[Files]
; Debug symbols never ship (the build script also refuses to produce them).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Captio"; Filename: "{app}\Captio.exe"
Name: "{autodesktop}\Captio"; Filename: "{app}\Captio.exe"; Tasks: desktopicon

[Registry]
; Captio writes its own "start with Windows" entry. Remove it on uninstall so nothing points at a
; deleted program. ValueType none: the installer itself never creates these values.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Captio"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: none; ValueName: "Captio"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Captio.exe"; Description: "{cm:LaunchProgram,Captio}"; Flags: nowait postinstall skipifsilent
