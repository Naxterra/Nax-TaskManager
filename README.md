# Better Task Manager

Better Task Manager is a Windows desktop tool for admins who want a more practical view of processes, memory, firewall state, and live network connections.

The app is written in C#/.NET WinForms. It starts normally and can restart itself with administrator rights when firewall or system-memory actions require elevation.

The current development build is `1.1.0-preview.4`. The checked-in v1.0 download remains the last stable release.

## Current Features

- App-based overview with an explicit process count and summed private-byte and working-set values for each executable group.
- Per-PID Process view with user, CPU, private bytes, working set, peak working set, threads, and executable path.
- Network view showing application, PID, user, protocol, local endpoint, remote endpoint, state, and executable path.
- Native IPv4/IPv6 TCP and UDP collection with owning-process IDs.
- Optional live monitoring with 1, 2, 5, or 15-second refresh intervals for Apps, Processes, and Network views.
- Snapshot timestamps on Apps, Processes, and Network so grouped and per-PID samples can be compared accurately.
- One-click **View Processes** reconciliation showing the exact contributing PIDs from the same Apps snapshot, with visible-row private-byte and working-set sums.
- CSV export for the current Process, Network, and bounded History views.
- Per-app Windows Firewall block/unblock actions.
- Better Task Manager block-rule status in the app list, plus the exact outbound rule explanation for the selected app.
- Memory cleanup tools:
  - Trim app working sets.
  - Clear standby cache.
  - Release system cache.
- Softer blue-slate dark UI with native dark controls and scrollbars.
- Connection-change history with 30-day retention, duplicate suppression, and a 2,000-row display limit.

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

## Publish

```powershell
.\scripts\publish-v1.ps1
```

The self-contained, single-file Windows x64 preview will be placed in:

```text
artifacts\BetterTaskManager-v1.1.0-preview.4-portable-win-x64
```

Run:

```text
artifacts\BetterTaskManager-v1.1.0-preview.4-portable-win-x64\BetterTaskManager.exe
```

## Download

The v1.0 Windows x64 zip is also checked into this repository:

```text
release-assets\BetterTaskManager-v1.0-win-x64.zip
```

## Safety Notes

This app can force-kill processes, trim memory, clear standby cache, and add/remove Windows Firewall rules. It starts unelevated and offers **Restart as Admin** for privileged actions. Use those actions carefully.

Windows uses free RAM as cache on purpose. Memory cleanup tools are intended for troubleshooting, not routine maintenance.

## Roadmap

The next major step is replacing the current live network snapshot approach with a background collector:

- Windows service running with admin privileges.
- ETW/WFP traffic capture.
- Per-process upload/download counters.
- Per-app history with one-month retention.
- Richer Portmaster-style app detail view.
- Rule list showing exactly why an app is allowed or blocked.
