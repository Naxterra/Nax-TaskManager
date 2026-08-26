# Changelog

## 1.1.0-preview.28 - Unreleased

### Added

- Added global live monitoring for Apps, Processes, and Network with selectable 1, 2, 5, and 15-second intervals.
- Added isolated self-test coverage for connection-history deduplication, CSV round trips, display limits, and 30-day pruning.
- Added non-blocking CSV export for Process, Network, and History views, including spreadsheet-formula protection.
- Added a self-contained single-file Windows x64 publish profile so preview testers do not need a separate .NET runtime installation.
- Added one-click grouped-app reconciliation: **View Processes** opens the contributing PIDs from the same snapshot and shows visible-row memory sums.
- Added a native Memory dashboard with physical load, used/available RAM, system cache, commit total/limit/peak, and process/thread/handle counts; it participates in selectable Live monitoring.
- Added global privilege status and **Restart as Admin** navigation; firewall and system-level memory controls are gated consistently in Standard mode.
- Added instant all-column filtering to the History view; sorting and CSV export operate on the complete filtered result.
- Added a watchdog-backed UI smoke test that verifies responsive History loading/live sampling, cached Process filtering/sorting, same-snapshot PID scope, and a responsive message-loop continuation.
- Added real live monitoring to History: the active page now samples native connections, records new/state-changed observations, and refreshes the current filtered view at the selected interval.
- Added instant all-column Network search across application, PID, user, protocol, endpoints, state, and path.
- Added explicit per-PID resolution state so successful and denied executable-path/user lookups are both cached across collectors.
- Expanded Apps search across name, path, user, firewall state, process IDs/count, and connection count.
- Added Previous/Next paging through every filtered and sorted History result while retaining the responsive 100-row render window.
- Added one shared asynchronous snapshot gate for Apps, Processes, Network, details reload, and live History collection.
- Added a global red **Live error** state that returns to green **Live** after the next successful automatic refresh.
- Added typed History sorting for timestamp, PID, local port, and remote port columns.
- Added a confirmed **Clear History** action for permanently removing the user-local connection-history store without administrator rights.
- Added grouped Apps CPU as the sum of normalized per-PID CPU values from the exact same Process snapshot.
- Added explicit per-PID CPU sample availability and visible-row CPU sums/partial-sample counts in Processes.
- Added native System CPU sampling to the Memory dashboard using Windows idle, kernel, and user time deltas.
- Added user-local persistence for window size, maximized state, and the selected Live refresh interval.
- Added persistence for user-resized Apps, Processes, Network, and History column widths.
- Added responsive multi-row wrapping for global navigation and dense page command bars.
- Added explicit `PerMonitorV2` high-DPI configuration and runtime verification.
- Added non-blocking CSV export for the current filtered and sorted Apps model.
- Added typed model-based Process and Network CSV exports with snapshot provenance and invariant numeric values.
- Added global F5, Ctrl+F, Escape, Ctrl+E, and Ctrl+L shortcuts with contextual tooltip guidance.
- Added Ctrl+1–5 view navigation and Page Up/Page Down History paging, including numpad navigation support.

### Changed

- Replaced localized `netstat` text parsing with native Windows IPv4/IPv6 TCP and UDP tables that include owning process IDs.
- The app now starts without elevation or a console and can directly restart its executable as administrator when needed.
- Reworked connection history to record new or changed connections instead of duplicating every full snapshot.
- Replaced the near-black palette with a softer blue-slate theme and enabled the supported WinForms dark color mode for native controls and scrollbars.
- Replaced the misleading generic `Allowed` firewall label with rule-specific `BTM Blocked` and `No BTM Block` states and an exact outbound-rule explanation.
- Clarified app aggregation with a process-count column, precise Private Bytes/Working Set names, shared-page overlap guidance, summed-memory labels, and snapshot timestamps on all live views.
- Process search now filters the latest complete snapshot in memory by PID, name, user, or path; only manual/Live refresh performs a new Windows process collection.
- Network filtering and typed column sorting now operate on the cached snapshot and persist across manual or Live collection refreshes.
- Split Network actions/search from its snapshot and bandwidth status line, and assigned stable column widths for a calmer layout.
- Process identity cache entries now carry process start time, are shared safely by Apps, Processes, Network, and History, and are invalidated when Windows reuses a PID.
- Apps filtering and typed sorting now operate on the cached grouped snapshot; active sort and selected app persist across search and Live refreshes.
- History filtering or sorting returns to the first result page; manual and Live reloads preserve the current page and clamp it safely when the result set shrinks.
- Queued collection requests now re-check the active page before running, and completed requests re-check it before updating caches or UI.
- Automatic and manual refresh origins now propagate through the active-page dispatcher so error presentation can match user intent.
- History text columns remain case-insensitive while numeric and timestamp columns now sort by their real values.
- Clearing History atomically rewrites the CSV header, resets cached rows/paging, and resets connection deduplication so later observations can be recorded normally.
- Apps CPU is visible, locale-formatted, searchable, numerically sortable, and included in selected-app snapshot metadata with a reconciliation tooltip.
- First snapshots now display `...` instead of a false `0.0%`; measured idle remains `0.0`, and grouped Apps metadata reports sampling or partial coverage.
- System CPU participates in Memory Live monitoring, uses the same first-sample marker, and follows the existing green/warning/danger thresholds.
- Restored window dimensions are clamped to the current primary working area; Live enabled state is intentionally not persisted and always starts paused.
- Restored column widths are clamped to 40–1200 pixels, and closing while minimized preserves the last non-minimized maximized state correctly.
- The Apps master/detail split is now proportional; metric cards and actions wrap as the detail pane narrows while tables remain scrollable.
- Replaced duplicated legacy WinForms startup calls with generated `ApplicationConfiguration.Initialize()` bootstrap while preserving native and supported dark-mode initialization.
- Apps export writes snapshot time, firewall, process count, CPU and sampled-process count, connections, Private Bytes, Working Set, user, and executable path using invariant numeric values.
- Process export records CPU availability explicitly; Network export preserves native endpoint fields and normalized connection state. All four views now share one asynchronous dialog/write workflow.
- Shortcut actions route through the active page's existing manual refresh, cached filter, export, and Live semantics; Memory intentionally has no filter/export shortcut action.
- Mouse clicks and keyboard view changes now share one asynchronous navigation route, preventing refresh behavior from drifting between input methods.

### Fixed

- Added the missing History navigation entry so saved connection snapshots can be viewed from the app.
- Firewall and force-kill actions now report Windows command failures and timeouts instead of presenting them as successful.
- External command arguments are passed without manual quoting, including executable paths containing spaces.
- Removed the per-application firewall command loop from UI rendering; firewall status is collected once in the background on manual refresh.
- Moved history persistence off the UI thread, guarded overlapping refreshes, stopped rebuilding hidden grids, and preserved grid selection and scroll position during live updates.
- Apps now refresh when navigating back to the grouped view, preventing a stale Apps snapshot from being compared with a newly refreshed Processes snapshot without an explicit timestamp.
- Network collection now resolves user/path details once per PID and reuses the same-snapshot Process rows during Apps refresh instead of repeating protected-process lookups for every connection.
- Replaced the expensive History data grid with a fixed-column virtual list and a 100-row render window over the newest 2,000 cached records, eliminating multi-second UI freezes without truncating filtered CSV exports.
- Reduced the connection-history sampling gate from 30 seconds to one second so short-lived changes can be captured during Live monitoring while unchanged rows remain deduplicated.
- Eliminated full process enumeration and protected path/user lookups on every Process search keystroke while preserving active column sorting and exact **View Processes** PID reconciliation.
- Removed content-based Network column resizing during refresh, avoiding repeated width measurement on large connection snapshots.
- Eliminated repeated protected-process path/user calls on every Live tick and synchronized the shared CPU baseline cache to prevent concurrent Apps/Processes refresh races.
- Fixed the Apps firewall column falling back to name sorting and stopped search or refresh from silently discarding the active Apps sort.
- Removed the earlier limitation that made History matches beyond the first 100 visible only through filtering or CSV export.
- Prevented rapid cross-page navigation from running multiple expensive native collectors concurrently or applying results to a page the user already left.
- Prevented recurring Process or Network error dialogs from stacking during Live monitoring; automatic failures are reported inline while manual refresh failures remain modal.
- Fixed History Remote Port sorting throwing when blank UDP ports and numeric TCP ports were present together.
- Serialized History clearing with live store writes so clearing cannot leave a partial CSV or stale deduplication state.
- Extended grouped-app reconciliation tests to prove CPU, private bytes, and working set all equal their contributing per-PID sums.
- CPU baselines now include process start time, preventing a reused PID from inheriting another process's CPU sample; normalized values are clamped to 0–100%.
- Added deterministic native CPU calculation tests for valid deltas and invalid counter rollback, plus UI coverage for initial sampling state.
- Settings writes are atomic, corrupt JSON falls back to defaults, and self/UI tests use isolated temporary settings files rather than the real user profile.
- Extended settings tests cover column-width round trips, UI capture, clamping, and minimized/maximized state policy.
- Added minimum-window UI bounds checks for navigation, Apps cards/actions, and Process/Network/History toolbars.
- Added UI smoke coverage proving the packaged app starts in `PerMonitorV2` mode without regressing responsive layout or dark controls.
- Added Apps export schema/reconciliation tests, blank unavailable CPU handling, and spreadsheet-formula protection for every model field.
- Replaced Process/Network grid-text scraping with deterministic export schemas and added exact field tests for CPU, memory, endpoints, state, identity, and timestamp.
- Removed the redundant Network panel Enter refresh so focusing its filter no longer starts an unintended native collection.
- Added runtime shortcut tests for actual Memory navigation and both History paging directions in addition to exhaustive static mapping coverage.

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

