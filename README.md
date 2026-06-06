# Better Task Manager

Better Task Manager is a Windows desktop tool for admins who want a more practical view of processes, memory, firewall state, and live network connections.

The v1.0 app is written in C#/.NET WinForms and runs as administrator so it can query protected processes and create Windows Firewall rules.

## Current Features

- App-based overview with grouped processes.
- Process view with PID, user, CPU, memory, threads, and executable path.
- Network view showing application, PID, user, protocol, local endpoint, remote endpoint, state, and executable path.
- Per-app Windows Firewall block/unblock actions.
- Firewall status in the app list and selected-app summary.
- Memory cleanup tools:
  - Trim app working sets.
  - Clear standby cache.
  - Release system cache.
- Dark UI with native dark title bar and dark-mode request for native controls.
- Local connection snapshot retention capped to 30 days.

## Important Limits in v1.0

This is not yet a Portmaster replacement.

- Per-app upload/download bandwidth is not implemented yet.
- Current bandwidth display is adapter-level, not per-process.
- Network collection currently uses Windows connection data and process mapping, not a dedicated WFP/ETW service.
- True Portmaster-style traffic attribution, live rule engine, DNS visibility, and long-running background collection require a Windows service and WFP/ETW collector.

## Requirements

- Windows 10/11.
- .NET 11 Windows Desktop Runtime or .NET 11 SDK.
- Administrator rights for firewall and memory maintenance actions.

This project currently targets:

```text
net11.0-windows
```

## Build

From the repository root:

```powershell
dotnet build .\BetterTaskManager.slnx -c Release
```

## Publish

```powershell
.\scripts\publish-v1.ps1
```

The published app will be placed in:

```text
artifacts\BetterTaskManager-v1.0
```

Run:

```text
artifacts\BetterTaskManager-v1.0\BetterTaskManager.exe
```

## Download

The v1.0 Windows x64 zip is also checked into this repository:

```text
release-assets\BetterTaskManager-v1.0-win-x64.zip
```

## Safety Notes

This app can force-kill processes, trim memory, clear standby cache, and add/remove Windows Firewall rules. Use those actions carefully.

Windows uses free RAM as cache on purpose. Memory cleanup tools are intended for troubleshooting, not routine maintenance.

## Roadmap

The next major step is replacing the current live network snapshot approach with a background collector:

- Windows service running with admin privileges.
- ETW/WFP traffic capture.
- Per-process upload/download counters.
- Per-app history with one-month retention.
- Richer Portmaster-style app detail view.
- Rule list showing exactly why an app is allowed or blocked.
