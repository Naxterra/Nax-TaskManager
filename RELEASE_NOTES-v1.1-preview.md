# Better Task Manager v1.1 Preview

This preview is a substantial update to the checked-in v1.0 release. It remains a portable, self-contained Windows x64 application and does not require a separate .NET installation.

## Highlights

- Responsive violet-slate dark interface with native dark controls, dark scrollbars, PerMonitorV2 scaling, narrow-window wrapping, saved window/column preferences, and global keyboard shortcuts.
- Apps master/detail sizing now keeps the master list usable at small widths while capping it on ultrawide windows so detail content expands naturally.
- Only one app instance runs per signed-in Windows user/session; a second launch restores the existing window, while **Restart as Admin** safely hands ownership to the elevated replacement.
- German UI localization follows the Windows display language automatically and covers static/dynamic UI text, menus, tooltips, dialogs, headers, and common states; English remains available via `--language=en`.
- Apps, Processes, Network, History, and Memory all support manual or selectable 1/2/5/15-second Live refresh where applicable.
- Grouped Apps and per-PID Processes use explicit same-snapshot CPU, Private Bytes, and Working Set semantics with reconciliation counts and unavailable-CPU sampling states.
- Native IPv4/IPv6 TCP/UDP collection replaces localized `netstat` parsing and preserves healthy table results when one address-family/protocol source fails.
- Cached filtering and typed sorting avoid recollection on every search keystroke; filtered/sorted model exports are available for Apps, Processes, Network, and History.
- Process manual Refresh now includes username/path rebuilding, Peak Working Set is fully visible, and per-process Trim is removed in favor of the Memory-page action.
- Page-specific right-click menus mirror each section's own actions and select the clicked grid row before exposing target-sensitive operations.
- History records new or changed connections with one-second sampling granularity, duplicate suppression, 30-day retention, 2,000-row view caching, responsive 100-row paging, cross-process file locking, filtered export, and confirmed clearing.
- A persisted **Record history** checkbox stops future disk writes while keeping existing history available for viewing and export.
- Every successful local publish updates a stable `BetterTaskManager-latest-portable-win-x64` folder and ZIP, avoiding stale numbered-preview launches.
- Apps search text is vertically centered, and detail headings use tight rendering without apparent leading spaces; all detail content shares one left edge, including firewall status on its own row.
- Unsorted grid headers remain clean while the active sort column shows one larger violet-white triangle at its right edge, including Remote Port.
- Memory includes native System CPU, physical/commit/cache counters, bounded 60-sample CPU/RAM trends, and non-blocking maintenance actions.
- Bulk Trim explains how many processes were protected/access denied, exited during enumeration, failed unexpectedly, or were intentionally skipped instead of combining them into one ambiguous failure count.
- Elevated sessions explicitly activate and verify the Windows system-memory privilege before enabling standby/system-cache maintenance, avoiding misleading administrator-only checks.
- Firewall, Process mutation, Memory maintenance, snapshot collection, and History persistence have explicit non-overlapping gates and stale-result safeguards.
- Force Kill/Trim validate process start time, reject PID reuse, and protect Better Task Manager's own process.
- Portable packages include documentation, security/privacy guidance, license, and an executable SHA-256 manifest. Windows CI mirrors restore, build, self-test, UI smoke, and publish steps after the branch is pushed.
- A proper English/German Windows installer supports current-user or all-users installation, Start Menu and optional desktop shortcuts, in-place upgrades, Add/Remove Programs uninstall, and dynamic light/dark wizard styling.
- A custom violet performance-chart icon is embedded at multiple resolutions for consistent Setup, shortcut, taskbar, and application identity.

## Upgrade and Local Data

Use the setup executable for a normal installed experience, or extract the portable ZIP and run `BetterTaskManager.exe` without installation. Both contain the same self-contained x64 application.

User-local data remains under `%LOCALAPPDATA%\BetterTaskManager`:

- `network-history.csv`
- `settings.json`
- `crash.log` and `crash.previous.log`

Existing history is retained and read by the preview. **Clear History** permanently resets the saved history but does not delete CSV exports saved elsewhere. Live monitoring always starts paused, even when the refresh interval is restored.

History recording remains enabled by default for compatibility. Disable **Record history** on the History page to stop future Apps/Network/History observations without erasing saved rows.

The checked-in `release-assets\BetterTaskManager-v1.0-win-x64.zip` remains unchanged as the stable v1.0 artifact.

Installer upgrades reuse a fixed application identity. Uninstall removes installed program files and shortcuts but intentionally preserves `%LOCALAPPDATA%\BetterTaskManager` settings, history, exports, and crash logs.

## Safety and Privacy

- The app starts unelevated. Firewall actions request just-in-time administrator approval while system-memory controls still use **Restart as Admin**.
- Better Task Manager firewall labels describe only rules created by this app; **Not blocked by BTM** means no BTM outbound block rule exists and does not claim that other firewall policies allow traffic.
- History, exports, and crash logs can contain usernames, executable paths, private IP addresses, and remote endpoints. Review them before sharing.
- The packaged SHA-256 manifest verifies executable integrity against the package; it is not a code-signing certificate.
- The preview setup executable is not Authenticode-signed, so Windows may show an unknown-publisher warning; verify it against the release SHA-256 manifest.

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
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-smoke-test --language=de
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-soak-test
.\scripts\publish-v1.ps1
```

The self-test plus UI smoke and three-round soak modes are non-destructive: they do not kill processes, alter firewall rules, or execute native memory cleanup actions.

For hands-on verification of the original freeze, console-window, Live monitoring, value-reconciliation, dark-scrollbar, alignment, privilege, and history-privacy reports, follow `TESTING.md` from the portable folder.
