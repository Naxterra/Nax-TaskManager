#ifndef AppVersion
  #error AppVersion must be defined by the build script
#endif
#ifndef NumericVersion
  #error NumericVersion must be defined by the build script
#endif
#ifndef SourceDir
  #error SourceDir must be defined by the build script
#endif
#ifndef OutputDir
  #error OutputDir must be defined by the build script
#endif
#ifndef InstallerBaseName
  #error InstallerBaseName must be defined by the build script
#endif
#ifndef IconPath
  #error IconPath must be defined by the build script
#endif
#ifndef AppIdValue
  #define AppIdValue "{{9B62E509-9DBE-4C73-88EC-DF93F70835A1}"
#endif
#ifndef AppNameValue
  #define AppNameValue "Better Task Manager"
#endif
#ifndef CloseApplicationsValue
  #define CloseApplicationsValue "yes"
#endif

#define AppName AppNameValue
#define AppPublisher "Naxterra"
#define AppExeName "BetterTaskManager.exe"
#define AppUrl "https://github.com/Naxterra/Better-Task-Manager"

[Setup]
AppId={#AppIdValue}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableProgramGroupPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename={#InstallerBaseName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
SetupLogging=yes
SetupIconFile={#IconPath}
CloseApplications={#CloseApplicationsValue}
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#SourceDir}\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.DesktopShortcut=Create a &desktop shortcut
german.DesktopShortcut=&Desktopverknüpfung erstellen
english.LaunchApplication=Launch Better Task Manager
german.LaunchApplication=Better Task Manager starten

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApplication}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
