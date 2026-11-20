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
AppPublisherURL=https://novora-link.aroonvaldes.workers.dev/
AppSupportURL=https://github.com/aroonvaldes-star/NOVORA-LINK/issues
AppUpdatesURL=https://github.com/aroonvaldes-star/NOVORA-LINK/releases
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

[InstallDelete]
; Remove legacy launchers after upgrading installations created by the old snapshots.
Type: files; Name: "{app}\NOVORA.exe"
Type: files; Name: "{app}\NOVORA.Launcher.exe"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
Name: "firewallrules"; Description: "Habilitar reglas en Firewall de Windows para enlace LAN/Wi-Fi con Android"; GroupDescription: "Configuración de red:"; Flags: checkedonce
Name: "installapk"; Description: "Instalar LINK Control en el teléfono Android conectado (vía USB/ADB)"; GroupDescription: "Dispositivo móvil:"; Flags: checkedonce

[Icons]
Name: "{group}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NOVORA"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Check: ShouldCreateDesktopIcon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""NOVORA-LINK LAN privada TCP"" dir=in action=allow protocol=TCP program=""{app}\{#ExeName}"" profile=private remoteip=localsubnet"; Tasks: firewallrules; Flags: runhidden; StatusMsg: "Configurando cortafuegos para enlace con Android..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""NOVORA-LINK LAN privada UDP"" dir=in action=allow protocol=UDP program=""{app}\{#ExeName}"" profile=private remoteip=localsubnet"; Tasks: firewallrules; Flags: runhidden
Filename: "{app}\Tools\adb.exe"; Parameters: "install -r -d ""{app}\Android\LinkControl.apk"""; Tasks: installapk; Flags: runhidden; StatusMsg: "Instalando LINK Control en el teléfono conectado vía ADB..."
Filename: "{app}\{#ExeName}"; Description: "Iniciar NOVORA"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NOVORA-LINK LAN privada TCP"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NOVORA-LINK LAN privada UDP"""; Flags: runhidden

[Code]
function ShouldCreateDesktopIcon: Boolean;
begin
  Result := WizardIsTaskSelected('desktopicon') or
    FileExists(ExpandConstant('{autodesktop}\NOVORA.lnk'));
end;
