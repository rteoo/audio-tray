; Inno Setup script for AudioTray: per-user install, no admin required.
;
; Build with packaging\build.ps1, which publishes the self-contained app and passes
; MyAppVersion, MyArch and MyDistDir, e.g.:
;   ISCC /DMyAppVersion=1.0.0 /DMyArch=x64 /DMyDistDir=..\artifacts\publish\win-x64 AudioPriorityTray.iss
;
; Settings and logs live in %LOCALAPPDATA%\AudioPriorityTray and are intentionally NOT removed
; on uninstall; the installer only manages the program files under {app}.

#ifndef MyAppVersion
  #error Pass /DMyAppVersion=x.y.z (packaging\build.ps1 does this)
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#ifndef MyDistDir
  #define MyDistDir "..\artifacts\publish\win-" + MyArch
#endif

#define MyAppName "AudioTray"
#define MyAppPublisher "Project Contributors"
#define MyAppExeName "AudioPriorityTray.exe"
; Same value name the app's own "Start with Windows" toggle uses, so both stay in sync.
#define MyRunValueName "AudioPriorityTray"

[Setup]
; Stable AppId so future versions upgrade in place instead of installing twice.
AppId={{0E5B2C71-9A4D-4F38-B6E2-7C1D8A3F5E90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/rteoo/audio-tray
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Upgrades from "Audio Priority" (v1.0.0) move to the new Start menu group instead of keeping the old one.
UsePreviousGroup=no
; Per-user install: no administrator/UAC prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=AudioTraySetup-{#MyAppVersion}-{#MyArch}
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\AudioPriorityTray\Assets\AudioPriorityTray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
; The app's single-instance mutex: upgrades and uninstall ask to close a running copy.
AppMutex=Local\AudioPriorityTray
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.StartupTask=Start %1 automatically with Windows
brazilianportuguese.StartupTask=Iniciar o %1 automaticamente com o Windows
english.StartupGroup=Startup:
brazilianportuguese.StartupGroup=Inicialização:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask,{#MyAppName}}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
Source: "{#MyDistDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; Shortcuts left by releases named "Audio Priority" (v1.0.0), before the AudioTray rename.
Type: filesandordirs; Name: "{autoprograms}\Audio Priority"
Type: files; Name: "{autodesktop}\Audio Priority.lnk"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyRunValueName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup
; Always remove the Run value on uninstall, including one the app itself wrote.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#MyRunValueName}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
