; ArchoeraCX Windows installer (Inno Setup 6).
;
; The default mode installs per-user under %LOCALAPPDATA%\Programs and needs no
; UAC prompt. The wizard also offers an all-users install, which is the only
; place this app asks for elevation.
;
; Usage (CI):
;   ISCC.exe /DVersion=0.0.1+1 /DAppDir=<staged-dir> /DOutputDir=<dist-dir> ^
;     packaging\windows\archoera_cx.iss

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef AppDir
  #define AppDir ""
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "ArchoeraCX"
#define AppExeName "archoera_cx.exe"

[Setup]
; AppId is the upgrade/uninstall identity. Never change it after a release.
AppId={{6C7C5C3E-7B4A-4E6A-9D2B-3F1A8E5C9D41}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher=BetaStudio2
AppPublisherURL=https://github.com/BetaStudio2/ArchoeraCX
AppSupportURL=https://github.com/BetaStudio2/ArchoeraCX/issues
AppUpdatesURL=https://github.com/BetaStudio2/ArchoeraCX/releases

; Per-user by default; the wizard lets the user ask for all-users explicitly.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowCancelDuringInstall=yes
WizardStyle=modern

SetupIconFile=..\..\app\windows\runner\resources\app_icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
LicenseFile=..\..\LICENSE

OutputDir={#OutputDir}
OutputBaseFilename=ArchoeraCX-{#Version}-windows-x64-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Release directory plus the README/LICENSE/PRIVACY/docs staged by package.ps1.
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
