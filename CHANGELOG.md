# Changelog

## 1.1.0-preview.2 - Unreleased

### Added

- Added global live monitoring for Apps, Processes, and Network with selectable 1, 2, 5, and 15-second intervals.
- Added isolated self-test coverage for connection-history deduplication, CSV round trips, display limits, and 30-day pruning.
- Added non-blocking CSV export for Process, Network, and History views, including spreadsheet-formula protection.
- Added a self-contained single-file Windows x64 publish profile so preview testers do not need a separate .NET runtime installation.
- Added one-click grouped-app reconciliation: **View Processes** opens the contributing PIDs from the same snapshot and shows visible-row memory sums.

### Changed

- Replaced localized `netstat` text parsing with native Windows IPv4/IPv6 TCP and UDP tables that include owning process IDs.
- The app now starts without elevation or a console and can directly restart its executable as administrator when needed.
- Reworked connection history to record new or changed connections instead of duplicating every full snapshot.
- Replaced the near-black palette with a softer blue-slate theme and enabled the supported WinForms dark color mode for native controls and scrollbars.
- Replaced the misleading generic `Allowed` firewall label with rule-specific `BTM Blocked` and `No BTM Block` states and an exact outbound-rule explanation.
- Clarified app aggregation with a process-count column, summed-memory labels, aligned working-set terminology, and snapshot timestamps on all live views.

### Fixed

- Added the missing History navigation entry so saved connection snapshots can be viewed from the app.
- Firewall and force-kill actions now report Windows command failures and timeouts instead of presenting them as successful.
- External command arguments are passed without manual quoting, including executable paths containing spaces.
- Removed the per-application firewall command loop from UI rendering; firewall status is collected once in the background on manual refresh.
- Moved history persistence off the UI thread, guarded overlapping refreshes, stopped rebuilding hidden grids, and preserved grid selection and scroll position during live updates.
- Apps now refresh when navigating back to the grouped view, preventing a stale Apps snapshot from being compared with a newly refreshed Processes snapshot without an explicit timestamp.

## v1.0.0 - 2026-06-06

Initial GitHub-ready release.

### Added

- App-based dashboard.
- Process table with user, path, CPU, and memory values.
- Network table with ports, destinations, users, and executable paths.
- Per-app firewall block/unblock actions.
- Firewall status in the app list.
- Memory cleanup actions.
- Dark UI pass, including native dark-mode requests.
- 30-day local connection snapshot cleanup.

### Known Limits

- Per-app bandwidth requires a future ETW/WFP collector service.
- Some native WinForms controls may still depend on Windows theme behavior.
- The UI is functional but still an early admin-tool shell.

