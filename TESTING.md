# Better Task Manager Preview Test Checklist

Use `artifacts\BetterTaskManager-latest-portable-win-x64\BetterTaskManager.exe`. Confirm the title bar shows the expected preview before reporting a result. These checks avoid process termination, firewall changes, history deletion, and native memory cleanup.

## Startup and responsiveness

1. Close existing Better Task Manager windows, then start the stable latest executable.
2. Confirm no Command Prompt or PowerShell window opens with it.
3. Switch repeatedly through Apps, Processes, Network, History, and Memory while pressing **Refresh** where available.
4. Type and clear filters while a page is populated. The window should remain movable and responsive; Live failures should appear inline rather than as recurring dialogs.
5. Right-click a non-editing area or data table on every page. Confirm the menu mirrors that page's own actions, right-clicking a grid row selects that row, and search boxes retain their normal editing menu.

For an automated non-destructive repetition of the same refresh paths, close the interactive window and run:

```powershell
.\artifacts\BetterTaskManager-latest-portable-win-x64\BetterTaskManager.exe --ui-soak-test
```

An exit code of `0` means three complete cross-page rounds stayed within the per-refresh limit and every refresh gate returned to idle.

## Windows installer

1. Run the setup executable and confirm current-user mode is the default; optionally verify the all-users choice requests elevation.
2. Confirm the selected Programs directory, Start Menu shortcut, optional desktop shortcut, and Add/Remove Programs entry are created.
3. Confirm Setup, the installed executable, Start Menu/optional desktop shortcuts, taskbar window, and uninstall entry show the violet performance-chart icon rather than the generic WinForms icon.
4. Run the same or a newer installer again and confirm it upgrades/repairs the existing installation rather than creating a second uninstall entry.
5. Launch the installed executable and confirm the preview version, violet theme, German/English localization, Live monitoring, and page-specific actions match the portable build.
6. Uninstall and confirm program files/shortcuts are removed while `%LOCALAPPDATA%\BetterTaskManager` user settings, history, exports, and crash logs remain.

The automated equivalent is:

```powershell
.\scripts\test-installer.ps1
```

## Live monitoring

1. Open **Memory**, select `1 sec`, and enable **Live monitoring**.
2. Confirm the top status reads **Live**, snapshot time advances, and the CPU/RAM trend lines gain samples without opening dialogs.
3. Visit Apps, Processes, Network, and History while Live remains enabled and confirm the active page continues updating.
4. Disable Live and confirm the status returns to **Paused** and stops changing.

## Apps and Processes value reconciliation

1. On Apps, wait for a second snapshot so CPU values are sampled rather than `...`.
2. Select an app group and note its process count, **Sum Private Bytes**, and **Sum Working Set**.
3. Choose **View Processes**. Confirm the visible PID count matches the Apps group.
4. Sum the visible per-PID **Private Bytes MB** and **Working Set MB** values. They should match the grouped cards to displayed precision when both views use the same snapshot.
5. Remember that Working Set includes shared pages, so summed process working sets are not a system-wide unique-page total.
6. On Processes, confirm **Peak Working Set MB** is fully visible and the toolbar contains one **Refresh** button with no separate Reload or per-process Trim action. Manual Refresh should retry usernames/paths as well as values.

## Dark theme and alignment

1. Check the vertical and horizontal scrollbars on Apps, Processes, Network, and History; tracks, thumbs, and arrow areas should remain dark rather than switching to a bright system theme.
2. Confirm the dark background is violet-slate rather than pitch black and selected rows remain readable.
3. On Apps, confirm **Search apps** is vertically centered in its field.
4. Confirm **Apps**, the selected application name, metadata, cards, actions, firewall status, **Connections**, and the connection grid share consistent left edges.
5. Confirm application headings such as `svchost` have no apparent leading spaces.
6. Click several grid headers, including Network **Remote Port**. Unsorted headers should contain no arrow text; only the active column should show one large violet-white triangle at the right edge and reverse direction on the second click.

## Violet theme and German localization

1. On German Windows, confirm navigation, buttons, headers, page-specific right-click menus, tooltips, dynamic snapshot/status text, common connection states, confirmations, and errors appear in German.
2. Confirm process names, usernames, executable paths, addresses, and CSV model fields are not translated or modified.
3. Confirm the dark background is violet-slate, selected rows/tabs use violet accents, and warning/good/danger colors remain distinct.
4. Run with `--language=en` to verify English fallback or `--language=de` to force German independently of the Windows display language.

## Privilege reporting

1. In Standard mode, open Memory and confirm **Clear Standby Cache** and **Release System Cache** are disabled with an explanation that elevation and `SeProfileSingleProcessPrivilege` are required.
2. Choose **Restart as Admin** and approve the Windows elevation prompt yourself.
3. Confirm the header distinguishes **memory privilege ready** from **memory privilege unavailable**. The two system-memory buttons should be enabled only in the ready state.
4. It is not necessary to execute either cleanup action to validate capability detection.

## Firewall wording and elevation

1. In Standard mode, select a Network row with an executable path and confirm **Block App** is enabled.
2. **Not blocked by BTM** means only that no Better Task Manager outbound block rule exists; another Windows Firewall policy may still block the executable.
3. Clicking Block/Unblock requests Windows administrator approval without restarting the main window. Cancel the UAC prompt if you do not intend to modify the firewall.

## Working-set Trim reporting

1. On Memory, choose **Trim App Memory** and confirm the warning yourself.
2. Confirm the result reports separate counts for trimmed, protected/access denied, exited during scan, other failures, and skipped System/BTM processes.
3. In Standard mode, access denials are expected for higher-integrity services and the result should suggest **Restart as Admin**. Elevated mode can reduce those denials, but Windows protected services and security processes may still reject working-set access.

## History privacy control

1. Open History and clear **Record history** without using **Clear History**.
2. Refresh Apps and Network, then return to History.
3. Confirm existing rows remain viewable/exportable, the page reports **Recording off**, and no new observations are appended.
4. Restart the app and confirm the Record history preference persists.

## Reporting a failure

Include the exact preview number, page, Live interval/state, Standard/Admin status, and the shortest sequence that reproduces the problem. Screenshots are useful, but review usernames, executable paths, IP addresses, and endpoints before sharing them publicly.
