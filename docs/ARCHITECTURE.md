# Architecture

## Current Desktop Shape

Better Task Manager 1.1.0-preview.11 remains a single Windows desktop executable.

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

New and changed connection observations are written locally. Unchanged snapshots are suppressed, the minimum sampling interval is one second, and entries are pruned after 30 days. The History view asynchronously caches the newest 2,000 rows, filters and sorts that cache in memory, and uses a native virtual list to paint only the first 100 rows of the current result. CSV export includes the complete filtered result.

The desktop UI can monitor its active Apps, Processes, Network, History, or Memory page every 1, 2, 5, or 15 seconds. On History, each live tick samples native connection tables, records new or state-changed observations, then reloads the retained view. Collection and history I/O run away from the UI thread, with per-view reentrancy guards preventing overlapping refreshes.

The Memory page uses `GetPerformanceInfo` for system-wide physical, cache, and committed-memory counters. Its page-based values are converted using the native page size and refreshed manually or through the same Live monitoring intervals.

The desktop starts unelevated. Privilege state and elevation are global navigation concerns; firewall mutation and system-level memory actions remain unavailable until the executable is restarted with `runas`.

The Apps view groups rows by executable path and sums private-byte and working-set values across all PIDs in that group. The Processes view remains per-PID. Both views expose their snapshot time and Apps exposes its contributing process count so the scopes are directly comparable. A working set contains private and shared pages, so summing per-PID working sets can count a shared page more than once.

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

