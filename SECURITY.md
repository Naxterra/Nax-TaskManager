# Security and Privacy

Better Task Manager is a Windows administration utility. It starts unelevated (`asInvoker`) and displays **Standard mode** until the user explicitly chooses **Restart as Admin**. Privileged controls remain disabled when elevation is unavailable.

## Privileged and Destructive Actions

The app asks for confirmation before high-impact actions and may:

- Force-kill a selected process and its child processes.
- Trim selected or accessible process working sets.
- Request Windows standby-cache or system working-set cleanup.
- Add or remove a Better Task Manager outbound Windows Firewall rule for a selected executable path.
- Permanently clear saved connection history.

Force Kill and selected Trim validate PID plus process start time before acting. Better Task Manager refuses to force-kill itself, and bulk trim excludes its own process. Memory and firewall mutations use non-overlapping busy gates.

The standby-list and system-working-set commands additionally require `SeProfileSingleProcessPrivilege`. Better Task Manager attempts to enable and verify it on the process token; if Windows policy does not assign it, those two controls remain disabled even in an elevated administrator session and the UI reports the distinction.

Firewall status describes only rules created by Better Task Manager. **No BTM Block** does not claim that another Windows Firewall policy allows traffic. Created rules apply outbound on all profiles for the selected executable path.

## Local Data

The app does not require an online account or send telemetry. It stores the following under `%LOCALAPPDATA%\BetterTaskManager`:

- `network-history.csv` — new or changed observed connections, retained for up to 30 days.
- `settings.json` — non-destructive window, interval, and column-width preferences.
- `crash.log` and `crash.previous.log` — contextual exception reports, bounded to 1 MiB each with one-file rotation.

Connection history and CSV exports can contain private IP addresses, usernames, executable paths, process names, and remote endpoints. **Clear History** removes the saved history file contents, but it does not delete exports the user saved elsewhere. Live monitoring can record new observations after clearing when recording remains enabled.

The History page's persisted **Record history** checkbox can stop future disk writes without deleting existing history. When disabled, Apps and Network refreshes do not save observations and live History reloads existing data only.

CSV exports prefix spreadsheet-formula trigger characters, but exported data should still be reviewed before sharing publicly.

## Verification and Distribution

Portable packages include `SHA256SUMS.txt` for the executable. This verifies file integrity against the packaged manifest; it is not a code-signing certificate or proof of publisher identity.

The built-in `--self-test` and `--ui-smoke-test` modes are non-destructive: they do not kill processes, clear memory, or modify firewall rules. The UI test uses isolated temporary settings and history files.

## Reporting Issues

Do not include private IP addresses, usernames, full paths, process lists, exported CSV files, crash logs, or connection history in public issues unless they have been reviewed and sanitized.

For sensitive reports, use a private GitHub security advisory or contact the maintainer privately once a public maintainer channel is available.

