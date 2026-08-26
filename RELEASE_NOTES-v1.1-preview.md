# Better Task Manager v1.1 Preview

This preview is a substantial update to the checked-in v1.0 release. It remains a portable, self-contained Windows x64 application and does not require a separate .NET installation.

## Highlights

- Responsive blue-slate dark interface with native dark controls, dark scrollbars, PerMonitorV2 scaling, narrow-window wrapping, saved window/column preferences, and global keyboard shortcuts.
- Apps, Processes, Network, History, and Memory all support manual or selectable 1/2/5/15-second Live refresh where applicable.
- Grouped Apps and per-PID Processes use explicit same-snapshot CPU, Private Bytes, and Working Set semantics with reconciliation counts and unavailable-CPU sampling states.
- Native IPv4/IPv6 TCP/UDP collection replaces localized `netstat` parsing and preserves healthy table results when one address-family/protocol source fails.
- Cached filtering and typed sorting avoid recollection on every search keystroke; filtered/sorted model exports are available for Apps, Processes, Network, and History.
- History records new or changed connections with one-second sampling granularity, duplicate suppression, 30-day retention, 2,000-row view caching, responsive 100-row paging, cross-process file locking, filtered export, and confirmed clearing.
- Memory includes native System CPU, physical/commit/cache counters, bounded 60-sample CPU/RAM trends, and non-blocking maintenance actions.
- Firewall, Process mutation, Memory maintenance, snapshot collection, and History persistence have explicit non-overlapping gates and stale-result safeguards.
- Force Kill/Trim validate process start time, reject PID reuse, and protect Better Task Manager's own process.
- Portable packages include documentation, security/privacy guidance, license, and an executable SHA-256 manifest. Windows CI mirrors restore, build, self-test, UI smoke, and publish steps after the branch is pushed.

## Upgrade and Local Data

No installer migration is required: extract the preview ZIP and run `BetterTaskManager.exe`.

User-local data remains under `%LOCALAPPDATA%\BetterTaskManager`:

- `network-history.csv`
- `settings.json`
- `crash.log` and `crash.previous.log`

Existing history is retained and read by the preview. **Clear History** permanently resets the saved history but does not delete CSV exports saved elsewhere. Live monitoring always starts paused, even when the refresh interval is restored.

The checked-in `release-assets\BetterTaskManager-v1.0-win-x64.zip` remains unchanged as the stable v1.0 artifact.

## Safety and Privacy

- The app starts unelevated and enables privileged controls only after an explicit **Restart as Admin**.
- Better Task Manager firewall labels describe only rules created by this app; **No BTM Block** does not claim that other firewall policies allow traffic.
- History, exports, and crash logs can contain usernames, executable paths, private IP addresses, and remote endpoints. Review them before sharing.
- The packaged SHA-256 manifest verifies executable integrity against the package; it is not a code-signing certificate.

See `SECURITY.md` for the complete current security and privacy model.

## Known Limits

- Per-app upload/download attribution is not implemented. The displayed bandwidth is a stable aggregate of matched network adapters.
- DNS visibility, a long-running collector, persistent per-app traffic totals, and Portmaster-style rule decisions require the planned Windows service and ETW/WFP architecture.
- History records changes only while a view that collects network data is actively refreshed; it is not a background service when the desktop app is closed.
- Protected process identity and maintenance actions remain subject to Windows access controls even when elevated.
- The project intentionally targets the installed .NET 11 preview toolchain; the portable executable bundles its runtime.

## Validation

From the repository root:

```powershell
dotnet build .\BetterTaskManager.slnx -c Release
dotnet .\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.dll --self-test
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-smoke-test
.\scripts\publish-v1.ps1
```

The self-test and UI smoke modes are non-destructive: they do not kill processes, alter firewall rules, or execute native memory cleanup actions.
