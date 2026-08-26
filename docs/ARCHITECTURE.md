# Architecture

## Current Desktop Shape

Better Task Manager 1.1.0-preview.2 remains a single Windows desktop executable.

```text
WinForms UI
  -> process collector
  -> network connection snapshot collector
  -> Windows Firewall commands
  -> Windows memory maintenance APIs
```

## Current Collection Model

The app collects live process data directly from Windows process APIs. IPv4 and IPv6 TCP/UDP endpoints and their owning process IDs come from the native Windows IP Helper tables (`GetExtendedTcpTable` and `GetExtendedUdpTable`).

New and changed connection observations are written locally. Unchanged snapshots are suppressed, entries are pruned after 30 days, and the History view asynchronously renders at most the newest 2,000 rows.

The desktop UI can monitor its active Apps, Processes, or Network page every 1, 2, 5, or 15 seconds. Collection and history I/O run away from the UI thread, with per-view reentrancy guards preventing overlapping refreshes.

The Apps view groups rows by executable path and sums private/commit and working-set values across all PIDs in that group. The Processes view remains per-PID. Both views expose their snapshot time and Apps exposes its contributing process count so the scopes are directly comparable.

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

