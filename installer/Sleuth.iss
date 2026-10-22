; Inno Setup script for Sleuth.
; Builds Sleuth-Setup.exe: Start menu shortcut, optional desktop icon,
; and an uninstaller listed in Windows Settings > Apps.
; GitHub Actions compiles it with:  ISCC.exe /DAppVersion=1.0.N installer\Sleuth.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Sleuth"
#define AppExe "Sleuth.exe"

[Setup]
AppId={{6F1C2B7E-3A4D-4E8B-9C21-5D7A0B3E9F14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=jehfslaps
AppPublisherURL=https://github.com/jehfslaps/Sleuth
AppSupportURL=https://github.com/jehfslaps/Sleuth
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Earlier versions were called "Space Sleuth": install into the new folder and tidy the old one away.
UsePreviousAppDir=no
; The app itself needs administrator rights to read Windows' change log, so install for all users.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\out
OutputBaseFilename=Sleuth-Setup
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
WelcomeLabel1=sleuth.
WelcomeLabel2=disk space finder.%n%nshows where your disk space went, ranked from most likely to least likely. it only looks; it never deletes anything.
FinishedHeadingLabel=done.
FinishedLabel=sleuth is installed. it's in your start menu.
FinishedLabelNoIcons=sleuth is installed.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "See where your disk space went"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Comment: "See where your disk space went"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[InstallDelete]
; Leftovers from when the app was called "Space Sleuth"
Type: filesandordirs; Name: "{autopf}\Space Sleuth"
Type: files; Name: "{autoprograms}\Space Sleuth.lnk"
Type: files; Name: "{autodesktop}\Space Sleuth.lnk"

[UninstallDelete]
; Scan history (sizes remembered between scans)
Type: filesandordirs; Name: "{localappdata}\SpaceSleuth"
