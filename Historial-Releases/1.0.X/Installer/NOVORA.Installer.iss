; NOVORA Installer - Inno Setup 6
#define AppName "NOVORA"
#define AppVersion "1.3.0"
#define Publisher "NOVORA Project"
AppPublisherURL=https://novora-link.aroonvaldes.workers.dev/
AppSupportURL=https://github.com/aroonvaldes-star/NOVORA-LINK/issues
AppUpdatesURL=https://github.com/aroonvaldes-star/NOVORA-LINK/releases
#define ExeName "NOVORA.Launcher.exe"
#define PublishDir "..\src\NOVORA.Launcher\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A2E6F4B4-6F7D-4F68-9A74-7B0C5A1F2D44}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\NOVORA
DefaultGroupName=NOVORA
OutputDir=output
OutputBaseFilename=NOVORA-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NOVORA"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\NOVORA"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Run]
Filename: "{app}\{#ExeName}"; Description: "Iniciar NOVORA"; Flags: nowait postinstall skipifsilent
