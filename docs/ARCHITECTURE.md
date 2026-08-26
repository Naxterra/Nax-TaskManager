# Architecture

## Current Desktop Shape

Better Task Manager 1.1.0-preview.19 remains a single Windows desktop executable.

```text
WinForms UI
  -> process collector
  -> network connection snapshot collector
  -> Windows Firewall commands
  -> Windows memory maintenance APIs
```

## Current Collection Model

The app collects live process data directly from Windows process APIs. Process collection produces a complete snapshot; search, same-app PID scoping, and column sorting operate on that in-memory snapshot without triggering new protected-process lookups. IPv4 and IPv6 TCP/UDP endpoints and their owning process IDs come from the native Windows IP Helper tables (`GetExtendedTcpTable` and `GetExtendedUdpTable`).

Apps, Processes, Network, and History share a synchronized per-PID identity cache. Path/user resolution state is explicit, so both successful results and access-denied empty results are reused instead of retried on each Live tick. Process start time guards against PID reuse, and full process snapshots prune exited PIDs. Apps refresh also passes its same-snapshot process rows directly into network attribution. Network search and typed sorting operate on the complete in-memory snapshot; they do not invoke the native collector and remain applied when Live monitoring replaces the snapshot.

New and changed connection observations are written locally. Unchanged snapshots are suppressed, the minimum sampling interval is one second, and entries are pruned after 30 days. The History view asynchronously caches the newest 2,000 rows, filters that cache in memory, applies column-aware sorting (date, integer, or case-insensitive text), and pages a native virtual list through the complete result in 100-row windows. Manual and Live reloads preserve the current page when possible. CSV export includes the complete filtered result. A confirmed Clear action uses the same store lock as sampling, atomically restores a header-only file, and resets in-memory deduplication state.

The desktop UI can monitor its active Apps, Processes, Network, History, or Memory page every 1, 2, 5, or 15 seconds. On History, each live tick samples native connection tables, records new or state-changed observations, then reloads the retained view. Heavy snapshot work runs away from the UI thread behind one asynchronous gate in addition to per-view reentrancy guards. This prevents cross-page collectors from overlapping; queued and completed work is discarded when its originating page is no longer active. Automatic failures are reported inline with a global **Live error** state rather than modal dialogs, while explicit user refreshes retain modal error feedback.

The Memory page uses `GetPerformanceInfo` for system-wide physical, cache, and committed-memory counters. Its page-based values are converted using the native page size and refreshed manually or through the same Live monitoring intervals.

The desktop starts unelevated. Privilege state and elevation are global navigation concerns; firewall mutation and system-level memory actions remain unavailable until the executable is restarted with `runas`.

The Apps view groups rows by executable path and sums normalized CPU, private-byte, and working-set values across all PIDs in that group. CPU requires two samples of the same process instance; availability and process start time travel with each row so first samples and PID reuse cannot masquerade as measured idle. Search, typed sorting, and selection operate on the grouped in-memory snapshot and remain active when Live monitoring replaces it. The Processes view remains per-PID and reports sampled visible-row CPU coverage. Both views expose their snapshot time and Apps exposes its contributing process count so the scopes are directly comparable. A working set contains private and shared pages, so summing per-PID working sets can count a shared page more than once.

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

