; Inno Setup script for Peek. Built by .github/workflows/build_binary.yml (ISCC.exe ships
; preinstalled on GitHub's windows-latest runner image) after the two exes are published to
; PEEK_DIST_DIR - this script never builds anything itself, it only packages an already-published
; output folder (either the self-contained or the framework-dependent one - see build_binary.yml).
;
; Local build: publish first, then run
;   iscc pack\peek.iss /DPeekDistDir="..\dist\win-x64" /DPeekVersion="0.1.0" /DPeekBuildMode="self-contained"

#define MyAppName "Peek"
#define MyAppPublisher "NeverMorewd"
#define MyAppURL "https://github.com/Peek-Access/Peek"
#define MyAppExeName "Peek.Desktop.exe"

#ifndef PeekVersion
  #define PeekVersion "0.1.0"
#endif
#ifndef PeekDistDir
  #define PeekDistDir "..\dist\win-x64"
#endif
#ifndef PeekBuildMode
  #define PeekBuildMode "self-contained"
#endif

[Setup]
; Fixed GUID so upgrades are recognized as upgrades rather than side-by-side installs -
; never regenerate this once released.
AppId={{5B6C6E6B-6A5B-4B7F-9C7E-2A6C6B6B7E5B}
AppName={#MyAppName}
AppVersion={#PeekVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=Peek-Setup-{#PeekVersion}-{#PeekBuildMode}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=..\assets\icon256.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; Peek is an accessibility tool - it should install without demanding admin rights so it
; isn't blocked by a UAC prompt a low-vision user may not be able to read; Inno still lets
; an admin choose an all-users install via the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Auto-closes a running Peek.Desktop.exe/Peek.Worker.exe on upgrade instead of failing
; with a file-in-use error.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start Peek automatically when Windows starts (recommended - Peek runs in the background like a screen reader)"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PeekDistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Peek.Worker.exe is spawned by Peek.Desktop.exe (see WorkerConnection) and won't
; necessarily have exited by the time the uninstaller deletes files.
Filename: "{cmd}"; Parameters: "/c taskkill /f /im Peek.Desktop.exe & taskkill /f /im Peek.Worker.exe"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillPeek"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
