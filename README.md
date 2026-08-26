# Better Task Manager

Better Task Manager is a Windows desktop tool for admins who want a more practical view of processes, memory, firewall state, and live network connections.

The app is written in C#/.NET WinForms. It starts normally and can restart itself with administrator rights when firewall or system-memory actions require elevation.

The current development build is `1.1.0-preview.26`. The checked-in v1.0 download remains the last stable release.

## Current Features

- App-based overview with explicit process count plus same-snapshot summed CPU, private-byte, and working-set values for each executable group. CPU displays `...` until a second snapshot exists, so an unavailable baseline is never mislabeled as measured idle. Cached all-field search, typed sorting, and selection persist across Live refreshes.
- Per-PID Process view with user, CPU, private bytes, working set, peak working set, threads, and executable path. The visible-row summary reconciles sampled CPU and memory totals, including partial-sample counts. Search filters the latest snapshot instantly by PID, name, user, or path without recollecting processes on every keystroke.
- Network view showing application, PID, user, protocol, local endpoint, remote endpoint, state, and executable path. All-column search and typed column sorting operate instantly on the latest snapshot and persist across Live refreshes.
- Native IPv4/IPv6 TCP and UDP collection with owning-process IDs.
- Synchronized per-PID identity caching shared by Apps, Processes, Network, and History collectors. Successful and access-denied path/user lookups are reused until the process exits or its PID is reused.
- A shared asynchronous snapshot gate prevents Apps, Processes, Network, and live History collectors from running concurrently. Queued work for pages that are no longer active is discarded instead of consuming CPU or applying stale UI results.
- Automatic Live refresh failures stay inline and change the global status to red **Live error** instead of opening recurring modal dialogs. A successful later tick restores green **Live**; explicit manual refresh failures still show a dialog.
- Window size, maximized state, selected refresh interval, and user-resized Apps/Processes/Network/History columns are saved atomically in `%LOCALAPPDATA%\BetterTaskManager\settings.json`. Restored dimensions and column widths are clamped safely; Live itself always starts paused.
- Global navigation, Apps cards/actions, and Process/Network/History command bars autosize and wrap at narrow window widths instead of clipping controls. The Apps split uses proportional columns while data tables retain horizontal scrolling.
- Explicit WinForms `PerMonitorV2` DPI awareness with generated application bootstrap, allowing forms, child controls, common controls, and dialogs to rescale when moved between monitors with different scaling.
- Optional live monitoring with 1, 2, 5, or 15-second refresh intervals for Apps, Processes, Network, History, and Memory views.
- Snapshot timestamps on Apps, Processes, and Network so grouped and per-PID samples can be compared accurately.
- One-click **View Processes** reconciliation showing the exact contributing PIDs from the same Apps snapshot, with visible-row private-byte and working-set sums.
- Model-based CSV export for filtered/sorted Apps, Processes, Network, and History. Exports include invariant snapshot/numeric fields, explicit CPU availability, complete identity/path data, and spreadsheet-formula protection rather than scraping localized grid text.
- Per-app Windows Firewall block/unblock actions.
- Better Task Manager block-rule status in the app list, plus the exact outbound rule explanation for the selected app.
- Global Standard/Administrator status with **Restart as Admin** available from every page; privileged controls are disabled until elevation.
- Memory cleanup tools:
  - Live physical load, used/available RAM, system cache, and system commit/limit dashboard.
  - Native System CPU usage derived from Windows idle/kernel/user time deltas, with an explicit first-sample state.
  - Trim app working sets.
  - Clear standby cache.
  - Release system cache.
- Softer blue-slate dark UI with native dark controls and scrollbars.
- Connection-change history with live native connection sampling, 30-day retention, duplicate suppression, one-second change granularity, instant all-column filtering, typed timestamp/PID/port sorting, responsive paging, complete filtered export, and a confirmed **Clear History** action for the user-local store.

## Important Limits in v1.0

This is not yet a Portmaster replacement.

- Per-app upload/download bandwidth is not implemented yet.
- Current bandwidth display is adapter-level, not per-process.
- Network collection uses native Windows IP Helper connection tables and process mapping, not a dedicated WFP/ETW service.
- True Portmaster-style traffic attribution, live rule engine, DNS visibility, and long-running background collection require a Windows service and WFP/ETW collector.

## Requirements

- Windows 10/11.
- The packaged portable preview includes its own .NET runtime and does not require a separate .NET installation.
- Building from source requires the .NET 11 SDK.
- Administrator rights only when using firewall and system-memory maintenance actions.

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

## Publish

```powershell
.\scripts\publish-v1.ps1
```

The self-contained, single-file Windows x64 preview will be placed in:

```text
artifacts\BetterTaskManager-v1.1.0-preview.26-portable-win-x64
```

Run:

```text
artifacts\BetterTaskManager-v1.1.0-preview.26-portable-win-x64\BetterTaskManager.exe
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
