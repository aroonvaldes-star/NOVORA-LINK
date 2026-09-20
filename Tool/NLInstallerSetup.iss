; NOVORA-LINK 1.4 PRERELEASE PRE FINAL
; Build with Inno Setup 7.
#ifndef PayloadDir
  #error PayloadDir must point to the verified release payload
#endif
#ifndef ArtifactDir
  #error ArtifactDir must point to the output directory
#endif
#ifndef BrandingIcon
  #error BrandingIcon must point to NLAssetNOVORA11.ico
#endif

[Setup]
AppId={{D3C7B995-96D4-4A47-AEC6-8A9BD67629CB}
AppName=NOVORA-LINK
AppVersion=1.4.1-prerelease-prefinal
AppVerName=NOVORA-LINK 1.4 PRERELEASE PRE FINAL
AppPublisher=aroonvaldes-star
AppPublisherURL=https://github.com/aroonvaldes-star/NOVORA-LINK
AppSupportURL=https://github.com/aroonvaldes-star/NOVORA-LINK/issues
AppUpdatesURL=https://github.com/aroonvaldes-star/NOVORA-LINK/releases
DefaultDirName={localappdata}\Programs\NOVORA-LINK
DefaultGroupName=NOVORA-LINK
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.26100
WizardStyle=modern
SetupIconFile={#BrandingIcon}
UninstallDisplayIcon={app}\NOVORA.exe
UninstallDisplayName=NOVORA-LINK 1.4 PRERELEASE PRE FINAL
OutputDir={#ArtifactDir}
OutputBaseFilename=NOVORA-LINK-1.4-PRERELEASE-PRE-FINAL-Setup-x64
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.4.1.0
VersionInfoDescription=NOVORA-LINK 1.4 PRERELEASE PRE FINAL
InfoBeforeFile={#PayloadDir}\Manual\NLManualUsuarioES.txt

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.log"
Source: "{#PayloadDir}\Manual\NLManualUsuarioES.txt"; DestDir: "{userdesktop}"; DestName: "NOVORA-LINK - Manual de Usuario ES.txt"; Flags: ignoreversion
Source: "{#PayloadDir}\Manual\NLUserManualEN.txt"; DestDir: "{userdesktop}"; DestName: "NOVORA-LINK - User Manual EN.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\NOVORA-LINK"; Filename: "{app}\NOVORA.exe"; WorkingDir: "{app}"
Name: "{group}\Manual de Usuario ES"; Filename: "{app}\Manual\NLManualUsuarioES.txt"
Name: "{group}\User Manual EN"; Filename: "{app}\Manual\NLUserManualEN.txt"
Name: "{group}\Desinstalar NOVORA-LINK"; Filename: "{uninstallexe}"
Name: "{userdesktop}\NOVORA-LINK"; Filename: "{app}\NOVORA.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\NOVORA.exe"; WorkingDir: "{app}"; Description: "Abrir NOVORA-LINK"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentCommand, InstalledCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\NOVORA.exe') + '" --autostart';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'NOVORA-LINK', CurrentCommand) then
    begin
      if CompareText(CurrentCommand, InstalledCommand) = 0 then
      begin
        if not RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NOVORA-LINK') then
          Log('No se pudo retirar el inicio automatico de esta instalacion.');
      end;
    end;
  end;
end;
