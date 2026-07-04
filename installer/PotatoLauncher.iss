#define AppName "Potato Launcher"
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\publish"
#endif
#ifndef OutputDir
#define OutputDir "..\release"
#endif

[Setup]
AppId={{D78EA07B-BD9E-4B24-8D63-71C16020A7A6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Naru6780
AppPublisherURL=https://github.com/Naru6780/potato-launcher
AppSupportURL=https://github.com/Naru6780/potato-launcher/issues
AppUpdatesURL=https://github.com/Naru6780/potato-launcher/releases
DefaultDirName={localappdata}\Programs\Potato Launcher
DefaultGroupName={#AppName}
DisableDirPage=no
DisableProgramGroupPage=auto
OutputDir={#OutputDir}
OutputBaseFilename=PotatoLauncherSetup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\Potato Launcher.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=Potato Launcher.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Potato Launcher"; Filename: "{app}\Potato Launcher.exe"
Name: "{autodesktop}\Potato Launcher"; Filename: "{app}\Potato Launcher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Potato Launcher.exe"; Description: "{cm:LaunchProgram,Potato Launcher}"; Flags: nowait postinstall skipifsilent
