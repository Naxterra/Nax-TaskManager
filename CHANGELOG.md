# Changelog

## 2.0.0-alpha.42 - 2026-10-28

The remaining fixes from the full review, and the move that makes an install for all users work with the History service.

- **The History service has its own folder.** It now runs from `Program Files\Nax-TaskManager History` instead of a folder inside the app's own install folder. An install for all users and the service therefore never share files, so installing the service can no longer copy files onto themselves and removing it can no longer delete the app's files. Setup for all users stops a service that still runs from the old folder, moves it after the files are copied, and starts it again if setup ends early. An install for all users is what "Replace Windows Task Manager" needs.
- **Shared helper programs keep their block.** One helper, such as the WebView2 runtime, can serve several apps. Unblocking one app now leaves the helper blocked while another blocked app lists it, or while the helper was blocked on its own. A program with network rules of its own, or one that already follows another app's rules, can no longer be made a helper, because that would replace its rules. The Rules page shows why such a program cannot be checked.
- **Rules follow an app to its new version.** When an update moves a helper into a new version folder, the Rules page checks the new file in place of the missing one, and saving moves the rules over. When the app itself moved (many apps install every update into a new folder), the Rules page offers to move its block, network rules and memory rule to the new file in one step.
- **Uninstalling cleans up more.** The memory manager switches go back to how they were before the app first changed them, including SysMain's exact start type. The memory rules, the standby limit and the cache trim setting are removed. An interactive uninstall asks whether the recorded history, the logs and the rules should be deleted as well, and a silent one keeps them. When a memory setting cannot be put back, a silent uninstall stops and an interactive one asks, and the record of the original settings is kept for a later attempt.
- **Trace sessions no longer outlive the app.** When an app window running as administrator is killed, its kernel traces kept their memory until Windows restarted. The History service now stops them once no app window is open, and every new window stops them at start.
- **History survives a clock set far ahead.** A clock that jumps ahead while the service runs, or more than three days while it is off, no longer counts toward the 30-day retention at once, so a wrong date cannot delete the whole history. The held-back time is released at four times real time, so a clock that was really behind and gets corrected only delays pruning for a while.
- Smaller fixes: the blocked log names the right rule right after rules change, the host name and disk caches stay bounded on machines that reach very many addresses or files, finding the driver behind a pool tag reads large drivers in pieces instead of loading them whole, and the cache trimmer opens a file that is written in many small steps once per pass instead of at every write.
## 2.0.0-alpha.41 - 2026-10-27

Safety and robustness fixes from a full review of the project.

- **Replace Windows Task Manager needs an install for all users.** Windows starts the replacement for every account, including elevated starts. A copy installed for one user lives in that user's profile, where any program the user runs could replace it, and an administrator starting Task Manager would then run that file with full rights. The setting is now only available when the app is in a folder that only administrators can change, such as Program Files. A replacement that is already on from an earlier version shows a warning and can still be turned off.
- **Damaged rule files are no longer overwritten.** When app-rules.json cannot be read, saving is refused instead of treating the file as empty, which used to drop every other app's rules while their filters kept blocking. The Rules page then offers "Reset rules", which removes every rule filter and the file together, so both agree again. A damaged memory-rules.json is kept as a copy and the rules start over.
- **Files are written crash-safe.** Rules, memory rules, the blocked-apps list, the standby limit, the memory manager state and the settings are written to a temporary file that is flushed to disk before it replaces the old one, so a power loss leaves the old or the new file, never a half-written one.
- **The history database survives a power loss.** It is written with full synchronisation, and a database that is damaged anyway is kept as a copy while the service starts a new one, instead of failing at every start.
- **Helper modes accept only what the app itself sends.** The administrator helper replaces Task Manager only with its own program file, blocks only programs given with a full path, and writes result files only as new "nax…" files in the user's temporary folder, never through a link and never over an existing file.
- **A silent uninstall stops when the cleanup fails**, for example when administrator approval is declined, instead of removing the files and leaving the history service, blocks and rules behind. An interactive uninstall shows what failed.
- **Rules reject shortened IPv4 addresses.** A target such as "10.1/16" or a bare number used to be read as a different network (10.0.0.0/16, 0.0.13.61). IPv4 targets now need all four parts.
- **Memory rules never touch a process that only reuses an old process ID**, because the creation time is checked first.
- Stopping the history service while it empties the standby cache no longer crashes it, and the kernel memory warning never names a tag that holds nothing in the reported pool.
## 2.0.0-alpha.40 - 2026-10-26

- **Kernel memory is visible, and a leaking driver is named.** Memory that drivers and Windows allocate in the kernel pools belongs to no process, so no app list can show it. When a driver leaks, the RAM fills up while every app looks small. The overview's memory card now shows "Kernel and drivers (pools)" as its own line, and the Memory page has a new "Kernel memory by driver" table with the largest pool tags, their non-paged and paged size and the driver files that contain each tag.
- When the non-paged or the paged pool holds a tenth of the RAM or more (at least 4 GB), both places show a warning that names the pool, the tag holding most of it and its driver, for example "NtFC (ntfs.sys)", and explains that only a restart releases this memory.
- Finding a tag's driver now also searches the kernel modules that are currently loaded, not only System32\drivers. Tags of the Windows kernel itself (for example EtwB and MmSt) and of drivers installed in the driver store (for example NVRM of the NVIDIA driver) now show their file instead of "no driver found". The startup memory report of the history service uses the same search.
- A console test project (tests/BetterTaskManager.Core.Tests) checks when the warning fires and which tag it names.
- Tested on this PC: the table listed NtFC under ntfs.sys, EtwB and MmSt under ntoskrnl.exe and NVRM under nvlddmkm.sys, without administrator rights. The warning itself was not shown on screen because the pools were normal during the test; its rule is covered by the test project.

## 2.0.0-alpha.39 - 2026-10-22

- **Rules cover an app's helper programs.** Apps that draw their window with a browser engine connect from the engine's program file, which a firewall rule for the app does not cover. An app's rules now list these helper programs under "Helper programs". A helper only this app uses (for example Steam's web helper) is checked for new rules; a shared one (the Windows WebView2 runtime that WhatsApp, the Widgets, Teams and Nax-Notepad use) only when you check it, with a note which other apps a rule would also affect. A checked helper gets the app's block, switches and rules as its own filters; unchecking it gives it its connections back, and rules a helper has of its own are never touched. The Rules list shows helpers as part of their app ("+1 helper programs"), "Recently blocked" includes them, and the app picker no longer offers them as apps.
- The usage line in the rule editor counts the helpers, like the Processes page.
- When saving rules fails, the message now says what failed instead of only an exit code.
- Tested elevated with two program copies as app and helper: "Block Internet" and the whole-app block reached the helper, removing the helper restored its access while the app stayed blocked, and removing everything left no filter behind. This also verified saving through the one-prompt helper for the first time.

## 2.0.0-alpha.38 - 2026-10-22

- **Steam is an app again.** Steam draws its window with its own browser engine, steamwebhelper.exe, so steam.exe itself has no window and was listed under background processes while "Steam Client WebHelper" showed as a separate app. The web helpers now count toward the program that started them, like WebView2 since alpha.37: Steam shows as one app with all its processes, memory and connections. The same applies to the Epic Games Launcher's web helper, Unreal's CEF helper and Qt WebEngine.

## 2.0.0-alpha.37 - 2026-10-22

Alignment fixes, and apps built on WebView2 now show their own traffic.

- **Apps that use WebView2 or CefSharp.** WhatsApp, and other apps that show their window through Microsoft Edge WebView2 or CefSharp, start helper processes that make the connections and use most of the memory. These helpers now count toward the app that started them, as in Task Manager: WhatsApp shows its 8 processes, its memory and its connection on the Processes, Network, Overview and Performance pages, and the History service records their traffic for the app. A helper started by a Windows service stays its own entry. Network rules still apply per program file, so a rule for WhatsApp.Root.exe does not cover the WebView2 helper.
- **Store app icons.** Apps from the Microsoft Store show their own logo from the app package instead of a generic window icon.
- **Meters.** The bars in table cells sit under the number like an underline and grow from the right, so a full bar no longer runs through the digits, and tiny values no longer leave a dot at the edge of the cell.
- **Status icons and sort arrows.** The efficiency mode leaf, the suspended icon and the Blocked badge follow the app name instead of sitting at the far edge of the Name column. Every table header shows its sort arrow next to its label. The Path header on the Disk page lines up with the others.

## 2.0.0-alpha.36 - 2026-10-22

The blocked log, and a more colorful, clearer interface.

- **Blocked connections.** The History service now records every connection a firewall filter dropped, with the reason: the whole-app block, one of the switches (Internet, LAN, this PC, incoming) or the exact rule. Repeats of the same app, remote end and reason within 10 minutes are counted in one entry. Remote host names come from the app's DNS lookups. Drops by other firewalls are recorded for outgoing connections of programs and can be shown with "Other firewalls too". Entries are kept for 30 days like the rest of the history.
- **Where to see it.** The Rules page has a second section, "Blocked connections", with the time range, a search and the list, newest first. Each row shows the app, the remote end with port and protocol, the reason as a colored chip, how often and when. Clicking a row opens that app's rules. Every app's editor shows what was blocked for it in the last 24 hours under "Recently blocked", right below its network rules. The History page has a "Blocked connections" button.
- **Colors.** Every area has its own color: CPU teal, memory purple, network cyan, disk green, history blue, startup apps amber and rules rose. The navigation icons, a badge next to every page title, the badges on card titles and a soft tint on the stat tiles use it, in the dark and the light theme.
- **Clearer at a glance.** The Rules page lists what each app's rules do as colored chips (red for a block, amber for switches, teal for network rules, purple for memory). Empty pages and lists explain what will appear there and offer the next step, for example "Add app".
- Tested: the service recorded the drops of two curl copies (one fully blocked, one with "Block Internet"), each with the right reason and host name, and nothing for the connection the rule allowed.

## 2.0.0-alpha.35 - 2026-10-22

Memory tuning, a Rules page, Performance and Memory in one page, and clickable Overview tiles.

- **Rules page (Ctrl+7).** Every rule now has one place. The left side lists each app with a network block, network rules or a memory rule, with a short summary. The right side is the selected app's editor, part of the page instead of a dialog, so it follows the window's size, scrolls on its own and keeps Save and Discard visible at the bottom. "Add app" picks a running app or takes the path of a program file. "Rules…" on the Processes and Network pages and "All rules" open this page. Saving applies the block, the network rules and the memory rule of an app together, with one UAC prompt when the app runs without administrator rights. Switching to another app with unsaved changes asks first.
- **Clearer rule editor.** The switches are toggle rows with their explanation, the rule rows have column labels and share the width they get, and in a narrow window the comment moves to a second line. "Block all network access" is part of the editor.
- **Memory rules per app, like cgroups.** An app can get a memory limit for the private memory of all its processes together and a memory priority from 1 (very low) to 5 (normal). The History service enforces both every 2 seconds. When an app goes over its limit, the service takes pages out of the largest processes' RAM until the app is below it again (like memory.high on Linux). The app keeps running and reads its data back when it needs it, so a limit that is too small makes it slow. A memory priority makes Windows reuse that app's cached pages first when memory runs short. Removing a rule gives the processes their normal priority back.
- **Performance and Memory combined.** The Performance page (Ctrl+2) has three sections. "CPU" shows the CPU, processes, threads and handles with their history and the apps using the most CPU. "Memory" is the former Memory page with "Free up memory". "Memory tuning" holds the standby limit and the removal of written files from RAM (both moved here from Settings), the list of memory rules, and new switches for Windows' memory manager: memory compression, page combining, application launch prefetching, application prelaunch and the SysMain service. Each switch says what it does. The History service records their state every 10 minutes, so the page can show it without administrator rights.
- **Clickable Overview tiles.** CPU opens the Processes page sorted by CPU, Memory opens the memory section, and Download and Upload open the Network page with all apps, the busiest first.

## 2.0.0-alpha.34 - 2026-10-22

Redesign stage 4 (Overview and Memory pages), plus a standby limit.

- **Overview page.** The app opens on a live overview: CPU, memory, download and upload with their last minute, a resource chart, the memory footprint, the apps using the most memory, and the active network destinations. Clicking an app or a destination opens it on the Processes or Network page. Ctrl+0 opens it.
- **Memory page (Ctrl+7).** Physical RAM as "In use", "Modified" and "Available", the four totals with their history, and a list of where the memory is: in use (programs' own memory, the Windows pools, the file cache, driver code, and loaded program files with DLLs) and cache and free (modified, standby, free), each group with its total. One sentence explains why "Free" is almost always close to zero. Counters that Windows does not deliver show as "Not available" instead of 0.
- **Keep the standby cache small.** Windows cannot switch its standby cache off. A new setting (Settings, Memory) lets the History service check every 10 seconds and empty the standby cache whenever it grows above a limit of 512 MB to 8 GB, so that RAM shows as free. Programs and files used again are then read from disk. Off unless you choose a limit. Tested: with a 1 GB limit the standby cache went from 35.9 GB to 0.7 GB and free memory from 0.09 GB to 36.2 GB.
- **All rules in one place.** "All rules" on the Network page lists every app with a block or its own rules, with the rule lines and buttons to edit or unblock.
- **From a process to its connections and back.** The Processes page's details card and menus open the app's connections; the Network page's open the app's processes. An app without open connections still shows, with an empty list.
- **Rule explanations.** A connection's details say which of your rules or switches allows or blocks it, and where its host name comes from. This explains the configured rules; it is not a record of dropped packets.
- **Navigation and look.** A brand header in the navigation pane, a card with the machine's CPU and memory at the bottom of it, details cards that appear when something is selected and the window is wide enough, and consistent colors in light and dark mode.
- **Fixes.** The Network and History app lists leave room for the scrollbar, so names and numbers are no longer covered, and the Network app list is wider. "All apps" no longer counts VPN tunnel traffic twice. A connection keeps its selection only while it belongs to the same process, so a reused PID cannot inherit it. A selection change during a refresh no longer re-enters the refresh. The memory cleanup report waits for a new measurement instead of comparing old values. A rules refresh that arrives during another one now runs afterwards instead of being skipped. Hidden pages stop updating, and a page can no longer subscribe to updates twice.
- Publishing now copies the compiled pages under Views, and the package check requires them.

## 2.0.0-alpha.33 - 2026-10-22

Redesign stage 3: the Network page as master and detail.

- **Apps on the left.** Every program with open sockets is one row with its icon, "x of y established", its current rates and two small meters for download and upload (relative to the busiest app). "All apps" sits on top. A dropdown sorts the list by name, data or speed.
- **The selected app's connections in the middle.** The table shows only that app's connections (or all of them, with an App column saying whose), with remote host, local address, scope, state with a colored dot, and speed. Clicking Remote host, State or Speed sorts the connections.
- **Traffic chart.** Under the table, the selected app's download and upload over the last minute.
- **Details on the right** for the connection or app selected last, as before. Expanding and collapsing app groups is gone; picking an app on the left replaces it.

## 2.0.0-alpha.32 - 2026-10-22

Redesign stage 2: the details card.

- **Details card on Processes and Network.** The right side of both pages shows the selected row: icon, name, publisher and process count; two mini tiles with the selected app's own CPU and memory (or download and upload) history as sparklines; the facts (PIDs, status, threads, handles, connections, user, path with a copy button; on Network established and listening counts, data totals and the app's network access, or for a connection its remote and local endpoints, protocol, state, scope and PID); and the actions as full-width buttons, with End task in red. A **Details** button in the toolbar hides and shows the card, and the choice is remembered.
- **View switch in the table.** "By app (n)" and "All processes (n)" sit on the table card like tabs, with their counts.
- Tiles adapt their sparkline width to the room they have, so the numbers no longer get cut off next to the card.

## 2.0.0-alpha.31 - 2026-10-22

The first stage of the redesign toward the concept art: the app's own look instead of the Windows 11 Task Manager look.

- **Own palette.** Navy surfaces in dark mode (slate in light mode), a blue accent of its own, and one color per resource used everywhere: CPU teal, memory purple, GPU blue, disk green, download cyan, upload violet. Mica is gone; the window is a flat, calm surface.
- **Tiles with gradient sparklines.** The tiles under the page titles have a colored edge, larger numbers and a filled gradient chart.
- **Bar meters in the cells.** The CPU, memory, GPU, disk, network and connection cells show a colored bar behind the number, proportional to the value (CPU and GPU over 100 %, memory over an eighth of RAM, disk over 10 MB/s, network over 50 Mbit/s, connections over 50). The amber tint is gone; the Disk page's active time and totals use the same bars.
- **Status strip.** A line at the bottom says whether the numbers are live and when they were last updated, and whether they come from the History service, from the app's own measurement, or neither; the version sits at the right.
- **Limited mode as a card in the pane.** The notice no longer pushes the content down; it sits at the bottom of the navigation pane with the "Run as administrator" button.
- **State dots.** Network states and startup entries get a colored dot (green for established or enabled, gray for listening or disabled, amber in between).
- **Ctrl+K** focuses the search box, as Ctrl+F did; the placeholder says so.

## 2.0.0-alpha.30 - 2026-10-22

A pass over the whole interface, after the feedback that it was hard to navigate and looked like a text screen:

- **Navigation with names.** The left pane is open with the page names (Processes, Performance, Network, Disk, History, Startup apps) instead of six unlabeled icons; it folds to icons on narrow windows and with the pane button in the title bar. Pages ease in when you switch.
- **Actions where the rows are.** Every row shows a "…" button when you point at it or select it, with the same actions as the right-click menu (end task, efficiency mode, open location, properties, copy path, block network, network rules, enable or disable startup entries, copy remote host). The main action of each page is an accent button next to the title: End task, Rules, Disable or Enable.
- **Tiles with sparklines.** Processes shows CPU, memory, disk, network and GPU tiles with the last minute as a small chart; Network shows download, upload and connection tiles. The numbers that used to be one muted sentence have a face now.
- **Tables on cards.** Each table sits on a card with a distinct header row, 20-pixel app icons and a little more air; hover and selection states stay.
- **Network rules as a list.** The rules dialog edits rules as rows (allow or block, target, address, protocol, ports, comment, with move-up, move-down and remove) instead of a text box. "Edit as text" shows the same rules as lines for pasting or presets; mistakes are explained and Save stays off until they are fixed.

## 2.0.0-alpha.29 - 2026-10-21

- **Per-app network rules.** Every app can now get its own rules instead of only "block everything": right-click an app on the Processes or Network page (or select it on Network and click **Rules**) and choose what to block. Four switches block the Internet, the local network, connections within this PC, or connections other devices start to the app; below them, an outgoing and an incoming rule list in Portmaster's form (`+ LAN`, `- Internet`, `+ 1.1.1.1 TCP/443`, `- * UDP/53`, with `#` comments) where the first matching line decides and everything else stays allowed. Presets give the common cases (web only, local network only, this PC only), and mistakes are explained line by line before saving. Apps with rules show a "Rules" badge on the Network page.
- **Rules are enforced by Windows itself.** They become persistent Windows Filtering Platform filters under this app's own provider, so they work next to Bitdefender or any other firewall, survive restarts and need no running program. Saving asks once for administrator approval. "Block network" still wins over any rule, and uninstalling removes all rules.
- **Blocks take effect at once.** Blocking an app, or saving rules that block one of its open connections, now closes those connections immediately instead of letting a running download continue. Tested: a running download stops within a second, over IPv4 and IPv6.
- Limits: Windows sends ping requests itself, so ICMP rules do not affect `ping`; a rule can block a program's own ICMP sockets only. Rules apply to the executable path, like the whole-app block.

## 2.0.0-alpha.28 - 2026-10-17

Fixes from an external code review (all eight findings confirmed and fixed):

- **Uninstall cleans up everything outside the app folder.** A real uninstall (silent or not) now runs one elevated step before any file is removed: it restores Windows Task Manager if it opens this app, removes every network block (otherwise blocked programs stayed blocked with nothing left to unblock them) and removes the History service. If Task Manager cannot be restored, a silent uninstall stops and an interactive one asks first, so Task Manager never points at a deleted program. Upgrades tell the old uninstaller to keep all of this.
- **Safer data folder.** `C:\ProgramData\NaxTaskManager` now gets Administrators as owner. A folder someone else created first, or a link to another place, is not used: links are removed (never their target) and other folders are renamed to `NaxTaskManager.untrusted-<time>`.
- **Blocking never weakens protection.** Old Windows Firewall rules are removed only after the new block is in place; unblocking reports an error if an old rule survives; a failed update of the block list file no longer reports an applied block as failed.
- **History keeps every connection.** A connection that ended and restarted on the same address before the next save lost its first part; a failed database write could lose a connection on the retry; a program that reused the PID of an exited one within a second or two could be credited with the other's traffic or DNS names. All fixed, with regression checks in `tests/history-checks.cs`.
- **Switch for removing written files from RAM** in Settings (Memory), on by default.
- **Warning when measuring fails**, instead of silently showing old values.
- `SECURITY.md` describes WFP blocking, uninstall cleanup and the data folder rules.

## 2.0.0-alpha.27 - 2026-10-17

- **Notice when "Replace Windows Task Manager" was removed.** Redirecting Task Manager is something security software and Windows sometimes undo on their own (on 2026-10-16 the setting disappeared at 09:18, half an hour after startup, without anything in Nax-TaskManager or its installer touching it). The app now remembers that you switched the replacement on and, if the setting is gone when it starts, shows a warning with a **Restore** button (one administrator prompt). Closing the warning means you no longer want the replacement. Turn it on once in Settings to start this.

## 2.0.0-alpha.26 - 2026-10-15

- **The Processes page's disk column shows real disk traffic.** It used to count all file and device I/O, including pipes between a program's own processes, so a chat app showed 7.3 MB/s there while the Disk page correctly showed a few KB/s. Whenever real disk bytes are measured (administrator rights, or the History service for a normal window) the column is now called "Disk", shows bytes that reached a disk like Task Manager, uses B/s, KB/s or MB/s as fits, and its header shows the disks' total. Without them it falls back to "Disk / I/O" as before, and the tooltip says which one you see.

## 2.0.0-alpha.25 - 2026-10-15

- **No more "WMI Provider Host" DNS lookups in History.** They came from Nax-TaskManager itself: it reads the Windows DNS cache every 10 seconds through WMI, and Windows answers that by running one cache-only query per cache entry inside WMI Provider Host (about 200 per read, 7,436 history rows on one day). These queries never reach the network. History now ignores every cache-only query, and the History service reads the cache only once at startup and then relies on the live DNS events, which see every real lookup anyway. A window without administrator rights still reads the cache (its only source of names), without leaving traces in History.

## 2.0.0-alpha.24 - 2026-10-15

- **No more borrowed host names on shared addresses.** When an app connected to an address it had not looked up itself, the Network and History pages showed whatever name another app had looked up for that address. Content delivery networks serve thousands of sites from the same addresses, so this could be wrong: 2.7 GB of Brave traffic to a Fastly address showed as "artifacts.bitwarden.com" only because Nax-Updater had looked that name up, while Mozilla's services used the same address. Now an address that was answered for several names gets no name (the IP is shown), and a name that only another app looked up is marked "≈" with an explanation in the tooltip. Connections recorded before this version keep their names.
- **History shows when the list is filtered.** Selecting an app on the left filters the connection list to that app; a bar above the lists now says so ("Only connections of Brave Origin") with a button to show all apps again.

## 2.0.0-alpha.23 - 2026-10-09

- **Big written files no longer stay in RAM.** Windows keeps every written byte in its file cache: a 31 GB game update left 18 GB of The Witcher 3's texture data in memory although the game never ran. The History service now watches the NTFS change journal of every local drive and, when a file of 256 MB or more was written and closed (game updates, installers, downloads, copies), removes it from RAM 30 seconds later and once more after 5 minutes, after virus scanners have read it. Files a running program still has open or mapped keep their data. Each removal is written to `C:\ProgramData\NaxTaskManager\service.log`. Update the History service to get it.

## 2.0.0-alpha.22 - 2026-10-08

- **History page can be sorted.** Click "App" or "Data used" above the app list ("All apps" stays on top), and Started, App, Remote host, Protocol or Data above the connection list. The connection list's columns can also be resized and moved like the other tables. The sort order is remembered.
- **Sorting by Data shows the biggest connections of the whole period.** The list holds 1,000 connections; sorted by data it now loads the 1,000 largest of the chosen period instead of the newest 1,000, and the line below says which ("Largest 1,000 connections").

## 2.0.0-alpha.21 - 2026-10-08

- **Disk page: exited processes keep their name.** The lists average the last minute, so a process that wrote to disk and then exited stayed listed as "PID 18580". It now shows as, for example, "powershell.exe (exited)" with its path, in both lists. The file list also names processes that lived too briefly to be seen in a snapshot, from the trace's own process events.

## 2.0.0-alpha.20 - 2026-10-08

- **"Open file location" opens the right folder again.** For any program whose path contains a space (everything under "Program Files", for example) Explorer opened the Documents folder instead of selecting the file. This affected double-click on the Disk page and "Open file location" on every page.

## 2.0.0-alpha.19 - 2026-10-06

- **New Disk page (Datenträger)**, modelled on Resource Monitor's Disk tab.
  - **Disks:** every physical disk with model, type (SSD/HDD and bus), active time, read and write speed, response time, queue length and the free space of its drives. Works without administrator rights. Empty card readers are hidden.
  - **Processes with disk activity:** read, write and total per process, averaged over the last minute like Resource Monitor. These are bytes that really reached a disk, not cached file access. Without administrator rights the History service supplies them (update the service once from Settings when the app asks).
  - **Disk activity per file:** every file being read or written, by which process, with I/O priority and response time. Like in Resource Monitor this needs administrator rights. Its trace runs only while the list is on screen and stops when you leave it.
  - All three tables sort, resize and have movable columns; search filters both lists; double-click or *Open file location* shows the file or program in Explorer.
- **Keyboard shortcuts follow the menu order:** Datenträger is Ctrl+4, Verlauf Ctrl+5, Autostart-Apps Ctrl+6. The Autostart-Apps tooltip had shown Ctrl+6 while the shortcut was Ctrl+5; both now agree.

## 2.0.0-alpha.18 - 2026-10-06

- **Move columns on the Network and Startup apps pages too**, the same way as on the Processes page: drag a header, or right-click it for *Move left*, *Move right* and *Reset column order*. On the Network page, the per-app connection summary (established / listening / other) fills whichever address, scope and state columns sit next to each other.

## 2.0.0-alpha.17 - 2026-10-05

- **Move columns on the Processes page**, in both layouts. Drag a column header left or right and drop it where you want it (an accent bar shows where it lands), or right-click a header for *Move left*, *Move right* and *Reset column order*. The order is remembered. Name stays first, and Path (by app) or Description (all processes) stays last. Dragging never sorts; a click still does.

## 2.0.0-alpha.16 - 2026-10-05

- **GPU column on the Processes page**, in both layouts. Like Task Manager, it shows each process's busiest GPU engine; point at a value to see which engine (for example "GPU 0 - 3D"). The header shows the whole GPU, sorting works, and the column can be resized. It works without administrator rights. App groups add up their processes, the same way as CPU.

## 2.0.0-alpha.15 - 2026-10-05

- **The program files follow the product name.** The app is now `Nax-TaskManager.exe` (was `NaxTaskManager.exe`) and the History service `Nax-TaskManager.HistoryService.exe`. Setup closes a running copy under either name. If "Replace Windows Task Manager" pointed at the old exe, Setup points it at the new one (one administrator prompt). Updating the History service switches the service to the new exe and removes the old service files. Settings, history data and the service name stay as they were.

## 2.0.0-alpha.14 - 2026-10-05

- **Less kernel memory for network tracing.** The trace sessions used the library default of 64 MB of non-pageable kernel memory each. They now use 16 MB (network) and 4 MB (DNS), and a copy running as administrator no longer starts its own traces while the History service is already tracing; it uses the service's measurements and only falls back to its own traces if the service stops.
- **Memory report after startup.** The History service writes `C:\ProgramData\NaxTaskManager\boot-memory.txt` 2, 5 and 10 minutes after Windows starts: the in-use breakdown, the vendors and processes with the most memory, and kernel memory per driver. The previous boot's report is kept as `boot-memory.previous.txt`.

## 2.0.0-alpha.13 - 2026-10-05

- **Processes and Details are now one page.** The Prozesse page has a switch at the top: **Nach App** (the grouped view, with resource columns) and **Alle Prozesse** (a flat list, one row per process, with PID, status and owner). The separate Details tab is gone, and the choice is remembered.
- **Priority removed.** The priority column and the "set priority" action are gone, since they went unused.


## 2.0.0-alpha.12 - 2026-10-05

- **Details search no longer floods.** It stopped matching the owner account, so a term that is part of your Windows user name (for example "nax" inside "Naxterra") no longer matches every process. It now matches the process name, description and PID.
- **PIDs on the Processes page.** A single-process app shows its PID next to the name without expanding; multi-process apps still show the count and reveal each PID when expanded.

## 2.0.0-alpha.11 - 2026-10-05

- **Free up memory in one click.** The "Free up memory" button now runs every available step at once (trim app memory, and with administrator rights also the standby cache and all working sets). The dropdown arrow still offers the three steps individually.
- **The limited-mode notice can be dismissed for good.** Close it once and it stays closed on later launches.
- **Columns are easier to resize.** The divider between column headers is wider and highlights when you point at it, on every table (Processes, Network, Details, Startup apps).
- **Network: sorting by Remote host works clearly.** Clicking the Remote host header now reorders the whole app list by each app's remote host (apps that only listen or use UDP sort last), and the connections inside each app stay sorted by host.

## 2.0.0-alpha.10 - 2026-10-05

- **Replace Windows Task Manager** (Settings, System integration): an optional toggle that makes Nax-TaskManager open when Task Manager is started from the taskbar, Ctrl+Shift+Esc, Ctrl+Alt+Del or the Start menu. It needs administrator approval, shows when another tool already replaces Task Manager and leaves that alone, and is removed when you turn it off or uninstall the app. This uses the same documented mechanism as Sysinternals Process Explorer.

## 2.0.0-alpha.9 - 2026-10-05

- Startup apps now include **Store and packaged apps** (1Password, Terminal, WhatsApp, Spotify, Xbox…) with the same on/off state as Task Manager, and can turn them on and off. Entries set by policy cannot be changed.
- Shortcuts that only store an environment-variable target (for example 0install's DeepL auto-start) resolve to their program.
- The summary shows the last BIOS time, like Task Manager.

## 2.0.0-alpha.8 - 2026-10-05

- **Efficiency mode**, as in Task Manager (EcoQoS plus low priority): toggle it from the Processes or Details overflow or context menu. A leaf marks processes in efficiency mode, a pause icon suspended ones. Not offered for Windows' critical processes.
- **Details page** (Ctrl+5): every process on its own row with PID, status, user name, CPU, memory, priority and description; set priority, end task or process tree.
- **Startup apps page** (Ctrl+6): the Run keys and startup folders with publisher, status and whether they start for you or all users. Disable and enable work like Task Manager (the entry stays; Windows' startup flag changes). All-users entries ask for administrator approval.

## 2.0.0-alpha.7 - 2026-10-04

- **German interface.** The app follows the Windows display language; Settings → Language switches between English and Deutsch (applies after a restart). Wording follows the German Task Manager. Process names, window titles, paths and host names are never translated.
- Processes: wider Memory and Disk / I/O columns so the headers fit.
- History: wider protocol column; the update prompt shows the commit when both builds carry the same version number.

## 2.0.0-alpha.6 - 2026-10-04

- History: DNS lookups are hidden unless **DNS lookups** is switched on (they are frequent and buried the connection log).
- History: wider protocol column so scope and state fit; shorter note on the All apps row.

## 2.0.0-alpha.5 - 2026-10-04

Network work modelled on how Portmaster handles apps, connections and DNS:
- **Per-app host names**: a connection is named after what that app itself looked up (Windows DNS-Client events carry the requesting process), falling back to any known name.
- **DNS lookups in History**: one row per app and name with the answers (needs the History service).
- **App identity**: Store apps by package (not their versioned folder), per-user services without their random suffix, single-service programs by service name, PID 4 as "Operating System". History moves older rows to the new keys.
- **Short-lived processes** (CLI tools, updaters) keep their name in History via the service's process trace instead of "Exited process".
- **Scope and direction**: connections show This PC / LAN / Internet / Multicast and whether they are incoming; History records both.
- **VPN tunnels** (WireGuard, OpenVPN, Tailscale, WARP… tunnel services) are marked and left out of all-apps totals, because they carry the other apps' traffic a second time.
- Speeds fed by the History service are averaged over 3 s (they jumped between neighbouring one-second windows).
- The History page offers **Update** when the installed History service is older than the app.
- "1024 KB" shows as "1.0 MB".

## 2.0.0-alpha.4 - 2026-10-04

- **Block network now really blocks**, also while a third-party firewall (for example Bitdefender) manages the firewall: blocks are Windows Filtering Platform filters instead of Windows Firewall rules, which such firewalls switch off. Older rules are still recognised and removed on allow.
- History: app rows no longer overlap, the log is ordered by start time, and UDP traffic (DNS, QUIC) is one row per app and remote endpoint instead of one per request.
- Sizes below 1 KB show bytes instead of "0 KB".
- Context menus open from the keyboard (Shift+F10 / Menu key); End process tree, Properties and Copy path are also in the Processes command bar.
- The classic WinForms app (Better Task Manager 1.x) is retired from the repository; its last release stays available as v1.1.0-preview.57. `main` now carries the WinUI 3 app.

## 2.0.0-alpha.3 - 2026-10-04

- Per-app network speed and data without administrator rights, fed live by the History service over a read-only local pipe.
- Repository renamed to Naxterra/Nax-TaskManager.

## 2.0.0-alpha.2 - 2026-10-04

- Renamed to Nax-TaskManager (`NaxTaskManager.exe`); setup upgrades a Better Task Manager install into the new folder and keeps settings and firewall rules.
- Network page: sortable columns, Data column, Pause, stable row order.
- Performance page: Free up memory (trim app memory, clear standby cache, empty all working sets).

## 2.0.0-alpha.1 - 2026-10-03

- First release of the WinUI 3 rebuild: Processes, Performance, Network, History and Settings pages, per-app network throughput from the kernel trace, remote host names, memory figures that reconcile with Task Manager, optional background History service.

## 1.1.0-preview.57 - 2026-09-13

### Fixed

- Removed a leftover display-text replacement that unconditionally turned every "™" character into "Ö". It was a workaround for parsing localized `netstat` output that predates the native TCP/UDP collector, and could corrupt a genuine trademark symbol appearing in a username or window title.

## 1.1.0-preview.56 - 2026-08-28

### Added

- Added global live monitoring for Apps, Processes, and Network with selectable 1, 2, 5, and 15-second intervals.
- Added isolated self-test coverage for connection-history deduplication, CSV round trips, display limits, and 30-day pruning.
- Added non-blocking CSV export for Process, Network, and History views, including spreadsheet-formula protection.
- Added a self-contained single-file Windows x64 publish profile so preview testers do not need a separate .NET runtime installation.
- Added one-click grouped-app reconciliation: **View Processes** opens the contributing PIDs from the same snapshot and shows visible-row memory sums.
- Added a native Memory dashboard with physical load, used/available RAM, system cache, commit total/limit/peak, and process/thread/handle counts; it participates in selectable Live monitoring.
- Added global privilege status and **Restart as Admin** navigation; system-level memory controls are gated in Standard mode while firewall actions request just-in-time elevation.
- Added instant all-column filtering to the History view; sorting and CSV export operate on the complete filtered result.
- Added a watchdog-backed UI smoke test that verifies responsive History loading/live sampling, cached Process filtering/sorting, same-snapshot PID scope, and a responsive message-loop continuation.
- Added real live monitoring to History: the active page now samples native connections, records new/state-changed observations, and refreshes the current filtered view at the selected interval.
- Added instant all-column Network search across application, PID, user, protocol, endpoints, state, and path.
- Added explicit per-PID resolution state so successful and denied executable-path/user lookups are both cached across collectors.
- Expanded Apps search across name, path, user, firewall state, process IDs/count, and connection count.
- Added Previous/Next paging through every filtered and sorted History result while retaining the responsive 100-row render window.
- Added one shared asynchronous snapshot gate for Apps, Processes, Network, details reload, and live History collection.
- Added a global red **Live error** state that returns to green **Live** after the next successful automatic refresh.
- Added typed History sorting for timestamp, PID, local port, and remote port columns.
- Added a confirmed **Clear History** action for permanently removing the user-local connection-history store without administrator rights.
- Added grouped Apps CPU as the sum of normalized per-PID CPU values from the exact same Process snapshot.
- Added explicit per-PID CPU sample availability and visible-row CPU sums/partial-sample counts in Processes.
- Added native System CPU sampling to the Memory dashboard using Windows idle, kernel, and user time deltas.
- Added user-local persistence for window size, maximized state, and the selected Live refresh interval.
- Added persistence for user-resized Apps, Processes, Network, and History column widths.
- Added responsive multi-row wrapping for global navigation and dense page command bars.
- Added explicit `PerMonitorV2` high-DPI configuration and runtime verification.
- Added non-blocking CSV export for the current filtered and sorted Apps model.
- Added typed model-based Process and Network CSV exports with snapshot provenance and invariant numeric values.
- Added global F5, Ctrl+F, Escape, Ctrl+E, and Ctrl+L shortcuts with contextual tooltip guidance.
- Added Ctrl+1–5 view navigation and Page Up/Page Down History paging, including numpad navigation support.
- Added stateful per-adapter download/upload sampling with stable-interface counts.
- Added selection-aware **Open Folder** and **Copy Path** actions to Apps, Processes, and Network.
- Added process-start-time validation and self-process protection for destructive Process actions.
- Added explicit trimmed/failed/skipped accounting to bulk working-set trimming.
- Added one shared non-overlapping busy gate for all Memory maintenance actions.
- Added one shared non-overlapping busy state for selected Process mutations.
- Added one shared non-overlapping firewall mutation gate across Apps and Network.
- Added deterministic path-scoped Windows named-mutex locking for History transactions across app instances.
- Added a Windows GitHub Actions workflow for .NET 11 preview restore, Release build, self-test, UI smoke test, self-contained publish, and artifact upload.
- Added deterministic, self-describing portable package staging with bundled README, changelog, license, and executable SHA-256 manifest.
- Added responsive 60-sample System CPU and physical RAM-load trend charts to the Memory dashboard.
- Split Apps refresh into immediate process/network rendering followed by asynchronous firewall status enrichment.
- Added partial-result native network snapshots with per-table issue reporting.
- Added amber partial-network disclosure to Apps grouped connection metadata.
- Added comprehensive current security/privacy documentation to the repository and portable package.
- Added bounded cross-process crash logging with one rotated previous file.
- Added concise tester-facing v1.1 preview release notes covering highlights, upgrade/local data, safety/privacy, validation, and known limits.
- Added a persisted **Record history** privacy control.
- Added a packaged non-destructive manual test checklist covering every original preview report and the exact evidence expected for each result.
- Added automatic German localization with English fallback and `--language=de` / `--language=en` overrides, including dynamic status text, grids, section menus, tooltips, dialogs, and common connection states.
- Added an upgrade-safe English/German Inno Setup installer with current-user/all-users modes, Start Menu and optional desktop shortcuts, Add/Remove Programs uninstall, dynamic light/dark wizard styling, and preserved user-local data.
- Added pinned installer-tool bootstrap, deterministic installer/checksum build, isolated silent install/repair/app-test/uninstall verification, and installer artifacts in Windows CI.
- Added a custom multi-resolution violet performance icon embedded in the application, setup executable, uninstall entry, Start Menu shortcut, desktop shortcut, and taskbar window.
- Added a per-user/session single-instance coordinator that restores the existing window when the app is launched again.

### Changed

- Replaced localized `netstat` text parsing with native Windows IPv4/IPv6 TCP and UDP tables that include owning process IDs.
- The app now starts without elevation or a console and can directly restart its executable as administrator when needed.
- Reworked connection history to record new or changed connections instead of duplicating every full snapshot.
- Replaced the near-black/blue palette with a softer violet-slate theme and enabled the supported WinForms dark color mode for native controls and scrollbars.
- Removed the setup wizard's raw Markdown release-notes page; release notes remain installed as a document instead of interrupting installation flow.
- Increased the process-level UI test watchdog for self-contained cold starts while retaining the strict eight-second ceiling on every individual page refresh.
- Bound the WinForms main-window icon explicitly to the embedded executable icon so the title bar, taskbar, shortcuts, Setup, and uninstall entry all use the same artwork.
- Replaced the misleading generic `Allowed` firewall label with rule-specific `BTM Blocked` and `Not blocked by BTM` states and an exact outbound-rule explanation.
- Clarified app aggregation with a process-count column, precise Private Bytes/Working Set names, shared-page overlap guidance, summed-memory labels, and snapshot timestamps on all live views.
- Process search now filters the latest complete snapshot in memory by PID, name, user, or path; only manual/Live refresh performs a new Windows process collection.
- Network filtering and typed column sorting now operate on the cached snapshot and persist across manual or Live collection refreshes.
- Split Network actions/search from its snapshot and bandwidth status line, and assigned stable column widths for a calmer layout.
- Process identity cache entries now carry process start time, are shared safely by Apps, Processes, Network, and History, and are invalidated when Windows reuses a PID.
- Apps filtering and typed sorting now operate on the cached grouped snapshot; active sort and selected app persist across search and Live refreshes.
- History filtering or sorting returns to the first result page; manual and Live reloads preserve the current page and clamp it safely when the result set shrinks.
- Queued collection requests now re-check the active page before running, and completed requests re-check it before updating caches or UI.
- Automatic and manual refresh origins now propagate through the active-page dispatcher so error presentation can match user intent.
- History text columns remain case-insensitive while numeric and timestamp columns now sort by their real values.
- Clearing History atomically rewrites the CSV header, resets cached rows/paging, and resets connection deduplication so later observations can be recorded normally.
- Apps CPU is visible, locale-formatted, searchable, numerically sortable, and included in selected-app snapshot metadata with a reconciliation tooltip.
- First snapshots now display `...` instead of a false `0.0%`; measured idle remains `0.0`, and grouped Apps metadata reports sampling or partial coverage.
- System CPU participates in Memory Live monitoring, uses the same first-sample marker, and follows the existing green/warning/danger thresholds.
- Restored window dimensions are clamped to the current primary working area; Live enabled state is intentionally not persisted and always starts paused.
- Restored column widths are clamped to 40–1200 pixels, and closing while minimized preserves the last non-minimized maximized state correctly.
- The Apps master pane now uses a DPI-aware clamped width: it remains usable at the minimum window size, follows normal resizing, and stops expanding on ultrawide windows so the detail pane receives excess width.
- **Restart as Admin** now waits for the unelevated owner to exit before the elevated replacement claims the single-instance lock.
- Installer upgrades now execute and wait for the registered previous uninstaller before copying the new version instead of overwriting the old installation in place.
- Replaced duplicated legacy WinForms startup calls with generated `ApplicationConfiguration.Initialize()` bootstrap while preserving native and supported dark-mode initialization.
- Apps export writes snapshot time, firewall, process count, CPU and sampled-process count, connections, Private Bytes, Working Set, user, and executable path using invariant numeric values.
- Process export records CPU availability explicitly; Network export preserves native endpoint fields and normalized connection state. All four views now share one asynchronous dialog/write workflow.
- Shortcut actions route through the active page's existing manual refresh, cached filter, export, and Live semantics; Memory intentionally has no filter/export shortcut action.
- Mouse clicks and keyboard view changes now share one asynchronous navigation route, preventing refresh behavior from drifting between input methods.
- Bandwidth rates now use only adapter IDs present in both samples with nondecreasing counters; new or reset interfaces wait for a fresh baseline.
- Folder opening uses direct Windows shell activation of the resolved directory; clipboard copying retries brief contention and reports success inline.
- Force Kill confirmation now identifies the process by name, PID, and path and explicitly states that child processes are included.
- Bulk trim excludes Better Task Manager and PID 0, always restores its action button, and refreshes only the active Memory dashboard.
- Standby-cache and system-working-set native calls now run through `Task.Run`; all maintenance controls disable together and restore according to administrator state in `finally`.
- Force Kill, Open Folder, and Copy Path now follow selection, refresh, mutation, and self-process eligibility from one centralized action-state method.
- Firewall action eligibility now combines selection/path availability, active refresh, global mutation state, and known Apps rule state; Standard mode elevates only the requested mutation.
- History save/load/clear/prune now use both in-process locking and a cross-process mutex with abandoned-owner recovery and a 10-second timeout.
- CI uses current official action majors, read-only repository permissions, per-ref cancellation, a 20-minute job timeout, and the app's own UI watchdog.
- The publish script derives version from the project, validates the exact artifacts child path, removes stale staging content, and regenerates the package from scratch.
- Trend charts are code-native, double-buffered, bounded to 0–100%, and driven only by existing manual/Live Memory refreshes.
- Late firewall results now require both matching Apps snapshot time and unchanged firewall mutation revision before updating cache/grid state.
- Native TCP/UDP table allocation is bounded to 4 bytes–64 MiB and retries table growth up to five times before reporting a scoped failure.
- Consolidated Process **Refresh** and **Reload Users/Paths** into one manual refresh that rebuilds identities and values; Live refreshes continue using the cache.
- Security guidance now documents unelevated startup, privileged/destructive gates, local history/settings/crash data, export sensitivity, firewall rule scope, non-destructive tests, and checksum limitations.
- Crash reports now include timestamp, app version, .NET runtime, OS, process bitness, DPI mode, and exception details; oversized entries are truncated with an explicit marker.
- Deterministic portable staging now bundles `RELEASE_NOTES-v1.1-preview.md` ahead of the detailed changelog and security guide.
- Apps, Network, and live History writes now read one thread-safe recording flag; disabling it retains existing rows for view/export and turns live History into read-only reloads.
- Portable publishing now refreshes a stable `BetterTaskManager-latest-portable-win-x64` folder and ZIP alongside the numbered preview artifacts.
- Added a watchdog-backed `--ui-soak-test` that repeats all five page refresh paths for three rounds, enforces a bounded refresh duration, and verifies message-pump and action-gate recovery; Windows CI now runs it.
- Portable publishing accepts `-SkipLatest` so a numbered preview can be staged without interrupting a tester who is still running the stable latest executable.

### Fixed

- Prevented installer upgrades from leaving prior-version files or incremented `unins001.exe` metadata by waiting for uninstaller self-cleanup before reinstalling.
- Fixed the percentage-based Apps pane growing to roughly 860–1200 pixels on 2389–3336 pixel windows and pushing useful detail content unnecessarily far right.
- Added the missing History navigation entry so saved connection snapshots can be viewed from the app.
- Firewall and force-kill actions now report Windows command failures and timeouts instead of presenting them as successful.
- External command arguments are passed without manual quoting, including executable paths containing spaces.
- Removed the per-application firewall command loop from UI rendering; firewall status is collected once in the background on manual refresh.
- Moved history persistence off the UI thread, guarded overlapping refreshes, stopped rebuilding hidden grids, and preserved grid selection and scroll position during live updates.
- Apps now refresh when navigating back to the grouped view, preventing a stale Apps snapshot from being compared with a newly refreshed Processes snapshot without an explicit timestamp.
- Network collection now resolves user/path details once per PID and reuses the same-snapshot Process rows during Apps refresh instead of repeating protected-process lookups for every connection.
- Replaced the expensive History data grid with a fixed-column virtual list and a 100-row render window over the newest 2,000 cached records, eliminating multi-second UI freezes without truncating filtered CSV exports.
- Reduced the connection-history sampling gate from 30 seconds to one second so short-lived changes can be captured during Live monitoring while unchanged rows remain deduplicated.
- Eliminated full process enumeration and protected path/user lookups on every Process search keystroke while preserving active column sorting and exact **View Processes** PID reconciliation.
- Removed content-based Network column resizing during refresh, avoiding repeated width measurement on large connection snapshots.
- Eliminated repeated protected-process path/user calls on every Live tick and synchronized the shared CPU baseline cache to prevent concurrent Apps/Processes refresh races.
- Fixed the Apps firewall column falling back to name sorting and stopped search or refresh from silently discarding the active Apps sort.
- Removed the earlier limitation that made History matches beyond the first 100 visible only through filtering or CSV export.
- Prevented rapid cross-page navigation from running multiple expensive native collectors concurrently or applying results to a page the user already left.
- Prevented recurring Process or Network error dialogs from stacking during Live monitoring; automatic failures are reported inline while manual refresh failures remain modal.
- Fixed History Remote Port sorting throwing when blank UDP ports and numeric TCP ports were present together.
- Serialized History clearing with live store writes so clearing cannot leave a partial CSV or stale deduplication state.
- Extended grouped-app reconciliation tests to prove CPU, private bytes, and working set all equal their contributing per-PID sums.
- CPU baselines now include process start time, preventing a reused PID from inheriting another process's CPU sample; normalized values are clamped to 0–100%.
- Added deterministic native CPU calculation tests for valid deltas and invalid counter rollback, plus UI coverage for initial sampling state.
- Settings writes are atomic, corrupt JSON falls back to defaults, and self/UI tests use isolated temporary settings files rather than the real user profile.
- Extended settings tests cover column-width round trips, UI capture, clamping, and minimized/maximized state policy.
- Added minimum-window UI bounds checks for navigation, Apps cards/actions, and Process/Network/History toolbars.
- Added UI smoke coverage proving the packaged app starts in `PerMonitorV2` mode without regressing responsive layout or dark controls.
- Added Apps export schema/reconciliation tests, blank unavailable CPU handling, and spreadsheet-formula protection for every model field.
- Replaced Process/Network grid-text scraping with deterministic export schemas and added exact field tests for CPU, memory, endpoints, state, identity, and timestamp.
- Removed the redundant Network panel Enter refresh so focusing its filter no longer starts an unintended native collection.
- Added runtime shortcut tests for actual Memory navigation and both History paging directions in addition to exhaustive static mapping coverage.
- Fixed negative total bandwidth values when VPN, Wi-Fi, or other interface counters reset or disappear between snapshots.
- Path actions now disable themselves when selection/path data is unavailable, and they remain inside responsive wrapping toolbars at narrow widths.
- Fixed stale PID reuse potentially targeting a different process between snapshot and Force Kill/Trim; Trim failures now receive scoped error handling.
- Removed the obsolete Process refresh requested from the Memory page after bulk trim, which the stale-page collector gate correctly discarded.
- Fixed Clear Standby Cache and Release System Cache blocking the WinForms UI thread while Windows performed native maintenance.
- Prevented overlapping Force Kill/Trim operations and disabled stale path/mutation actions while Process or Network refreshes replace their grids.
- Prevented simultaneous Apps/Network firewall commands and guaranteed both pages' controls recover in `finally` after success, failure, or early command return.
- Prevented overlapping normal/elevated or manually duplicated app instances from racing header creation, append, prune replacement, or clear operations on the same history CSV.
- Added documentation distinguishing locally verified workflow commands from GitHub execution, which begins only after the branch is pushed.
- CI now uploads the complete portable folder instead of only the executable; package tests compare `SHA256SUMS.txt` with a freshly computed executable hash.
- Added trend normalization, rolling-capacity, initial CPU availability, RAM append, and UI integration tests.
- Prevented slow or failed firewall enumeration from delaying or relabeling an otherwise successful Apps snapshot, and prevented stale reads from overwriting a concurrent rule mutation.
- Prevented one unavailable IPv4/IPv6 TCP/UDP table from discarding every healthy connection table; verified documented network-order IPv6 scope parsing remains intact.
- Replaced obsolete Network path guidance with protected-process/elevation guidance and added Apps completeness status tests.
- Fixed stale documentation claiming the app requests elevation at startup, renamed README limits for the current preview, and added `SECURITY.md` to deterministic package staging.
- Prevented `crash.log` from growing indefinitely or racing overlapping app instances; current and previous logs remain capped at 1 MiB each.
- Documented that existing local history/settings remain in place, Live starts paused, v1.0 remains unchanged, and background ETW/WFP functionality is still outside the desktop preview.
- Added isolated opt-out tests proving disabled recording prevents a unique snapshot write, displays **Recording off**, and persists through settings round-trip while corrupt settings default to recording enabled.
- Fixed elevated Memory actions failing with `STATUS_PRIVILEGE_NOT_HELD`: the process now enables `SeProfileSingleProcessPrivilege`, verifies whether Windows assigned it, and only enables **Clear Standby Cache** and **Release System Cache** when the capability is genuinely available.
- Replaced the contradictory “Administrator”/“required privilege unavailable” guidance with separate elevation and memory-privilege status plus the actual Windows error when activation fails.
- Fixed the Apps detail heading, metadata, metric cards, action row, firewall status, Connections heading, and grid starting at different horizontal positions.
- Moved Apps firewall status into its own aligned row so it no longer wraps beneath the action buttons with an extra left offset.
- Fixed the Apps search field being stretched to the full row height, which placed its text against the upper edge; it now retains its native edit height and is vertically centered.
- Removed WinForms glyph-overhang padding from Apps headings/status text and trim display names before rendering, eliminating the apparent leading spaces before names such as `svchost`.
- Replaced the small low-contrast native sort glyph with one larger violet-white triangle drawn at the right edge of only the active sort column, including Remote Port.
- Removed the noisy neutral arrow prefix from every unsorted header and restored compact single-line header labels.
- Widened Peak Working Set so its full header remains visible without clipping.
- Removed the duplicated per-process Trim action; working-set Trim remains under Memory with categorized results.
- Added page-specific right-click action menus: each section mirrors its own toolbar operations, preserves normal text-edit menus, and selects a right-clicked grid row before exposing target-sensitive actions.
- Standard-user Block/Unblock buttons now request just-in-time UAC elevation through a hidden helper process instead of remaining disabled or requiring a full app restart.
- Renamed **No BTM Block** to **Not blocked by BTM** to state that only this app's outbound rule is absent, not that Windows permits the traffic.
- Replaced the Apps search field's native baseline placement with an explicit vertically centered edit formatting rectangle and centered custom placeholder rendering.
- Split bulk working-set Trim results into protected/access-denied, exited-during-scan, other failure, and intentional skip categories, with Standard/Admin-specific explanation.

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

