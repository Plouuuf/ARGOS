; Installateur Windows d'ARGOS (Inno Setup 6), compilé par la CI sur un runner Windows.
;   iscc /DAppVersion=0.4.0 /DAppBuild=78 /DSourceExe=chemin\ARGOS.exe /DOutputDir=sortie ARGOS.iss
; Installation pour l'utilisateur courant (sans droits Administrateur) dans %LOCALAPPDATA%\Programs\ARGOS :
; ARGOS peut ensuite se mettre à jour tout seul. Les données (%LOCALAPPDATA%\VDMS\ARGOS) ne sont jamais supprimées.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppBuild
  #define AppBuild "0"
#endif
#ifndef SourceExe
  #define SourceExe "ARGOS.exe"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6F1C2A57-3E4B-4D0A-9B8E-A7C1D2E3F405}
AppName=ARGOS
AppVersion={#AppVersion} (build {#AppBuild})
AppVerName=ARGOS {#AppVersion} (build {#AppBuild})
VersionInfoVersion={#AppVersion}.{#AppBuild}
AppPublisher=VAL-DE-MARNE SIMULATION
AppCopyright=© 2026 VAL-DE-MARNE SIMULATION
DefaultDirName={localappdata}\Programs\ARGOS
DefaultGroupName=ARGOS
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ARGOS-Setup
SetupIconFile=..\..\src\ARGOS.Desktop\Assets\ARGOS.ico
UninstallDisplayIcon={app}\ARGOS.exe
UninstallDisplayName=ARGOS
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci ARGOS sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "ARGOS.exe"; Flags: ignoreversion

[InstallDelete]
; Restes d'une mise à jour automatique précédente.
Type: files; Name: "{app}\ARGOS.old.exe"
Type: files; Name: "{app}\ARGOS.new.exe"

[UninstallDelete]
Type: files; Name: "{app}\ARGOS.old.exe"
Type: files; Name: "{app}\ARGOS.new.exe"

[Icons]
Name: "{autoprograms}\ARGOS"; Filename: "{app}\ARGOS.exe"; Comment: "ARGOS - poste de travail BSPP (VDMS)"
Name: "{autodesktop}\ARGOS"; Filename: "{app}\ARGOS.exe"; Comment: "ARGOS - poste de travail BSPP (VDMS)"; Tasks: desktopicon

[Run]
Filename: "{app}\ARGOS.exe"; Description: "Lancer ARGOS"; Flags: nowait postinstall skipifsilent
