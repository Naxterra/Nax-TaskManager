# Changelog

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

