; ============================================================
; NOVORA Installer
; Inno Setup 7
; ============================================================
; Autor: Aaron Yair Galarza Valdes
; Proyecto: NOVORA
; Versión: 1.2.0
; Fecha: 25/08/2026
; ============================================================

#define AppName "NOVORA"
#define AppVersion "1.2.0"
#define Publisher "Aaron Yair Galarza Valdes"
AppPublisherURL=https://novora-link.aroonvaldes.workers.dev/
AppSupportURL=https://github.com/aroonvaldes-star/NOVORA-LINK/issues
AppUpdatesURL=https://github.com/aroonvaldes-star/NOVORA-LINK/releases
#define ReleaseDate "25/08/2026"
#define ExeName "NOVORA.exe"

; ============================================================
; RUTA DEL BUILD
; ============================================================

#define PublishDir "..\src\NOVORA\bin\Release\net8.0-windows10.0.26100.0"


[Setup]

AppId={{A2E6F4B4-6F7D-4F68-9A74-7B0C5A1F2D44}

AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}

; Instalar en:
; C:\Program Files\NOVORA

DefaultDirName={autopf}\NOVORA
DefaultGroupName=NOVORA

OutputDir=output
OutputBaseFilename=NOVORA-Setup-{#AppVersion}

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


; ============================================================
; INFORMACIÓN DE VERSIÓN
; ============================================================

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#Publisher}
VersionInfoDescription=NOVORA Installer
VersionInfoCopyright=Copyright (C) 2026 Aaron Yair Galarza Valdes
VersionInfoProductName=NOVORA
VersionInfoProductVersion={#AppVersion}


; ============================================================
; ARCHIVOS
; ============================================================

[Files]

Source: "{#PublishDir}\*"; \
    DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs


; ============================================================
; TAREAS
; ============================================================

[Tasks]

Name: "desktopicon"; \
    Description: "Crear acceso directo en el escritorio"; \
    GroupDescription: "Accesos directos:"; \
    Flags: unchecked


; ============================================================
; ACCESOS DIRECTOS
; ============================================================

[Icons]

Name: "{group}\NOVORA"; \
    Filename: "{app}\{#ExeName}"; \
    WorkingDir: "{app}"

Name: "{autodesktop}\NOVORA"; \
    Filename: "{app}\{#ExeName}"; \
    WorkingDir: "{app}"; \
    Tasks: desktopicon


; ============================================================
; MENSAJE DE BIENVENIDA
; ============================================================

[Messages]

WelcomeLabel2=Instalación de NOVORA%n%nVersión: {#AppVersion}%nAutor: {#Publisher}%nFecha: {#ReleaseDate}


; ============================================================
; INICIAR NOVORA
; ============================================================

[Run]

Filename: "{app}\{#ExeName}"; \
    Description: "Iniciar NOVORA"; \
    WorkingDir: "{app}"; \
    Flags: nowait postinstall skipifsilent