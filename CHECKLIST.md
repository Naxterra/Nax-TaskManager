# Must never happen

This list names what Nax-TaskManager must never do. Every release report marks each item as verified by a named test or as not verified.

1. The app never ends, suspends or changes the priority of a process unless the user chose that action for that process.
2. Removing a block, a rule or a helper program from a rule never leaves a Windows Filtering Platform filter or firewall rule behind, and a block never affects an app the user did not select or a helper program the user did not check.
3. Uninstalling never leaves the history service, its data folder permissions, the "replace Windows Task Manager" redirect, startup entries or filters created by the app behind, except where the user chose to keep data.
4. A crash or power loss never leaves the history database, the settings file or the blocked-apps list half-written or unreadable.
5. The user interface never writes to the history database, and the history service never deletes history younger than the configured retention.
6. The app and the history service never send data about processes, connections or the PC to any server, except reverse DNS lookups of remote addresses, which go to the DNS server Windows uses.
7. Elevated helper modes never act on paths or arguments that did not come from the app's own installer or from an action the user confirmed.
8. Kernel trace sessions started by the app or the service never keep running after the program that started them exits, and their buffers never grow beyond the configured size.
9. Monitoring never freezes the user interface, and the app's own CPU and memory use stay small enough not to distort the numbers it shows.
10. A number the app shows never silently disagrees with the Windows Task Manager or the system counters; where the sources differ, the page says why.
11. The kernel memory warning never fires while both kernel pools are below a tenth of the RAM (at least 4 GB), and when it fires it names the pool that is actually too large.
