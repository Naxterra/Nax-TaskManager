# Security and Privacy

Nax-TaskManager is a Windows administration tool. It starts unelevated (`asInvoker`) and shows **Limited mode** until the user chooses **Restart as administrator**.

## Privileged and destructive actions

- **End task / End process tree**: the process's creation time is checked before termination, so a PID reused by another process is never killed. Critical system processes and tree kills ask for confirmation.
- **Network block/allow**: persistent Windows Filtering Platform filters (outbound, per executable, IPv4 and IPv6) under this app's own WFP provider and sublayer, so they are enforced even when a third-party firewall manages Windows Firewall. Without elevation each change starts a short-lived elevated copy of the app with one UAC prompt; it changes the filters and exits before any UI starts. Windows Firewall rules from earlier versions (`Nax-TaskManager Block <hash>`, `BetterTaskManager Block <hash>`) are removed only after the filter change succeeded. A real uninstall removes every filter, the provider and sublayer, and any such older rule, so no program stays blocked.
- **Network rules**: per-app rules (block the Internet, the LAN, this PC or inbound connections; allow or block lists by address, range, scope, protocol and port) are persistent Windows Filtering Platform filters under the same provider and sublayer, at the connect layers for outgoing and the receive/accept layers for incoming connections. Within one app the first matching rule decides; the whole-app block always wins. Saving replaces the app's filters in one transaction (the old set stays if anything fails), writes `app-rules.json` to the protected data folder, and closes open IPv4 TCP connections the new rules block. A real uninstall removes the filters and the file.
- **Free up memory**: trimming app working sets works for the user's own processes; clearing the standby cache and emptying all working sets need administrator rights and the "Profile single process" privilege. Every action asks for confirmation.
- **History service**: installing or removing it needs one UAC prompt. The service runs as LocalSystem from `%ProgramFiles%\Nax-TaskManager\HistoryService` (never from a user-writable folder).
- **Replace Windows Task Manager**: an Image File Execution Options `Debugger` value for `taskmgr.exe`, set and removed only through UAC. The app remembers the choice and warns if something else removed it.
- **Uninstall**: a real uninstall (silent or not) runs one elevated step before any file is removed. It restores Windows Task Manager if the redirect points at this installation, removes every network block and removes the History service. If Task Manager cannot be restored the uninstall stops (silent) or asks (interactive), so Task Manager never points at a deleted program. Upgrades pass `/NAXUPGRADE` to the old uninstaller and keep all of this.
- **Standby limit**: when the user sets a limit in Settings, the History service (LocalSystem) checks the size of the standby list every 10 seconds and empties it with the "Profile single process" privilege when it is larger. Nothing is read or changed on disk. The limit is a file in the protected data folder, written only through UAC; without the file the feature is off.
- **Memory rules**: per-app memory limits and memory priorities are stored in `memory-rules.json` in the protected data folder, written only through UAC (one prompt saves an app's block, network rules and memory rule together). The History service (LocalSystem) applies them on its 2-second snapshots: it sets the memory priority of the app's processes, and when their combined private working set is above the limit it empties the working sets of the largest ones (`EmptyWorkingSet`), which moves pages to the standby list and never ends a process. Protected processes refuse this; each refusal is logged once. Removing a rule restores the normal memory priority.
- **Blocked log**: the History service subscribes to the Windows Filtering Platform's classify-drop events (read only; it changes no filter). It keeps the drops caused by this app's filters, and outgoing drops of programs by other filters, and stores app path, direction, protocol, remote address and host, ports, reason and count in `history.db`.
- **Windows memory manager switches**: memory compression, page combining, application launch prefetching and application prelaunch are changed through Windows' own `PS_MMAgent` WMI class (the class behind `Enable-MMAgent` and `Disable-MMAgent`), and SysMain through its service configuration. Every change runs in a short-lived elevated copy of the app after a UAC prompt. The History service reads the state every 10 minutes and stores it in `memory-manager.json` so windows without administrator rights can show it.
- **Removing written files from RAM**: the History service opens big files (256 MB or more) that were just written once without buffering, which makes Windows drop them from its file cache. Nothing is read or changed. Switch it off in Settings (stored as a marker file in the protected data folder).

## Data the service exposes

- `C:\ProgramData\NaxTaskManager` gets an explicit owner (Administrators) and ACL: SYSTEM and Administrators full control, Users read-only, no inheritance from ProgramData (which would let users create files there). A folder that already exists but is a junction or symbolic link, or is owned by anyone other than SYSTEM, Administrators or TrustedInstaller (someone created it first), is not used: a link is removed (never its target) and any other folder is renamed to `NaxTaskManager.untrusted-<time>` before a fresh folder is created.
- The live feed pipe `\\.\pipe\NaxTaskManager.NetworkFeed` is one-way. SYSTEM and Administrators own it; authenticated users may only read, so they cannot create rogue instances while the service runs. The app accepts the feed only when the pipe's server process is the service's own process.
- Both contain which apps connected to which hosts and how much data they moved; the database also contains which connections were blocked. On a shared PC every local user can read this; turn the History service off if that matters.

## Network access

The app and service make no network requests of their own except DNS reverse (PTR) lookups for remote addresses whose host name is not already known. There is no telemetry and no update check. Host names shown for connections come from the connecting app's own DNS lookups where known; a name another app looked up is marked as such, and an address answered for several names (a CDN) gets no name.

## Releases

Installer and executables are not Authenticode-signed, so Windows may show an unknown-publisher warning. Each release includes a SHA-256 checksum file.

## Reporting issues

Do not include private IP addresses, usernames, full paths, process lists, crash logs or connection history in public issues unless they have been reviewed and sanitised. For sensitive reports use a private GitHub security advisory.
