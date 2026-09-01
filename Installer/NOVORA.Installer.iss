; ============================================================
; NOVORA Installer - Inno Setup 6/7
; Base: NOVORA-LINK 1.3
; ============================================================

#define AppName "NOVORA"
#ifndef AppVersion
  #define AppVersion "1.3"
#endif
#define Publisher "Aaron Yair Galarza Valdes"
#define ExeName "NOVORA.exe"
#define IconFile "..\src\NOVORA\NOVORA_1.1.ico"

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{A2E6F4B4-6F7D-4F68-9A74-7B0C5A1F2D44}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\NOVORA
DefaultGroupName=NOVORA
OutputDir=output
OutputBaseFilename=NOVORA-Setup-{#AppVersion}
SetupIconFile={#IconFile}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=NOVORA
UninstallDisplayIcon={app}\{#ExeName}
VersionInfoVersion={#AppVersion}.0.0
VersionInfoCompany={#Publisher}
VersionInfoDescription=NOVORA Installer
VersionInfoCopyright=Copyright (C) 2026 Aaron Yair Galarza Valdes
VersionInfoProductName=NOVORA
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Icons]
Name: "{group}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Iniciar NOVORA"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
