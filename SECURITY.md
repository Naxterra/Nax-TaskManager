# Security and Privacy

Nax-TaskManager is a Windows administration tool. It starts unelevated (`asInvoker`) and shows **Limited mode** until the user chooses **Restart as administrator**.

## Privileged and destructive actions

- **End task / End process tree** — the process's creation time is checked before termination, so a PID reused by another process is never killed. Critical system processes and tree kills ask for confirmation.
- **Firewall block/allow** — Windows Firewall outbound rules named `Nax-TaskManager Block <hash>` (older `BetterTaskManager Block <hash>` rules are recognised). Without elevation each change starts a short-lived elevated copy of the app with one UAC prompt; it applies one `netsh` rule and exits before any UI starts.
- **Free up memory** — trimming app working sets works for the user's own processes; clearing the standby cache and emptying all working sets need administrator rights and the "Profile single process" privilege. Every action asks for confirmation.
- **History service** — installing or removing it needs one UAC prompt. The service runs as LocalSystem from `%ProgramFiles%\Nax-TaskManager\HistoryService` (never from a user-writable folder). Uninstalling the app interactively removes the service.

## Data the service exposes

- `C:\ProgramData\NaxTaskManager` gets an explicit ACL: SYSTEM and Administrators full control, Users read-only, no inheritance from ProgramData (which would let users create files there).
- The live feed pipe `\\.\pipe\NaxTaskManager.NetworkFeed` is one-way. SYSTEM and Administrators own it; authenticated users may only read, so they cannot create rogue instances while the service runs. The app accepts the feed only when the pipe's server process is the service's own process.
- Both contain which apps connected to which hosts and how much data they moved. On a shared PC every local user can read this; turn the History service off if that matters.

## Network access

The app and service make no network requests of their own except DNS reverse (PTR) lookups for remote addresses whose host name is not already known. There is no telemetry and no update check.

## Releases

Installer and executables are not Authenticode-signed, so Windows may show an unknown-publisher warning. Each release includes a SHA-256 checksum file.

## Reporting issues

Do not include private IP addresses, usernames, full paths, process lists, crash logs or connection history in public issues unless they have been reviewed and sanitised. For sensitive reports use a private GitHub security advisory.
