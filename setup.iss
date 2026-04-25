[Setup]
AppName=Llamashot
AppVersion=2.5.0
AppVerName=Llamashot 2.5.0
AppPublisher=Santosh Kumar
AppPublisherURL=https://github.com/user/llamashot
DefaultDirName={autopf}\Llamashot
DefaultGroupName=Llamashot
OutputDir=dist
OutputBaseFilename=LlamashotSetup
SetupIconFile=assets\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
UninstallDisplayIcon={app}\Llamashot.exe
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes

[Files]
Source: "Llamashot\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Llamashot.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Llamashot"; Filename: "{app}\Llamashot.exe"
Name: "{autodesktop}\Llamashot"; Filename: "{app}\Llamashot.exe"; Tasks: desktopicon
Name: "{userstartup}\Llamashot"; Filename: "{app}\Llamashot.exe"; Tasks: startupicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Start with &Windows"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\Llamashot.exe"; Description: "Launch Llamashot"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\*"
Type: dirifempty; Name: "{app}"
