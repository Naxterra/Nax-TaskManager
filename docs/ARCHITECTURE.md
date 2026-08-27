# Architecture

## Current Desktop Shape

Better Task Manager 1.1.0-preview.53 remains a single Windows desktop executable.

```text
WinForms UI
  -> process collector
  -> network connection snapshot collector
  -> Windows Firewall commands
  -> Windows memory maintenance APIs
```

## Current Collection Model

The app collects live process data directly from Windows process APIs. Process collection produces a complete snapshot; search, same-app PID scoping, and column sorting operate on that in-memory snapshot without triggering new protected-process lookups. IPv4 and IPv6 TCP/UDP endpoints and their owning process IDs come from the native Windows IP Helper tables (`GetExtendedTcpTable` and `GetExtendedUdpTable`). Each table is captured independently with bounded allocation and growth retries. Healthy tables remain usable when one source fails; scoped issues propagate to Network and live History status, while failure of all four sources aborts the snapshot.

Apps, Processes, Network, and History share a synchronized per-PID identity cache. Path/user resolution state is explicit, so both successful results and access-denied empty results are reused instead of retried on each Live tick. Process start time guards against PID reuse, and full process snapshots prune exited PIDs. Apps refresh also passes its same-snapshot process rows directly into network attribution. Network search and typed sorting operate on the complete in-memory snapshot; they do not invoke the native collector and remain applied when Live monitoring replaces the snapshot.

Partial native table issues propagate to Network, live History, and Apps. Apps uses an amber compact completeness message so grouped connection totals disclose missing source tables. Identity resolution is automatic; manual Process **Refresh** replaces the synchronized cache to retry every current PID explicitly, while Live uses cached results.

Apps refresh is staged: the serialized heavy snapshot pipeline returns process/network/group data to the UI first, then optional firewall enumeration runs separately. The late result carries requested Apps snapshot time and firewall mutation revision; either mismatch discards it. Firewall-only exceptions remain scoped to the rule detail label and cannot turn the already-rendered Apps snapshot into a failure.

Adapter bandwidth is sampled separately from connection ownership. Counters are keyed by stable network-interface ID; rate calculation includes only interfaces present in consecutive samples whose received/sent totals remain monotonic. Interface additions, removals, and resets therefore cannot create negative aggregate rates, but the result remains adapter-level rather than per-process.

Apps, Processes, and Network resolve selected executable paths through one active-page helper. Copy uses the STA clipboard with short contention retries; Open Folder passes the resolved directory to `ProcessStartInfo` with `UseShellExecute=true`, avoiding command construction and console windows. Button availability follows each view's current selection.

Process snapshots carry process start time in addition to PID. Force Kill compares this identity before confirmation/action and again inside the background operation, preventing a recycled PID from targeting a different process. It also rejects the application's own PID; zero/unavailable start times retain best-effort compatibility for protected processes.

Selected Process mutations use one UI busy gate. Centralized action-state calculation combines selection, current-process protection, Process refresh, and mutation state so kill and executable-path actions cannot overlap or remain enabled against a grid being replaced. `finally` restores eligibility after each mutation.

Bulk working-set trim enumerates processes off the UI thread, excludes PID 0 and the controlling Better Task Manager process, and categorizes protected/access-denied targets, processes that exited during enumeration, and unexpected failures separately. Its UI action is restored in `finally`, and only the active Memory snapshot is refreshed afterward.

All Memory maintenance shares a UI-level busy gate. Bulk trim, standby purge, and system working-set release cannot overlap; native work executes off the UI thread, all action buttons disable together, and `finally` restores controls according to the current per-action capability state. UI smoke coverage verifies these transitions without executing destructive native calls.

New and changed connection observations are written locally. Unchanged snapshots are suppressed, the minimum sampling interval is one second, and entries are pruned after 30 days. The store combines its object lock with a path-derived `Local\\` named mutex, serializing transactions across separate normal/elevated app instances with abandon recovery and bounded wait. The History view asynchronously caches the newest 2,000 rows, filters that cache in memory, applies column-aware sorting (date, integer, or case-insensitive text), and pages a native virtual list through the complete result in 100-row windows. Manual and Live reloads preserve the current page when possible. CSV export includes the complete filtered result. A confirmed Clear action atomically restores a header-only file and resets in-memory deduplication state.

A persisted thread-safe recording flag gates every `SaveSnapshot` call made by Apps, Network, or live History. With recording disabled, existing rows remain readable/exportable, filters/paging continue to work, and live History performs only a bounded store reload. Corrupt/missing settings default to recording enabled for backward compatibility.

The desktop UI can monitor its active Apps, Processes, Network, History, or Memory page every 1, 2, 5, or 15 seconds. On History, each live tick samples native connection tables, records new or state-changed observations, then reloads the retained view. Heavy snapshot work runs away from the UI thread behind one asynchronous gate in addition to per-view reentrancy guards. This prevents cross-page collectors from overlapping; queued and completed work is discarded when its originating page is no longer active. Automatic failures are reported inline with a global **Live error** state rather than modal dialogs, while explicit user refreshes retain modal error feedback.

The Memory page uses `GetPerformanceInfo` for system-wide physical, cache, and committed-memory counters. Its page-based values are converted using the native page size. A stateful `GetSystemTimes` collector computes System CPU from idle, kernel, and user deltas; kernel time includes idle time, so busy percentage is `(kernel + user - idle) / (kernel + user)`. Both collectors refresh manually or through the same Live monitoring intervals. Each refresh appends valid CPU and RAM-load percentages to responsive double-buffered controls capped at 60 samples; no chart-specific timer or background work exists.

System-memory list commands require `SeProfileSingleProcessPrivilege`, not merely membership in the Administrators group. Startup attempts to enable that privilege on the process token and stores the result; the two corresponding controls use this capability result rather than elevation alone. Firewall controls continue to use elevation because they execute through the Windows firewall command surface.

The desktop starts unelevated. Firewall mutation launches the same console-free executable as a narrow `runas` helper with an exact block/unblock verb and path, then reports its exit code to the still-running main window. System-level memory actions continue to require a full elevated session because their token privilege and controls are session-wide.

Apps and Network firewall controls share one mutation gate and one action-state calculation. Eligibility combines selected executable path, current refresh, active mutation, and rule state where known. Commands run off the UI thread or through the waited just-in-time elevated helper, update the shared path-keyed rule cache, and restore both pages in `finally`.

Non-destructive UI preferences are stored as atomic JSON under `%LOCALAPPDATA%\BetterTaskManager`. The app restores a screen-clamped window size, maximized state, refresh interval, and clamped widths for fixed/virtual data columns. The last non-minimized state preserves maximization when closing from the taskbar. Live enabled state is excluded so launching the app never begins background sampling unexpectedly.

Unexpected exception reports use a path-derived named mutex across app instances. Before append, the writer rotates `crash.log` to `crash.previous.log` when the new entry would exceed 1 MiB; oversized single entries are bounded and marked. Reports include version/runtime/OS/bitness/DPI context, while logging failures remain non-fatal.

Top-level navigation and dense command surfaces use autosized wrapping flow layouts. Each page has its own dark context menu that forwards to the same Button actions as its toolbar; grid right-click first selects the target row, and text inputs retain their normal editing menu. The Apps master/detail split uses percentage sizing, with cards and actions wrapping independently; data grids retain explicit column widths and horizontal scrolling. UI smoke tests shrink the form to its minimum size and assert that visible command controls remain inside their containers.

WinForms bootstraps through generated `ApplicationConfiguration.Initialize()` with `ApplicationHighDpiMode=PerMonitorV2`, then requests native and framework dark modes before constructing forms. Runtime smoke coverage asserts the configured DPI mode so packaged builds cannot silently fall back to implicit system-aware scaling.

Localization selects German automatically from `CurrentUICulture` with English fallback and optional command-line override. Static control/header/menu/tooltip text is translated once after construction; non-input control text changes are localized as dynamic statuses update, while grid formatting localizes display states without mutating cached/export models. Message boxes pass through the same translator, and English/German UI smoke modes verify both paths.

The Windows CI workflow installs the .NET 11 preview channel, restores/builds Release, launches self-test plus watchdog-backed UI smoke and soak modes as waited processes, publishes the single-file win-x64 profile, and uploads the complete portable folder. The soak mode performs three rounds across all five page refresh paths, bounds individual refresh duration, verifies the UI message pump recovers, and checks that gates and controls return to idle. The publish script derives its version from the project and performs guarded exact-child cleanup before staging the executable, README, v1.1 preview release notes, changelog, security/privacy guide, license, and SHA-256 manifest. CI has read-only repository permissions and no destructive or privileged test steps.

The main form uses KeyPreview and maps global shortcuts to commands before routing them through shared navigation or the active page's existing refresh/filter/export paths. Static mapping tests cover view, paging, filter, export, refresh, and Live commands; UI smoke coverage verifies real navigation, both paging directions, focus, and clear behavior without invoking file dialogs.

The Apps view groups rows by executable path and sums normalized CPU, private-byte, and working-set values across all PIDs in that group. CPU requires two samples of the same process instance; availability and process start time travel with each row so first samples and PID reuse cannot masquerade as measured idle. Search, typed sorting, and selection operate on the grouped in-memory snapshot and remain active when Live monitoring replaces it. The Processes view remains per-PID and reports sampled visible-row CPU coverage. Both views expose their snapshot time and Apps exposes its contributing process count so the scopes are directly comparable. A working set contains private and shared pages, so summing per-PID working sets can count a shared page more than once.

Apps, Process, and Network CSV exports consume their filtered/sorted cached models rather than scraping localized grid cells. Their invariant schemas include snapshot provenance, CPU availability, full memory/endpoint data, identity, and path; unavailable CPU remains empty and all model fields receive spreadsheet-formula protection. History exports the complete filtered result through the same asynchronous save workflow.

## Planned Collection Model

Portmaster-style per-app bandwidth and rule decisions need a different architecture:

```text
Desktop UI
  -> local API
  -> background Windows service
  -> ETW/WFP collector
  -> local event database
  -> firewall/rule engine
```

That split keeps the UI responsive and allows network observation to continue even when the desktop window is closed.

