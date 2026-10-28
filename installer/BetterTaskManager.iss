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
english.TaskManagerRestoreFailed=Windows Task Manager could not be restored, so it still opens Nax-TaskManager. If you uninstall now, Task Manager will not open until the setting is removed.%n%nUninstall anyway?
german.TaskManagerRestoreFailed=Der Windows-Task-Manager konnte nicht wiederhergestellt werden und öffnet weiterhin Nax-TaskManager. Wenn Sie jetzt deinstallieren, öffnet sich der Task-Manager nicht mehr, bis die Einstellung entfernt ist.%n%nTrotzdem deinstallieren?
english.CleanupFailed=Not everything could be cleaned up (blocked programs or the History service may remain).%n%n%1%n%nUninstall anyway?
german.CleanupFailed=Nicht alles konnte aufgeräumt werden (gesperrte Programme oder der Verlaufsdienst können bleiben).%n%n%1%n%nTrotzdem deinstallieren?
english.RemoveDataQuestion=Also delete the recorded network history, the logs and the saved rules?%n%nChoose No to keep them for a later installation.
german.RemoveDataQuestion=Auch den aufgezeichneten Netzwerkverlauf, die Protokolle und die gespeicherten Regeln löschen?%n%nWählen Sie Nein, um sie für eine spätere Installation zu behalten.
english.CleanupPartly=Nax-TaskManager will now be removed, but not everything could be put back.%n%n%1
german.CleanupPartly=Nax-TaskManager wird jetzt entfernt, aber nicht alles konnte zurückgesetzt werden.%n%n%1
english.MemoryRestoreFailed=The Windows memory settings this app changed (memory compression, page combining, prefetching or SysMain) could not be put back as they were.%n%n%1%n%nIf you uninstall now, they stay as they are. Uninstall anyway?
german.MemoryRestoreFailed=Die Windows-Speichereinstellungen, die diese App geändert hat (Speicherkomprimierung, Seitenzusammenführung, Prefetching oder SysMain), konnten nicht wiederhergestellt werden.%n%n%1%n%nWenn Sie jetzt deinstallieren, bleiben sie so. Trotzdem deinstallieren?
english.ServiceMoveFailed=The History service could not be moved to its new folder and may not be running. Turn it off and on again in Settings > History.%n%n%1
german.ServiceMoveFailed=Der Verlaufsdienst konnte nicht in seinen neuen Ordner verschoben werden und läuft möglicherweise nicht. Schalten Sie ihn unter Einstellungen > Verlauf aus und wieder ein.%n%n%1

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
  ServiceStopped: Boolean;
  ServiceMoveTried: Boolean;

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

// Windows Task Manager still opens this app (the redirect points into this installation).
function TaskManagerStillOurs(): Boolean;
var
  Value: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe', 'Debugger', Value) then
    Result := Pos(Lowercase(ExpandConstant('{app}\{#AppExeName}')), Lowercase(Value)) > 0;
end;

// A newer setup runs this uninstaller with /NAXUPGRADE before installing itself.
function IsUpgradeUninstall(): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), '/NAXUPGRADE') = 0 then
      Result := True;
end;

function ShouldMoveTaskManagerReplacement(): Boolean;
var
  Value: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe', 'Debugger', Value) then
    Result := Pos(Lowercase(ExpandConstant('{app}\{#LegacyExeName}')), Lowercase(Value)) > 0;
end;

// The History service is registered with a program inside this installation folder.
function ServiceRunsFromApp(): Boolean;
var
  ImagePath: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SYSTEM\CurrentControlSet\Services\NaxTaskManagerHistory', 'ImagePath', ImagePath) then
    Result := Pos(Lowercase(ExpandConstant('{app}\')), Lowercase(ImagePath)) > 0;
end;

// The elevated helpers only create a new "nax" file in the temporary folder and never write over one, so each run
// needs a name of its own: the time plus a random number.
function NewResultFile(const Prefix: String): String;
begin
  Result := ExpandConstant('{%TEMP}') + '\' + Prefix + GetDateTimeString('yyyymmddhhnnss', '-', '-') + '-' + IntToStr(Random(1000000000)) + '.txt';
end;

// The files are in place: move the stopped History service to its own folder and start it again. An install for all
// users already runs elevated.
procedure MoveHistoryService();
var
  ResultFile: String;
  ResultCode: Integer;
  Details: AnsiString;
begin
  ServiceMoveTried := True;
  ResultFile := NewResultFile('nax-setup-service-');
  if not Exec(ExpandConstant('{app}\{#AppExeName}'), '--install-history-service "' + ResultFile + '"', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Log('Moving the History service exited with code ' + IntToStr(ResultCode));
  if not LoadStringFromFile(ResultFile, Details) then
    Details := '';
  DeleteFile(ResultFile);
  if ResultCode = 0 then
    Exit;
  Log('History service details: ' + String(Details));
  if not WizardSilent() then
    MsgBox(FmtMessage(CustomMessage('ServiceMoveFailed'), [String(Details)]), mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and ServiceStopped then
    MoveHistoryService();
end;

// Setup stopped the service but never got as far as moving it (the old uninstaller failed, the copy failed or the
// user cancelled): start it again from where it was.
procedure DeinitializeSetup();
var
  ResultCode: Integer;
begin
  if ServiceStopped and (not ServiceMoveTried) then
  begin
    Exec(ExpandConstant('{sys}\net.exe'), 'start NaxTaskManagerHistory', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('Started the History service again, exit code ' + IntToStr(ResultCode));
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  RegistrationFound: Boolean;
  ResultCode: Integer;
begin
  Result := '';
  // Versions up to 2.0.0-alpha.41 ran the History service from {app}\HistoryService. Its running files cannot be
  // replaced, so it is stopped here and moved to its own folder after the files are copied.
  if IsAdminInstallMode() and (not ServiceStopped) and ServiceRunsFromApp() then
  begin
    Exec(ExpandConstant('{sys}\net.exe'), 'stop NaxTaskManagerHistory', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('Stopped the History service before copying files, exit code ' + IntToStr(ResultCode));
    ServiceStopped := True;
  end;
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
  // /NAXUPGRADE tells the old uninstaller this is an upgrade: keep the service, blocks and Task Manager choice.
  if (not ShellExec('', Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NAXUPGRADE', '',
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

// A real uninstall (not the removal a newer setup does before upgrading) undoes everything outside the app folder in
// one elevated step: Windows Task Manager is restored if it opens this app, every block is removed (otherwise blocked
// programs would stay blocked with nothing left to unblock them) and the History service goes. This runs before any
// file is removed, so a failure can still stop the uninstall.
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  ResultFile: String;
  Details: AnsiString;
  RemoveData: String;
begin
  Result := True;
  if IsUpgradeUninstall() then
    Exit;
  ResultFile := NewResultFile('nax-uninstall-');
  // Only an interactive uninstall asks. A silent one (winget, scripts) keeps the history.
  RemoveData := '';
  if (not UninstallSilent()) and (MsgBox(CustomMessage('RemoveDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    RemoveData := ' --remove-data';
  if not ShellExec('runas', ExpandConstant('{app}\{#AppExeName}'),
      '--uninstall-cleanup "' + ExpandConstant('{app}\{#AppExeName}') + '" "' + ResultFile + '"' + RemoveData,
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Log('Uninstall cleanup exited with code ' + IntToStr(ResultCode));
  if not LoadStringFromFile(ResultFile, Details) then
    Details := '';
  DeleteFile(ResultFile);
  if Details <> '' then
    Log('Uninstall cleanup details: ' + String(Details));
  if ResultCode = 0 then
    Exit;
  // Bits 16 (the data folder the user asked to delete could not be deleted) and 32 (an unreadable memory settings
  // record was dropped, so nothing is left to retry) leave no change to Windows that a later attempt could undo, so
  // they are only reported.
  if (ResultCode > 0) and ((ResultCode and 15) = 0) then
  begin
    if not UninstallSilent() then
      MsgBox(FmtMessage(CustomMessage('CleanupPartly'), [String(Details)]), mbInformation, MB_OK);
    Exit;
  end;
  // Removing the program now would leave Windows Task Manager pointing at a deleted file.
  if TaskManagerStillOurs() then
  begin
    if UninstallSilent() then
      Result := False
    else
      Result := MsgBox(CustomMessage('TaskManagerRestoreFailed'), mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    Exit;
  end;
  // A silent uninstall (winget, scripts) cannot ask, so it stops. Removing the files now would leave the service,
  // blocks, rules or changed memory settings behind with nothing left to undo them.
  if UninstallSilent() then
    Result := False
  // Only the memory settings failed (bit 8, perhaps with 16). Their record stays in the data folder, so a later
  // uninstall can still restore them.
  else if (ResultCode > 0) and ((ResultCode and 7) = 0) then
    Result := MsgBox(FmtMessage(CustomMessage('MemoryRestoreFailed'), [String(Details)]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES
  else
    Result := MsgBox(FmtMessage(CustomMessage('CleanupFailed'), [String(Details)]), mbConfirmation, MB_YESNO) = IDYES;
end;
