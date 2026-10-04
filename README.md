# Nax-TaskManager

A Windows 11 Task Manager replacement whose numbers add up, with per-app network monitoring in the spirit of Portmaster: which app talks to which host, how fast, and how much over the last 30 days.

Built with WinUI 3 (Fluent design) on .NET 11. Formerly *Better Task Manager*.

## Features

- **Processes** — apps and background processes grouped like Task Manager, with icons, CPU, memory, disk I/O, per-app network speed, connection count, publisher and path. End task (creation-time checked, so a reused PID is never killed by mistake), end process tree, open file location, properties, and per-app outbound firewall blocking.
- **Memory that reconciles** — the Memory column is the private working set, the same figure Task Manager shows. The Performance page splits "In use" into apps, kernel, file cache, drivers and shared memory so the parts add up, and offers *Free up memory* (trim app memory, clear standby cache, empty all working sets) as troubleshooting tools.
- **Network** — every connection grouped by app with remote host names (from the Windows DNS cache, live DNS events, or reverse DNS), per-connection speed, total data per app, sorting, search and pause.
- **History** — an optional background service records connections and traffic per app for 30 days, and lets the app show per-app network speed without administrator rights.
- Light and dark theme, Mica, keyboard shortcuts (Ctrl+1–4 pages, Ctrl+F search).

## Requirements

- Windows 11, x64
- [.NET 11 desktop runtime](https://dotnet.microsoft.com/download/dotnet/11.0) (x64)

## Install

Download `NaxTaskManager-v<version>-setup-win-x64.exe` from [Releases](https://github.com/Naxterra/Nax-TaskManager/releases). Setup installs for the current user (no administrator rights needed) into `%LOCALAPPDATA%\Programs\Nax-TaskManager` and replaces an older Better Task Manager install. A portable ZIP is attached to each release as well.

## Administrator rights

The app starts without elevation. These features need administrator rights:

| Feature | Without admin | With admin or the History service |
|---|---|---|
| Per-app network speed and data | Shown when the History service runs | Measured by the app itself (kernel TCP/IP trace) |
| Path, publisher and icon of system processes | Hidden for protected processes | Shown |
| Firewall block/allow | One UAC prompt per change | Direct |
| Clear standby cache, empty all working sets | Unavailable | Available |
| Install the History service | One UAC prompt | Direct |

Use **Restart as administrator** in the app, or turn on **Settings → History → Record in the background** once.

## Privacy

Nothing leaves the PC except DNS reverse lookups for addresses without a known host name (sent to your configured DNS server). There is no telemetry.

The History service stores its database in `C:\ProgramData\NaxTaskManager`. Every local user account can read it; only administrators can change it. Settings and the crash log are in `%LOCALAPPDATA%\NaxTaskManager`.

## Build from source

```powershell
dotnet build BetterTaskManager.slnx -c Release
scripts\publish-fluent.ps1      # portable folder + ZIP in artifacts\
scripts\build-installer.ps1     # Inno Setup installer + SHA256SUMS
scripts\test-installer.ps1      # install / upgrade / smoke test / uninstall under a test AppId
```

Requires the .NET 11 SDK. Inno Setup 6 is downloaded automatically if it is not installed.

| Project | Purpose |
|---|---|
| `src/BetterTaskManager.Core` | Collection without UI: native process reader, network tables, kernel network trace, DNS names, firewall rules, history store, service feed |
| `src/BetterTaskManager.Fluent` | The WinUI 3 app (`NaxTaskManager.exe`) |
| `src/BetterTaskManager.HistoryService` | The background history service (`NaxTaskManager.HistoryService.exe`) |

Design notes, verified measurements and the roadmap are in [docs/FLUENT-REBUILD.md](docs/FLUENT-REBUILD.md).

## License

MIT — see [LICENSE](LICENSE).
