; =====================================================================
; DictaMeeting — Inno Setup Script
; Instalador para Windows de DictaMeeting (Transcripción y Actas Locales)
; =====================================================================

#define MyAppName "DictaMeeting"
#define MyAppVersion "1.5.2"
#define MyAppPublisher "Aritz Villodas"
#define MyAppURL "https://github.com/oakmusic/dictameeting"
#define MyAppExeName "DictaMeeting.exe"
#define SourcePublishDir "..\dist\publish-win-x64"

[Setup]
AppId={{D1C7A001-MEET-46B9-B31F-D1C7AMEET1NG}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist\installer
OutputBaseFilename=DictaMeeting-v{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\DictaMeeting.App\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=auto
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Archivos publicados de la aplicación (DictaMeeting.exe, Meetings/, data/)
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Directorio de reuniones en la aplicación y directorios locales para modelos y DPAPI
Name: "{app}\Meetings"; Flags: uninsneveruninstall
Name: "{localappdata}\DictaMeeting"; Flags: uninsneveruninstall
Name: "{localappdata}\DictaMeeting\models"; Flags: uninsneveruninstall
Name: "{localappdata}\DictaMeeting\secure"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "Transcripción y Actas de Reuniones Locales"
Name: "{group}\Carpeta de Reuniones"; Filename: "{app}\Meetings"; Comment: "Acceso a las grabaciones y transcripciones de reuniones"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"; Comment: "Transcripción y Actas de Reuniones Locales"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
