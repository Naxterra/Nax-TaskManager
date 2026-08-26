# Roadmap

## v1.x

- Improve app-based layout and visual polish.

## Completed after v1.0

- Replaced localized `netstat` parsing with native Windows IPv4/IPv6 TCP and UDP tables.
- Added selectable live monitoring for Apps, Processes, Network, History, and Memory.
- Added bounded, deduplicated 30-day connection history.
- Added a softer blue-slate theme with native dark scrollbar support.
- Added CSV export for Process, Network, and History views.
- Clarified Better Task Manager firewall rule state and selected-app rule details.
- Added a native real-time system Memory dashboard.
- Made Process searching an instant in-memory operation over the latest complete snapshot.
- Added cached all-column Network search with persistent typed sorting and fixed-width rendering.
- Added synchronized process identity and CPU caches to reduce repeated work during Live monitoring.
- Made Apps search, typed sorting, and selection persistent over cached grouped snapshots.
- Added responsive paging through the complete filtered and sorted History cache.
- Serialized heavy snapshot collection and suppressed stale work during rapid page navigation.
- Replaced recurring automatic Live error dialogs with recoverable inline status.
- Made History sorting type-safe for mixed TCP/UDP endpoint data.
- Added a confirmed atomic reset for user-local connection history.
- Added same-snapshot grouped CPU to the Apps view with per-PID reconciliation tests.
- Distinguished unavailable first-snapshot CPU from measured idle and tied baselines to process identity.
- Added native System CPU to the real-time Memory dashboard.
- Added safe user-local persistence for window state and refresh interval.
- Persisted clamped data-column widths and corrected minimized/maximized restoration.
- Made navigation and dense command surfaces responsive at narrow window widths.
- Enabled and verified PerMonitorV2 scaling for mixed-DPI displays.
- Added complete grouped Apps CSV export from the cached filtered/sorted model.
- Converted Process and Network exports to typed models and unified asynchronous CSV saving.
- Added discoverable active-page keyboard shortcuts and removed focus-triggered Network refresh.
- Added keyboard view navigation and History paging through the shared navigation route.
- Replaced unstable aggregate bandwidth deltas with per-adapter monotonic sampling.
- Added safe selected executable path copy/open-folder workflows across primary views.
- Hardened Force Kill and Trim against PID reuse and self-termination.
- Made bulk working-set trim self-safe, counted, and exception-safe.
- Moved native Memory maintenance off the UI thread behind one busy gate.
- Centralized Process mutation/path action state and prevented overlapping operations.
- Unified Apps/Network firewall eligibility behind one cross-view mutation gate.
- Added cross-process transaction locking for the shared connection-history file.
- Added Windows CI for build, tests, portable publish, and artifact upload.
- Made portable packages deterministic and self-describing with an internal checksum manifest.
- Added bounded real-time CPU and RAM-load trend charts to Memory.
- Staged Apps rendering ahead of guarded asynchronous firewall enrichment.
- Added bounded partial-result resilience to native IPv4/IPv6 TCP/UDP collection.
- Surfaced partial network completeness in Apps and clarified identity-cache reload semantics.

## v2.0

- Add a Windows background service.
- Add ETW/WFP traffic collection.
- Track upload/download per app.
- Store one month of per-app traffic history.
- Add Portmaster-style connection grouping and rule explanations.

