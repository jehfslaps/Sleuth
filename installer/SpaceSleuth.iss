; Inno Setup script for Space Sleuth.
; Builds SpaceSleuth-Setup.exe: Start menu shortcut, optional desktop icon,
; and an uninstaller listed in Windows Settings > Apps.
; GitHub Actions compiles it with:  ISCC.exe /DAppVersion=1.0.N installer\SpaceSleuth.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Space Sleuth"
#define AppExe "SpaceSleuth.exe"

[Setup]
AppId={{6F1C2B7E-3A4D-4E8B-9C21-5D7A0B3E9F14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=jehfslaps
AppPublisherURL=https://github.com/jehfslaps/space-sleuth
AppSupportURL=https://github.com/jehfslaps/space-sleuth
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The app itself needs administrator rights to read Windows' change log, so install for all users.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\out
OutputBaseFilename=SpaceSleuth-Setup
SetupIconFile=..\src\SpaceSleuth\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large-100.bmp,wizard-large-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-200.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
UsedUserAreasWarning=no

[Messages]
WelcomeLabel1=Hi! Let's set up [name].
WelcomeLabel2=Space Sleuth shows you where your disk space went, ranked from most likely to least likely.%n%nIt only looks; it never deletes anything.%n%nThis takes a few seconds.
FinishedHeadingLabel=All set.
FinishedLabel=[name] is installed. You'll find it in your Start menu.
FinishedLabelNoIcons=[name] is installed.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\Unbounded-OFL.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "See where your disk space went"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Comment: "See where your disk space went"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Scan history (sizes remembered between scans)
Type: filesandordirs; Name: "{localappdata}\SpaceSleuth"
