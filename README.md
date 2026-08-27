# Better Task Manager

Better Task Manager is a Windows desktop tool for admins who want a more practical view of processes, memory, firewall state, and live network connections.

The app is written in C#/.NET WinForms. It starts normally and can restart itself with administrator rights when firewall or system-memory actions require elevation.

The current development build is `1.1.0-preview.53`. The checked-in v1.0 download remains the last stable release.

## Current Features

- App-based overview with explicit process count plus same-snapshot summed CPU, private-byte, and working-set values for each executable group. CPU displays `...` until a second snapshot exists, so an unavailable baseline is never mislabeled as measured idle. Cached all-field search, typed sorting, and selection persist across Live refreshes.
- Unsorted data-grid headers remain clean; the actively sorted column receives one larger violet-white triangle at its right edge, including Local Port and Remote Port.
- Apps renders process/network grouping before the slower firewall-rule scan completes; late firewall results are guarded by snapshot and mutation revision so they cannot overwrite newer data.
- Apps marks partial native network data in amber so grouped connection counts are never presented as silently complete.
- Per-PID Process view with user, CPU, private bytes, working set, peak working set, threads, and executable path. The visible-row summary reconciles sampled CPU and memory totals, including partial-sample counts. Search filters the latest snapshot instantly by PID, name, user, or path without recollecting processes on every keystroke.
- One Process **Refresh** action rebuilds usernames/executable paths and refreshes process values; Live monitoring reuses the synchronized identity cache for responsiveness.
- Process working-set Trim is kept on the Memory page rather than duplicated as a per-process action.
- Network view showing application, PID, user, protocol, local endpoint, remote endpoint, state, and executable path. All-column search and typed column sorting operate instantly on the latest snapshot and persist across Live refreshes.
- Stable total adapter bandwidth sampling tracks each active interface independently and ignores new, removed, or reset counters until a second monotonic sample exists.
- Selection-aware **Open Folder** and **Copy Path** actions in Apps, Processes, and Network. Folder launch uses Windows shell activation directly and never opens a command prompt.
- Force Kill validates process start time before acting, rejects reused stale PIDs, and prevents Better Task Manager from force-killing itself. Its confirmation includes process name, PID, path, and child-process scope.
- Process actions share a non-overlapping busy state: Force Kill, Open Folder, and Copy Path disable during Process refresh/mutation and recover together afterward.
- Native IPv4/IPv6 TCP and UDP collection with owning-process IDs.
- Partial native collection resilience: a failed address-family/protocol table produces a scoped warning while healthy TCP/UDP tables remain visible; all failures still stop the snapshot.
- Synchronized per-PID identity caching shared by Apps, Processes, Network, and History collectors. Successful and access-denied path/user lookups are reused until the process exits or its PID is reused.
- A shared asynchronous snapshot gate prevents Apps, Processes, Network, and live History collectors from running concurrently. Queued work for pages that are no longer active is discarded instead of consuming CPU or applying stale UI results.
- Automatic Live refresh failures stay inline and change the global status to red **Live error** instead of opening recurring modal dialogs. A successful later tick restores green **Live**; explicit manual refresh failures still show a dialog.
- Unexpected exceptions write contextual, cross-process-safe crash reports capped at 1 MiB with one rotated previous log.
- Window size, maximized state, selected refresh interval, and user-resized Apps/Processes/Network/History columns are saved atomically in `%LOCALAPPDATA%\BetterTaskManager\settings.json`. Restored dimensions and column widths are clamped safely; Live itself always starts paused.
- Global navigation, Apps cards/actions, and Process/Network/History command bars autosize and wrap at narrow window widths instead of clipping controls. The Apps split uses proportional columns while data tables retain horizontal scrolling.
- Explicit WinForms `PerMonitorV2` DPI awareness with generated application bootstrap, allowing forms, child controls, common controls, and dialogs to rescale when moved between monitors with different scaling.
- Global keyboard shortcuts with tooltip discovery: **F5** refreshes the active view, **Ctrl+F** focuses its filter, **Escape** clears it, **Ctrl+E** exports it, and **Ctrl+L** toggles Live monitoring.
- **Ctrl+1** through **Ctrl+5** navigate Apps, Processes, Network, History, and Memory; **Page Up/Page Down** browse History result pages.
- Right-click a page's non-editing surfaces to access that section's own actions—for example Refresh/Force Kill/Export in Processes or Refresh/Block/Unblock/Export in Network.
- Optional live monitoring with 1, 2, 5, or 15-second refresh intervals for Apps, Processes, Network, History, and Memory views.
- Snapshot timestamps on Apps, Processes, and Network so grouped and per-PID samples can be compared accurately.
- One-click **View Processes** reconciliation showing the exact contributing PIDs from the same Apps snapshot, with visible-row private-byte and working-set sums.
- Model-based CSV export for filtered/sorted Apps, Processes, Network, and History. Exports include invariant snapshot/numeric fields, explicit CPU availability, complete identity/path data, and spreadsheet-formula protection rather than scraping localized grid text.
- Per-app Windows Firewall block/unblock actions. Standard mode requests just-in-time administrator approval through a console-free helper while the main window remains open.
- One cross-view firewall mutation gate keeps Apps and Network rule actions non-overlapping and selection/refresh aware.
- **Not blocked by BTM** means no Better Task Manager outbound block rule exists for that executable; other Windows Firewall policies may still block it.
- Global Standard/Administrator status with **Restart as Admin** available from every page; system-memory controls remain disabled until elevation, while firewall buttons request approval when used.
- Memory cleanup tools:
  - Live physical load, used/available RAM, system cache, and system commit/limit dashboard.
  - Native System CPU usage derived from Windows idle/kernel/user time deltas, with an explicit first-sample state.
  - Double-buffered System CPU and physical RAM-load charts retaining the latest 60 Memory refresh samples without an additional timer.
  - Trim app working sets.
  - Bulk trim excludes Better Task Manager itself and reports trimmed, failed/inaccessible, and skipped process counts.
  - Clear standby cache.
  - Release system cache.
- Memory maintenance actions share one non-overlapping busy gate; native standby/system cache work runs off the UI thread and restores privilege-aware controls after success or failure.
- Bulk working-set Trim separates protected/access-denied processes, processes that exited during the scan, unexpected failures, and intentional System/BTM skips, with privilege-aware guidance.
- Elevated startup now activates and verifies `SeProfileSingleProcessPrivilege` before enabling the two system-memory actions; elevation and actual action capability are reported separately.
- Softer violet-slate dark UI with violet selection/accent states plus native dark controls and scrollbars.
- Automatic German localization when the Windows display language is German, covering controls, headers, menus, tooltips, dynamic statuses, confirmations, and errors; English remains the fallback. `--language=de` and `--language=en` provide explicit overrides.
- Connection-change history with live native connection sampling, 30-day retention, duplicate suppression, one-second change granularity, instant all-column filtering, typed timestamp/PID/port sorting, responsive paging, complete filtered export, and a confirmed **Clear History** action for the user-local store.
- A persisted **Record history** checkbox stops all future connection-history writes without deleting existing rows; live History remains available as a read-only refresh while recording is off.
- Path-scoped cross-process History locking protects append/load/prune/clear when two app instances overlap, including Restart-as-Admin handoff.

## Current Preview Limits

This is not yet a Portmaster replacement.

- Per-app upload/download bandwidth is not implemented yet.
- Current bandwidth display is a stable aggregate of matched adapters, not per-process.
- Network collection uses native Windows IP Helper connection tables and process mapping, not a dedicated WFP/ETW service.
- True Portmaster-style traffic attribution, live rule engine, DNS visibility, and long-running background collection require a Windows service and WFP/ETW collector.

## Requirements

- Windows 10/11.
- The packaged portable preview includes its own .NET runtime and does not require a separate .NET installation.
- Building from source requires the .NET 11 SDK.
- Administrator approval is requested only when using firewall or system-memory maintenance actions.

This project currently targets:

```text
net11.0-windows
```

## Build

From the repository root:

```powershell
dotnet build .\BetterTaskManager.slnx -c Release
```

## Self-test

After building, run the non-destructive command, CSV/history, native network collector, and UI-construction checks with:

```powershell
dotnet .\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.dll --self-test
```

The watchdog-backed UI smoke test briefly opens the app and verifies responsive History loading/live sampling plus cached Process filtering and sorting:

```powershell
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-smoke-test
```

The longer non-destructive UI soak test repeats Apps, Processes, Network, History, and Memory refreshes across three page-switching rounds. It enforces an eight-second per-refresh ceiling, verifies the UI message pump recovers after each refresh, and checks that refresh gates and action controls return to idle:

```powershell
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-soak-test
```

## Continuous Integration

`.github/workflows/windows-ci.yml` runs on Windows for pushes, pull requests, and manual dispatch. It installs the .NET 11 preview channel, restores and builds Release, runs the self-test plus English/German UI smoke and repeated soak modes, publishes the self-contained executable, and uploads it as a 14-day workflow artifact. The workflow becomes active when this branch is pushed to GitHub.

## Publish

```powershell
.\scripts\publish-v1.ps1
```

If a tester currently has the stable latest executable open, stage only the numbered folder and ZIP without interrupting that session:

```powershell
.\scripts\publish-v1.ps1 -SkipLatest
```

Run the normal publish command after the old latest window closes to refresh the stable path.

The self-contained, single-file Windows x64 preview will be placed in:

```text
artifacts\BetterTaskManager-v1.1.0-preview.53-portable-win-x64
```

Run:

```text
artifacts\BetterTaskManager-v1.1.0-preview.53-portable-win-x64\BetterTaskManager.exe
```

The publish script also refreshes this stable path on every successful build, so testers do not need to locate the newest numbered preview folder:

```text
artifacts\BetterTaskManager-latest-portable-win-x64\BetterTaskManager.exe
```

Each portable folder contains `BetterTaskManager.exe`, `README.md`, `RELEASE_NOTES-v1.1-preview.md`, `TESTING.md`, `CHANGELOG.md`, `SECURITY.md`, `LICENSE`, and `SHA256SUMS.txt`. Start with the preview release notes, use `TESTING.md` for a non-destructive issue-by-issue checklist, then verify the executable from inside that folder with:

```powershell
(Get-FileHash .\BetterTaskManager.exe -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content .\SHA256SUMS.txt
```

## Download

The v1.0 Windows x64 zip is also checked into this repository:

```text
release-assets\BetterTaskManager-v1.0-win-x64.zip
```

## Safety Notes

This app can force-kill processes, trim memory, clear standby cache, and add/remove Windows Firewall rules. It starts unelevated and offers **Restart as Admin** for privileged actions. Use those actions carefully.

Windows uses free RAM as cache on purpose. Memory cleanup tools are intended for troubleshooting, not routine maintenance.

**Clear History** permanently removes Better Task Manager's saved connection observations. If Live monitoring remains enabled, new observations can be recorded again immediately.

## Roadmap

The next major step is replacing the current live network snapshot approach with a background collector:

- Windows service running with admin privileges.
- ETW/WFP traffic capture.
- Per-process upload/download counters.
- Per-app history with one-month retention.
- Richer Portmaster-style app detail view.
- Rule list showing exactly why an app is allowed or blocked.
