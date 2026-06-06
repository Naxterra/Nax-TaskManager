# Better Task Manager v1.0

This is the first GitHub-ready release of Better Task Manager.

## Highlights

- Dark app-based UI.
- Process, memory, firewall, and live network connection views.
- Per-app firewall block/unblock controls.
- App-level firewall status.
- Memory cleanup tools for troubleshooting.
- Local network snapshot retention capped to 30 days.

## Known Limits

- Per-app bandwidth is not available yet.
- Portmaster-style network attribution requires a future background service using ETW/WFP.
- The current UI is a functional admin shell, not a finished product design.

## Build

```powershell
dotnet build .\BetterTaskManager.slnx -c Release
```

## Publish

```powershell
.\scripts\publish-v1.ps1
```
