# Security and Privacy

Nax-TaskManager is a Windows administration tool. It starts unelevated (`asInvoker`) and shows **Limited mode** until the user chooses **Restart as administrator**.

## Privileged and destructive actions

- **End task / End process tree** — the process's creation time is checked before termination, so a PID reused by another process is never killed. Critical system processes and tree kills ask for confirmation.
- **Network block/allow**: persistent Windows Filtering Platform filters (outbound, per executable, IPv4 and IPv6) under this app's own WFP provider and sublayer, so they are enforced even when a third-party firewall manages Windows Firewall. Without elevation each change starts a short-lived elevated copy of the app with one UAC prompt; it changes the filters and exits before any UI starts. Windows Firewall rules from earlier versions (`Nax-TaskManager Block <hash>`, `BetterTaskManager Block <hash>`) are removed only after the filter change succeeded. A real uninstall removes every filter, the provider and sublayer, and any such older rule, so no program stays blocked.
- **Free up memory** — trimming app working sets works for the user's own processes; clearing the standby cache and emptying all working sets need administrator rights and the "Profile single process" privilege. Every action asks for confirmation.
- **History service** — installing or removing it needs one UAC prompt. The service runs as LocalSystem from `%ProgramFiles%\Nax-TaskManager\HistoryService` (never from a user-writable folder).
- **Replace Windows Task Manager**: an Image File Execution Options `Debugger` value for `taskmgr.exe`, set and removed only through UAC. The app remembers the choice and warns if something else removed it.
- **Uninstall**: a real uninstall (silent or not) runs one elevated step before any file is removed. It restores Windows Task Manager if the redirect points at this installation, removes every network block and removes the History service. If Task Manager cannot be restored the uninstall stops (silent) or asks (interactive), so Task Manager never points at a deleted program. Upgrades pass `/NAXUPGRADE` to the old uninstaller and keep all of this.
- **Removing written files from RAM**: the History service opens big files (256 MB or more) that were just written once without buffering, which makes Windows drop them from its file cache. Nothing is read or changed. Switch it off in Settings (stored as a marker file in the protected data folder).

## Data the service exposes

- `C:\ProgramData\NaxTaskManager` gets an explicit owner (Administrators) and ACL: SYSTEM and Administrators full control, Users read-only, no inheritance from ProgramData (which would let users create files there). A folder that already exists but is a junction or symbolic link, or is owned by anyone other than SYSTEM, Administrators or TrustedInstaller (someone created it first), is not used: a link is removed (never its target) and any other folder is renamed to `NaxTaskManager.untrusted-<time>` before a fresh folder is created.
- The live feed pipe `\\.\pipe\NaxTaskManager.NetworkFeed` is one-way. SYSTEM and Administrators own it; authenticated users may only read, so they cannot create rogue instances while the service runs. The app accepts the feed only when the pipe's server process is the service's own process.
- Both contain which apps connected to which hosts and how much data they moved. On a shared PC every local user can read this; turn the History service off if that matters.

## Network access

The app and service make no network requests of their own except DNS reverse (PTR) lookups for remote addresses whose host name is not already known. There is no telemetry and no update check. Host names shown for connections come from the connecting app's own DNS lookups where known; a name another app looked up is marked as such, and an address answered for several names (a CDN) gets no name.

## Releases

Installer and executables are not Authenticode-signed, so Windows may show an unknown-publisher warning. Each release includes a SHA-256 checksum file.

## Reporting issues

Do not include private IP addresses, usernames, full paths, process lists, crash logs or connection history in public issues unless they have been reviewed and sanitised. For sensitive reports use a private GitHub security advisory.
