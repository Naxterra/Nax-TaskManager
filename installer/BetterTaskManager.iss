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
  #define AppNameValue "Nax-TaskManager"
#endif
#ifndef CloseApplicationsValue
  #define CloseApplicationsValue "yes"
#endif
#ifndef UninstallRegistryId
  #define UninstallRegistryId "{9B62E509-9DBE-4C73-88EC-DF93F70835A1}"
#endif

#define AppName AppNameValue
#define AppPublisher "Naxterra"
#define AppExeName "Nax-TaskManager.exe"
; Up to 2.0.0-alpha.14 the program was NaxTaskManager.exe.
#define LegacyExeName "NaxTaskManager.exe"
#define AppUrl "https://github.com/Naxterra/Nax-TaskManager"

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
; Installs before the rename went to "Better Task Manager"; move to the new folder instead of reusing it.
UsePreviousAppDir=no
UsePreviousGroup=no
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
CloseApplicationsFilter={#AppExeName},{#LegacyExeName}
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#SourceDir}\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.DesktopShortcut=Create a &desktop shortcut
german.DesktopShortcut=&Desktopverknüpfung erstellen
english.LaunchApplication=Launch Nax-TaskManager
german.LaunchApplication=Nax-TaskManager starten
english.RemovingPreviousVersion=Removing the existing installation...
german.RemovingPreviousVersion=Die vorhandene Installation wird entfernt...
english.PreviousUninstallFailed=The existing installation could not be removed. Setup will not continue. Uninstaller exit code:
german.PreviousUninstallFailed=Die vorhandene Installation konnte nicht entfernt werden. Setup wird nicht fortgesetzt. Deinstallations-Fehlercode:
english.PreviousUninstallerMissing=An existing installation was detected, but its uninstaller is missing. Remove or repair the existing installation in Windows Settings before trying again.
german.PreviousUninstallerMissing=Eine vorhandene Installation wurde erkannt, aber das Deinstallationsprogramm fehlt. Entfernen oder reparieren Sie die vorhandene Installation in den Windows-Einstellungen und versuchen Sie es erneut.

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

; "Replace Windows Task Manager" still pointing at the pre-rename exe: point it at the new one (UAC prompt).
Filename: "{app}\{#AppExeName}"; Parameters: "--replace-taskmanager-on ""{app}\{#AppExeName}"""; WorkingDir: "{app}"; Flags: shellexec waituntilterminated; Verb: "runas"; Check: ShouldMoveTaskManagerReplacement

[Code]
const
  UninstallRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#UninstallRegistryId}_is1';

var
  PreviousVersionRemoved: Boolean;

function ExecutableFromCommand(const CommandLine: String): String;
var
  ClosingQuote: Integer;
  Separator: Integer;
  Value: String;
begin
  Value := Trim(CommandLine);
  Result := '';
  if Value = '' then
    Exit;

  if Value[1] = '"' then
  begin
    ClosingQuote := Pos('"', Copy(Value, 2, Length(Value) - 1));
    if ClosingQuote > 0 then
      Result := Copy(Value, 2, ClosingQuote - 1);
  end
  else
  begin
    Separator := Pos(' ', Value);
    if Separator > 0 then
      Result := Copy(Value, 1, Separator - 1)
    else
      Result := Value;
  end;
end;

function QueryExistingUninstaller(var Uninstaller: String; var RegistrationFound: Boolean): Boolean;
var
  CommandLine: String;
begin
  Result := False;
  RegistrationFound := False;
  Uninstaller := '';

  if RegQueryStringValue(HKCU, UninstallRegistryKey, 'UninstallString', CommandLine) then
  begin
    RegistrationFound := True;
    Uninstaller := ExecutableFromCommand(CommandLine);
  end
  else if RegQueryStringValue(HKLM64, UninstallRegistryKey, 'UninstallString', CommandLine) then
  begin
    RegistrationFound := True;
    Uninstaller := ExecutableFromCommand(CommandLine);
  end
  else if RegQueryStringValue(HKLM32, UninstallRegistryKey, 'UninstallString', CommandLine) then
  begin
    RegistrationFound := True;
    Uninstaller := ExecutableFromCommand(CommandLine);
  end;

  Result := RegistrationFound and (Uninstaller <> '') and FileExists(Uninstaller);
end;

function ExistingRegistrationRemains(): Boolean;
begin
  Result := RegKeyExists(HKCU, UninstallRegistryKey) or
    RegKeyExists(HKLM64, UninstallRegistryKey) or
    RegKeyExists(HKLM32, UninstallRegistryKey);
end;

function WaitForUninstallerRemoval(const Uninstaller: String): Boolean;
var
  Attempt: Integer;
begin
  for Attempt := 1 to 100 do
  begin
    if not FileExists(Uninstaller) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(100);
  end;
  Result := not FileExists(Uninstaller);
end;

function ShouldRemoveHistoryService(): Boolean;
begin
  Result := (not UninstallSilent()) and RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\NaxTaskManagerHistory');
end;

function ShouldRestoreTaskManager(): Boolean;
var
  Value: String;
begin
  Result := False;
  if UninstallSilent() then
    Exit;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe', 'Debugger', Value) then
    Result := Pos(Lowercase(ExpandConstant('{app}\{#AppExeName}')), Lowercase(Value)) > 0;
end;

function ShouldMoveTaskManagerReplacement(): Boolean;
var
  Value: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe', 'Debugger', Value) then
    Result := Pos(Lowercase(ExpandConstant('{app}\{#LegacyExeName}')), Lowercase(Value)) > 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  RegistrationFound: Boolean;
  ResultCode: Integer;
begin
  Result := '';
  if PreviousVersionRemoved then
    Exit;

  if not QueryExistingUninstaller(Uninstaller, RegistrationFound) then
  begin
    if RegistrationFound then
      Result := CustomMessage('PreviousUninstallerMissing');
    Exit;
  end;

  Log('Existing installation detected. Running previous uninstaller: ' + Uninstaller);
  WizardForm.StatusLabel.Caption := CustomMessage('RemovingPreviousVersion');
  if (not ShellExec('', Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS', '',
      SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
  begin
    Result := CustomMessage('PreviousUninstallFailed') + ' ' + IntToStr(ResultCode);
    Exit;
  end;

  if ExistingRegistrationRemains() or (not WaitForUninstallerRemoval(Uninstaller)) then
  begin
    Result := CustomMessage('PreviousUninstallFailed') + ' ' + IntToStr(ResultCode);
    Exit;
  end;

  PreviousVersionRemoved := True;
  Log('Previous installation removed successfully before installing the new version.');
end;

procedure RunElevatedApp(const Parameters: String);
var
  ResultCode: Integer;
begin
  if not ShellExec('runas', ExpandConstant('{app}\{#AppExeName}'), Parameters, ExpandConstant('{app}'),
      SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    Log('Could not run ' + Parameters + ': ' + SysErrorMessage(ResultCode))
  else
    Log(Parameters + ' exited with code ' + IntToStr(ResultCode));
end;

// [UninstallRun] Check functions are evaluated at install time, where UninstallSilent() is not allowed,
// so these steps run here, before the app files are removed.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  // The background history service runs from Program Files as LocalSystem; remove it with the app (one UAC prompt).
  // Skipped for silent uninstalls, which is how a newer setup removes this version during an upgrade.
  if ShouldRemoveHistoryService() then
    RunElevatedApp('--uninstall-history-service "' + ExpandConstant('{%TEMP}') + '\NaxTaskManager-uninstall-service.txt"');
  // If this app replaced Windows Task Manager, restore the built-in one (one UAC prompt). Only when the redirect is
  // still ours, and skipped for silent uninstalls so the choice survives app upgrades.
  if ShouldRestoreTaskManager() then
    RunElevatedApp('--replace-taskmanager-off "' + ExpandConstant('{app}\{#AppExeName}') + '"');
end;
