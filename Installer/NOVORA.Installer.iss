; ============================================================
; NOVORA Installer - Inno Setup 6/7
; Base: NOVORA-LINK 1.4
; ============================================================

#define AppName "NOVORA"
#ifndef AppVersion
  #define AppVersion "1.4"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "1.4.0.0"
#endif
#define Publisher "Aaron Yair Galarza Valdes"
#define ExeName "NOVORA-LINK.exe"

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef ComplianceDir
  #define ComplianceDir "..\artifacts\compliance"
#endif
#ifndef LegalDir
  #define LegalDir "..\Legal"
#endif
#ifndef SetupBaseFilename
  #define SetupBaseFilename "NOVORA-Setup-" + AppVersion
#endif

[Setup]
AppId={{A2E6F4B4-6F7D-4F68-9A74-7B0C5A1F2D44}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\NOVORA
DefaultGroupName=NOVORA
OutputDir=output
OutputBaseFilename={#SetupBaseFilename}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin
LicenseFile={#LegalDir}\NLLegalEULA.txt
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=NOVORA
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany={#Publisher}
VersionInfoDescription=NOVORA Installer
VersionInfoCopyright=Copyright (C) 2026 Aaron Yair Galarza Valdes
VersionInfoProductName=NOVORA
VersionInfoProductVersion={#VersionInfoVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
Source: "{#LegalDir}\*"; DestDir: "{app}\Legal"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "README.md"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\TRADEMARKS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ComplianceDir}\ffmpeg-8.1.2.tar.xz"; DestDir: "{app}\Tools\Legal\Sources"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Icons]
Name: "{group}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Iniciar NOVORA"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
