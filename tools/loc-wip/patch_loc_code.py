import os
os.chdir(r"D:\Projects\Nax-TaskManager\src")

def edit(p, pairs, all_ok=()):
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        n = s.count(a)
        assert n == 1 or (a in all_ok and n >= 1), (p, a[:90], n)
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8').write(s)

C = 'BetterTaskManager.Core/'
F = 'BetterTaskManager.Fluent/'
V = F + 'Views/'
VM = F + 'ViewModels/'

# --- Core: cleanup results say what went wrong, so the app can word it in either language.
edit(C + 'Native/MemoryCleanup.cs', [
('''/// <summary>Outcome of a system-wide memory list command.</summary>
public readonly record struct CleanupResult(bool Succeeded, string Message);''',
 '''public enum CleanupFailure { None, PrivilegeNotGranted, PrivilegeError, PrivilegeNotHeld, AccessDenied, Status }

/// <summary>Outcome of a system-wide memory list command. <see cref="Code"/> is the Win32 error or NTSTATUS.</summary>
public readonly record struct CleanupResult(bool Succeeded, string Message, CleanupFailure Failure = CleanupFailure.None, uint Code = 0);'''),
('''            return new CleanupResult(false, privilegeError == 1300
                ? "Windows did not grant the \\"Profile single process\\" privilege to this process. This needs administrator rights."
                : $"Could not enable the \\"Profile single process\\" privilege (error {privilegeError}).");''',
 '''            return privilegeError == 1300
                ? new CleanupResult(false, "Windows did not grant the \\"Profile single process\\" privilege to this process. This needs administrator rights.", CleanupFailure.PrivilegeNotGranted)
                : new CleanupResult(false, $"Could not enable the \\"Profile single process\\" privilege (error {privilegeError}).", CleanupFailure.PrivilegeError, (uint)privilegeError);'''),
('''            StatusPrivilegeNotHeld => new CleanupResult(false, "Windows refused: the required privilege is not held. This needs administrator rights."),
            StatusAccessDenied => new CleanupResult(false, "Windows refused the request (access denied)."),
            _ => new CleanupResult(false, $"Windows returned status 0x{status:X8}.")''',
 '''            StatusPrivilegeNotHeld => new CleanupResult(false, "Windows refused: the required privilege is not held. This needs administrator rights.", CleanupFailure.PrivilegeNotHeld, status),
            StatusAccessDenied => new CleanupResult(false, "Windows refused the request (access denied).", CleanupFailure.AccessDenied, status),
            _ => new CleanupResult(false, $"Windows returned status 0x{status:X8}.", CleanupFailure.Status, status)'''),
])

edit(F + 'MainWindow.xaml.cs', [
('''"System process details need administrator rights. Per-app network speed comes from the History service."''', '''Loc.Get("Limited_FeedMessage")'''),
])
edit(F + 'Services/HistoryServiceSetup.cs', [
('''$"Exit code {helper.ExitCode}."''', '''Loc.F("Common_ExitCode", helper.ExitCode)'''),
('''"Administrator approval was cancelled. Nothing was changed."''', '''Loc.Get("Uac_CancelledNothing")'''),
])
edit(F + 'Services/MonitorHost.cs', [
('''"Administrator approval was cancelled. The firewall was not changed."''', '''Loc.Get("Uac_CancelledFirewall")'''),
('''$"Windows Firewall returned exit code {exitCode}."''', '''Loc.F("Firewall_Failed", exitCode)'''),
])

edit(VM + 'HistoryViewModel.cs', [
('''"Could not read the history: " + result.Error''', '''Loc.F("History_ReadError", result.Error)'''),
('''$"Recording in the background · last saved {last.ToLocalTime():T}" : "Recording in the background",''',
 '''Loc.F("History_RecordingSaved", last.ToLocalTime().ToString("T")) : Loc.Get("History_Recording"),'''),
('''HasData ? "Background recording is off · showing earlier history" : "Background recording is off",''',
 '''HasData ? Loc.Get("History_OffWithData") : Loc.Get("History_Off"),'''),
('''_ => "The history service is not running"''', '''_ => Loc.Get("History_ServiceStopped")'''),
('''new AppUsageRow("", "All apps", "", $"{Format.Count(result.Apps.Sum(app => app.Connections))} connections" + (counted.Count < result.Apps.Count ? " · VPN excluded" : ""),''',
 '''new AppUsageRow("", Loc.Get("History_AllApps"), "", Loc.F("Common_Connections", Format.Count(result.Apps.Sum(app => app.Connections))) + (counted.Count < result.Apps.Count ? Loc.Get("History_VpnExcluded") : ""),'''),
('''apps.Add(new AppUsageRow(app.AppKey, app.AppName, app.AppPath,''', '''apps.Add(new AppUsageRow(app.AppKey, Loc.AppName(app.AppName), app.AppPath,'''),
('''(app.Connections == 1 ? "1 connection" : $"{Format.Count(app.Connections)} connections") + (app.Tunnel ? " · VPN tunnel, not in total" : ""),''',
 '''(app.Connections == 1 ? Loc.Get("Common_OneConnection") : Loc.F("Common_Connections", Format.Count(app.Connections))) + (app.Tunnel ? Loc.Get("History_TunnelNotInTotal") : ""),'''),
('''? $"Latest {Format.Count(ConnectionLimit)} connections"''', '''? Loc.F("History_LatestConnections", Format.Count(ConnectionLimit))'''),
(''': $"{Format.Count(result.Connections.Count)} connections";''', ''': Loc.F("Common_Connections", Format.Count(result.Connections.Count));'''),
('''$"First seen {first:G}\\nLast seen {last:G}\\nDuration {Format.Duration(last - first)}"''',
 '''Loc.F("History_TimeDetail", first.ToString("G"), last.ToString("G"), Format.Duration(last - first))'''),
('''? $"{endpoint}\\nName from reverse DNS; may be the hosting provider rather than the service"''', '''? Loc.F("Remote_Reverse", endpoint)'''),
(''': $"{endpoint}\\nName the app looked up";''', ''': Loc.F("Remote_Lookup", endpoint);'''),
('''"no addresses"''', '''Loc.Get("Dns_NoAddresses")'''),
('''$"DNS lookup by the app\\nAnswer: {answers}", "DNS lookup", "–"''', '''Loc.F("Dns_Detail", answers), Loc.Get("Dns_Lookup"), "–"'''),
('''string protocol = record.Protocol == "UDP" ? "UDP" : $"TCP · {record.State}";''', '''string protocol = record.Protocol == "UDP" ? "UDP" : "TCP · " + Loc.State(record.State);'''),
('''protocol += " · " + record.Scope + (record.Inbound ? " · in" : "");''', '''protocol += " · " + Loc.Scope(record.Scope) + (record.Inbound ? Loc.Get("Scope_Inbound") : "");'''),
('''record.AppName, record.AppPath''', '''Loc.AppName(record.AppName), record.AppPath'''),
], all_ok={'''record.AppName, record.AppPath'''})

edit(VM + 'NetworkViewModel.cs', [
('''(first.Pid == 0 ? "Closing connections (no owning process)" : "PID " + first.Pid)''', '''(first.Pid == 0 ? Loc.Get("Network_Closing") : "PID " + first.Pid)'''),
(''': AppIdentityRules.AppName(owner);''', ''': Loc.AppName(AppIdentityRules.AppName(owner));'''),
('''$"{established} established · {listening} listening · {group.Connections.Count - established - listening} other"''',
 '''Loc.F("Network_GroupSummary", established, listening, group.Connections.Count - established - listening)'''),
('''                    connection.State, "", "",''', '''                    Loc.State(connection.State), "", "",'''),
('''string scope = IpScopes.Label(connection.Scope);''', '''string scope = Loc.Scope(IpScopes.Label(connection.Scope));'''),
('''return connection.Inbound ? scope + " · in" : scope;''', '''return connection.Inbound ? scope + Loc.Get("Scope_Inbound") : scope;'''),
('''? $" · all apps ↓ {Format.NetworkRate(counted.Sum(p => p.NetworkReceiveBytesPerSecond))} ↑ {Format.NetworkRate(counted.Sum(p => p.NetworkSendBytesPerSecond))}" +''',
 '''? Loc.F("Network_AllApps", Format.NetworkRate(counted.Sum(p => p.NetworkReceiveBytesPerSecond)), Format.NetworkRate(counted.Sum(p => p.NetworkSendBytesPerSecond))) +'''),
('''(counted.Count < snapshot.Processes.Count ? " (VPN tunnels not counted)" : "")''', '''(counted.Count < snapshot.Processes.Count ? Loc.Get("Network_VpnNotCounted") : "")'''),
(''': " · per-app speed needs administrator rights or the History service";''', ''': Loc.Get("Network_NeedsAdmin");'''),
('''$"{Format.Count(snapshot.Connections.Count)} connections · {Format.Count(totalEstablished)} established · " +''',
 '''Loc.F("Network_Summary", Format.Count(snapshot.Connections.Count), Format.Count(totalEstablished), groups.Count) +'''),
('''$"{groups.Count} apps shown" + throughput''', '''throughput'''),
('''" · measured by the History service"''', '''Loc.Get("Network_FromService")'''),
('''" · some network tables could not be read"''', '''Loc.Get("Network_TablesFailed")'''),
('''return "UDP sockets have no fixed remote side";''', '''return Loc.Get("Remote_UdpNone");'''),
('''? $"{endpoint}\\nName from reverse DNS; may be the hosting provider rather than the service"''', '''? Loc.F("Remote_Reverse", endpoint)'''),
(''': $"{endpoint}\\nName the app looked up";''', ''': Loc.F("Remote_Lookup", endpoint);'''),
])

edit(VM + 'ProcessesViewModel.cs', [
(''': monitor.IsElevated ? "Paused" : "Admin";''', ''': monitor.IsElevated ? Loc.Get("Header_Paused") : Loc.Get("Header_Admin");'''),
('''? "Current send + receive per app, measured by the Nax-TaskManager History service (loopback excluded)"''', '''? Loc.Get("Bandwidth_TooltipService")'''),
(''': "Current send + receive per app, measured from the kernel's TCP/IP events (loopback excluded)"''', ''': Loc.Get("Bandwidth_TooltipKernel")'''),
(''': monitor.IsElevated ? system.PerProcessNetworkStatus''', ''': monitor.IsElevated ? Loc.NetworkStatus(system.PerProcessNetworkStatus)'''),
(''': system.PerProcessNetworkStatus + " Use Restart as administrator.";''', ''': Loc.NetworkStatus(system.PerProcessNetworkStatus) + Loc.Get("Bandwidth_UseRestart");'''),
])
edit(VM + 'ProcessTree.cs', [
('''AddSection(rows, "Apps", ''', '''AddSection(rows, Loc.Get("Section_Apps"), '''),
('''AddSection(rows, "Background processes", ''', '''AddSection(rows, Loc.Get("Section_Background"), '''),
('''string name = "Service Host: " + AppIdentityRules.ServiceName(services[0]);''', '''string name = Loc.Get("Name_ServiceHost") + AppIdentityRules.ServiceName(services[0]);'''),
])

edit(V + 'HistoryPage.xaml.cs', [
('''!ViewModel.HasData ? "Nothing has been recorded yet."''', '''!ViewModel.HasData ? Loc.Get("History_EmptyNothing")'''),
('''? "No connections match the search."''', '''? Loc.Get("History_EmptySearch")'''),
(''': "No connections in this period.";''', ''': Loc.Get("History_EmptyPeriod");'''),
('''ServiceBar.Title = "Update the History service";''', '''ServiceBar.Title = Loc.Get("Service_UpdateTitle");'''),
('''$"The running service is {Short(HistoryServiceControl.VersionIn(HistoryServiceControl.InstallFolder))}; this app brings {Short(HistoryServiceControl.VersionIn(HistoryServiceSetup.BundledFolder))}. Recording continues during the update."''',
 '''Loc.F("Service_UpdateMessage", Short(HistoryServiceControl.VersionIn(HistoryServiceControl.InstallFolder)), Short(HistoryServiceControl.VersionIn(HistoryServiceSetup.BundledFolder)))'''),
('''ServiceButton.Content = "Update";''', '''ServiceButton.Content = Loc.Get("Service_Update");'''),
('''ServiceBar.Title = "Background recording is off";''', '''ServiceBar.Title = Loc.Get("History_Off");'''),
('''"A small Windows service can record which apps connect where and how much data they use, even while this window is closed. History is kept for 30 days."''', '''Loc.Get("Service_OffMessage")'''),
('''ServiceButton.Content = "Turn on";''', '''ServiceButton.Content = Loc.Get("Service_TurnOn");'''),
('''ServiceBar.Title = "The history service is not running";''', '''ServiceBar.Title = Loc.Get("History_ServiceStopped");'''),
('''"Nothing is recorded until it runs again."''', '''Loc.Get("Service_StoppedMessage")'''),
('''ServiceButton.Content = "Start";''', '''ServiceButton.Content = Loc.Get("Service_Start");'''),
('''?? "unknown"''', '''?? Loc.Get("Common_Unknown")'''),
('''Title = "Background recording not turned on"''', '''Title = Loc.Get("Service_NotTurnedOn")'''),
('''CloseButtonText = "OK"''', '''CloseButtonText = Loc.Get("Common_OK")'''),
('''Text = "Copy remote host"''', '''Text = Loc.Get("Menu_CopyRemoteHost")'''),
('''Text = "Open file location"''', '''Text = Loc.Get("Menu_OpenLocation")'''),
])
edit(V + 'NetworkPage.xaml.cs', [
('''FirewallButton.Label = blocked ? "Allow network" : "Block network";''', '''FirewallButton.Label = blocked ? Loc.Get("Firewall_AllowShort") : Loc.Get("Firewall_BlockShort");'''),
('''blocked ? "Allow network access" : "Block network access"''', '''blocked ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block")'''),
('''Text = "Open file location"''', '''Text = Loc.Get("Menu_OpenLocation")'''),
('''Text = "Copy remote address"''', '''Text = Loc.Get("Menu_CopyRemoteAddress")'''),
('''Title = "Block network access?",''', '''Title = Loc.Get("Firewall_ConfirmTitle"),'''),
('''$"Adds a Windows Firewall rule that blocks all outbound connections for:\\n{path}"''', '''Loc.F("Firewall_ConfirmText", path)'''),
('''PrimaryButtonText = "Block",''', '''PrimaryButtonText = Loc.Get("Firewall_BlockButton"),'''),
('''CloseButtonText = "Cancel",''', '''CloseButtonText = Loc.Get("Common_Cancel"),'''),
('''Title = "Firewall rule not changed"''', '''Title = Loc.Get("Firewall_NotChanged")'''),
('''CloseButtonText = "OK"''', '''CloseButtonText = Loc.Get("Common_OK")'''),
])

edit(V + 'PerformancePage.xaml.cs', [
('''new("Apps and processes", "Memory private to each process. Task Manager's per-app Memory column adds up to this.",''', '''new(Loc.Get("Mem_Apps"), Loc.Get("Mem_AppsDesc"),'''),
('''new("Windows kernel", "Kernel and driver data structures held in RAM (paged pool in RAM plus non-paged pool).",''', '''new(Loc.Get("Mem_Kernel"), Loc.Get("Mem_KernelDesc"),'''),
('''new("File cache in use", "File data Windows currently has mapped for reading and writing.",''', '''new(Loc.Get("Mem_Cache"), Loc.Get("Mem_CacheDesc"),'''),
('''new("Driver code", "Loaded kernel-mode driver code.",''', '''new(Loc.Get("Mem_Drivers"), Loc.Get("Mem_DriversDesc"),'''),
('''new("Shared and other", "Shared program code and mapped files (counted once), page tables, and memory drivers lock directly, for example GPU and VM drivers. Sysinternals RAMMap can split this further.",''', '''new(Loc.Get("Mem_Shared"), Loc.Get("Mem_SharedDesc"),'''),
('''new("Modified", "Changed data waiting to be written to disk. Becomes available afterwards.",''', '''new(Loc.Get("Mem_Modified"), Loc.Get("Mem_ModifiedDesc"),'''),
('''new("Standby cache", "Recently used data kept for speed. Windows hands it to apps immediately, so it counts as available.",''', '''new(Loc.Get("Mem_Standby"), Loc.Get("Mem_StandbyDesc"),'''),
('''new("Free", "Unused memory.",''', '''new(Loc.Get("Mem_Free"), Loc.Get("Mem_FreeDesc"),'''),
('''item.Text += " (needs administrator)";''', '''item.Text += Loc.Get("Cleanup_NeedsAdmin");'''),
('''["Trim"] = new("Trim app memory?",''', '''["Trim"] = new(Loc.Get("Cleanup_TrimTitle"),'''),
('''"Asks every app this account can reach to give back the memory it is not actively using. Nax-TaskManager itself is skipped. " +''', '''Loc.Get("Cleanup_TrimText"),'''),
('''"\\"In use\\" drops, but the pages only move to the standby or modified list, and apps read them back in as they need them, which can make them briefly slower.",''', ''''''),
('''            "Trim"),''', '''            Loc.Get("Cleanup_TrimButton")),'''),
('''["Standby"] = new("Clear the standby cache?",''', '''["Standby"] = new(Loc.Get("Cleanup_StandbyTitle"),'''),
('''"Discards recently used file and program data that Windows keeps in RAM for speed. Free memory goes up, but the next launches and file reads come from disk again. " +''', '''Loc.Get("Cleanup_StandbyText"),'''),
('''"Standby memory already counts as available, so this rarely helps an app that is short of memory.",''', ''''''),
('''            "Clear"),''', '''            Loc.Get("Cleanup_StandbyButton")),'''),
('''["System"] = new("Empty all working sets?",''', '''["System"] = new(Loc.Get("Cleanup_SystemTitle"),'''),
('''"Trims every process at once, including Windows services and the kernel's system working set. Expect stutter for a few seconds while everything pages back in.",''', '''Loc.Get("Cleanup_SystemText"),'''),
('''            "Empty")''', '''            Loc.Get("Cleanup_SystemButton"))'''),
('''Text = action.Explanation + "\\n\\nWindows uses spare RAM as cache on purpose. These are troubleshooting tools, not routine optimisation.",''', '''Text = action.Explanation + "\\n\\n" + Loc.Get("Cleanup_Note"),'''),
('''CloseButtonText = "Cancel",''', '''CloseButtonText = Loc.Get("Common_Cancel"),'''),
('''? $" {trim.Denied} protected processes refused."''', '''? Loc.F("Cleanup_ProtectedRefused", trim.Denied)'''),
(''': $" {trim.Denied} processes need administrator rights.";''', ''': Loc.F("Cleanup_NeedAdminCount", trim.Denied);'''),
('''return (true, $"Trimmed {Format.Count(trim.Trimmed)} processes.{refused}");''', '''return (true, Loc.F("Cleanup_Trimmed", Format.Count(trim.Trimmed)) + refused);'''),
('''"Standby" => ToTuple(await Task.Run(MemoryCleanup.PurgeStandbyList)),''', '''"Standby" => ToTuple(await Task.Run(MemoryCleanup.PurgeStandbyList), "Cleanup_StandbyDone"),'''),
('''_ => ToTuple(await Task.Run(MemoryCleanup.EmptySystemWorkingSets))''', '''_ => ToTuple(await Task.Run(MemoryCleanup.EmptySystemWorkingSets), "Cleanup_SystemDone")'''),
('''static (bool, string) ToTuple(CleanupResult result) => (result.Succeeded, result.Message);''',
 '''static (bool, string) ToTuple(CleanupResult result, string doneKey) => (result.Succeeded, result.Succeeded ? Loc.Get(doneKey) : Loc.CleanupFailure(result));'''),
('''$" In use {Format.Gigabytes(before.InUse)} → {Format.Gigabytes(after.InUse)}, " +''', '''Loc.F("Cleanup_Delta", Format.Gigabytes(before.InUse), Format.Gigabytes(after.InUse),'''),
('''$"standby {Format.Gigabytes(before.Standby)} → {Format.Gigabytes(after.Standby)}, " +''', '''Format.Gigabytes(before.Standby), Format.Gigabytes(after.Standby),'''),
('''$"free {Format.Gigabytes(before.Free)} → {Format.Gigabytes(after.Free)}.";''', '''Format.Gigabytes(before.Free), Format.Gigabytes(after.Free));'''),
('''CleanupBar.Title = succeeded ? "Done" : "Not done";''', '''CleanupBar.Title = succeeded ? Loc.Get("Cleanup_Done") : Loc.Get("Cleanup_NotDone");'''),
('''Text = "In use", Style''', '''Text = Loc.Get("Mem_InUse"), Style'''),
('''Text = "Not in use", Style''', '''Text = Loc.Get("Mem_NotInUse"), Style'''),
('''$"Updated {snapshot.Timestamp:T} · collected in {snapshot.CollectionTime.TotalMilliseconds:0} ms"''',
 '''Loc.F("Perf_Updated", snapshot.Timestamp.ToString("T"), snapshot.CollectionTime.TotalMilliseconds.ToString("0"))'''),
('''$"{Format.Count(system.ProcessCount)} processes · {Format.Count(system.ThreadCount)} threads · " +''', '''Loc.F("Perf_CpuDetail", Format.Count(system.ProcessCount), Format.Count(system.ThreadCount),'''),
('''$"{Format.Count(system.HandleCount)} handles · {Environment.ProcessorCount} logical processors · up {Format.Duration(system.Uptime)}";''',
 '''Format.Count(system.HandleCount), Environment.ProcessorCount, Format.Duration(system.Uptime));'''),
('''$"Available {Format.Gigabytes(memory.Available)} · Cached {Format.Gigabytes(memory.Standby + memory.Modified)} · " +''',
 '''Loc.F("Perf_MemoryDetail", Format.Gigabytes(memory.Available), Format.Gigabytes(memory.Standby + memory.Modified),'''),
('''$"Committed {Format.Gigabytes(memory.CommitTotal)} / {Format.Gigabytes(memory.CommitLimit)}";''', '''Format.Gigabytes(memory.CommitTotal), Format.Gigabytes(memory.CommitLimit));'''),
('''$"Scale {Format.NetworkRate(networkMax)} · {Format.Count(snapshot.Connections.Count)} open connections · " +''',
 '''Loc.F("Perf_NetworkDetail", Format.NetworkRate(networkMax), Format.Count(snapshot.Connections.Count)) +'''),
('''"receive (green) and send (amber), all active adapters";''', '''"";'''),
('''$"In use · {Format.Gigabytes(memory.InUse)}"''', '''Loc.F("Mem_InUseTotal", Format.Gigabytes(memory.InUse))'''),
('''$"Not in use · {Format.Gigabytes(memory.Modified + memory.Standby + memory.Free)}"''', '''Loc.F("Mem_NotInUseTotal", Format.Gigabytes(memory.Modified + memory.Standby + memory.Free))'''),
])

edit(V + 'ProcessesPage.xaml.cs', [
('''$"{Format.Count(system.ProcessCount)} processes · {Format.Count(system.ThreadCount)} threads · " +''', '''Loc.F("Proc_Summary", Format.Count(system.ProcessCount), Format.Count(system.ThreadCount),'''),
('''$"{Format.Count(system.HandleCount)} handles · up {Format.Duration(system.Uptime)}";''', '''Format.Count(system.HandleCount), Format.Duration(system.Uptime));'''),
('''FirewallButton.Label = blocked ? "Allow network" : "Block network";''', '''FirewallButton.Label = blocked ? Loc.Get("Firewall_AllowShort") : Loc.Get("Firewall_BlockShort");'''),
('''MenuItem("End task",''', '''MenuItem(Loc.Get("Proc_EndTask"),'''),
('''"End process tree"''', '''Loc.Get("Proc_EndTree")'''),
('''MenuItem("Open file location",''', '''MenuItem(Loc.Get("Menu_OpenLocation"),'''),
('''MenuItem("Properties",''', '''MenuItem(Loc.Get("Proc_Properties"),'''),
('''MenuItem("Copy path",''', '''MenuItem(Loc.Get("Proc_CopyPath"),'''),
('''blocked ? "Allow network access" : "Block network access"''', '''blocked ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block")'''),
('''ConfirmAsync("End a critical system process?",''', '''ConfirmAsync(Loc.Get("Proc_CriticalTitle"),'''),
('''$"Windows marks \\"{name}\\" as critical. Ending it will crash or restart Windows immediately and unsaved work is lost.",''', '''Loc.F("Proc_CriticalText", name),'''),
('''"End it anyway"''', '''Loc.Get("Proc_EndAnyway")'''),
('''ConfirmAsync($"End \\"{name}\\" and everything it started?",''', '''ConfirmAsync(Loc.F("Proc_TreeTitle", name),'''),
('''"Ending the process tree also ends every child process, which can include unrelated apps started from it.",''', '''Loc.Get("Proc_TreeText"),'''),
('''"\\n\\nRestarting as administrator allows ending more processes."''', '''"\\n\\n" + Loc.Get("Proc_RestartHint")'''),
('''ShowMessageAsync($"Could not end \\"{name}\\"",''', '''ShowMessageAsync(Loc.F("Proc_CouldNotEnd", name),'''),
('''ConfirmAsync("Block network access?",''', '''ConfirmAsync(Loc.Get("Firewall_ConfirmTitle"),'''),
('''$"Adds a Windows Firewall rule that blocks all outbound connections for:\\n{path}", "Block"))''', '''Loc.F("Firewall_ConfirmText", path), Loc.Get("Firewall_BlockButton")))'''),
('''ShowMessageAsync("Firewall rule not changed", error)''', '''ShowMessageAsync(Loc.Get("Firewall_NotChanged"), error)'''),
('''CloseButtonText = "Cancel",''', '''CloseButtonText = Loc.Get("Common_Cancel"),'''),
('''CloseButtonText = "OK"''', '''CloseButtonText = Loc.Get("Common_OK")'''),
], all_ok={'''"End process tree"'''})

edit(V + 'SettingsPage.xaml.cs', [
('''elevated ? "Running as administrator" : "Running as standard user";''', '''elevated ? Loc.Get("Settings_Elevated") : Loc.Get("Settings_Standard");'''),
('''? "All processes show their path, publisher and icon, per-app network speed is measured, and firewall changes need no extra prompt."''', '''? Loc.Get("Settings_ElevatedText")'''),
(''': "Per-app network speed is unavailable, system processes hide their path, publisher and icon, and each firewall change asks for administrator approval.";''', ''': Loc.Get("Settings_StandardText");'''),
('''"On, but the service is older than this app. Use Update on the History page.",''', '''Loc.Get("Settings_HistoryOld"),'''),
('''HistoryServiceState.Running => "On. Recording while Windows runs, even with this window closed.",''', '''HistoryServiceState.Running => Loc.Get("Settings_HistoryOn"),'''),
('''App.Monitor.IsElevated ? "Off." : "Off. Turning it on asks for administrator approval once.",''', '''App.Monitor.IsElevated ? Loc.Get("Settings_HistoryOff") : Loc.Get("Settings_HistoryOffUac"),'''),
('''HistoryServiceState.Starting => "Starting…",''', '''HistoryServiceState.Starting => Loc.Get("Settings_HistoryStarting"),'''),
('''_ => "Installed, but the service is not running."''', '''_ => Loc.Get("Settings_HistoryNotRunning")'''),
('''enable ? "Turning on…" : "Turning off…";''', '''enable ? Loc.Get("Settings_TurningOn") : Loc.Get("Settings_TurningOff");'''),
('''Title = enable ? "Background recording not turned on" : "Background recording not turned off"''', '''Title = enable ? Loc.Get("Service_NotTurnedOn") : Loc.Get("Service_NotTurnedOff")'''),
('''CloseButtonText = "OK"''', '''CloseButtonText = Loc.Get("Common_OK")'''),
])

# Files that now use Loc need the Services namespace.
for p in (VM + 'HistoryViewModel.cs', VM + 'NetworkViewModel.cs', VM + 'ProcessesViewModel.cs', VM + 'ProcessTree.cs',
          V + 'HistoryPage.xaml.cs', V + 'NetworkPage.xaml.cs', V + 'PerformancePage.xaml.cs', V + 'ProcessesPage.xaml.cs',
          V + 'SettingsPage.xaml.cs', F + 'MainWindow.xaml.cs'):
    s = open(p, encoding='utf-8').read()
    if 'using BetterTaskManager.Fluent.Services;' not in s and 'namespace BetterTaskManager.Fluent.Services' not in s:
        s = 'using BetterTaskManager.Fluent.Services;\n' + s
        open(p, 'w', encoding='utf-8').write(s)
print("patched")
