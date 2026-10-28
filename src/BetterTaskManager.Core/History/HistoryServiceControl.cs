using System.ServiceProcess;
using BetterTaskManager.Core.Firewall;

namespace BetterTaskManager.Core.History;

public enum HistoryServiceState
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// Installs, removes and queries the background history service. Install and uninstall need administrator rights.
/// The service binaries are copied to Program Files: a LocalSystem service must never run from a folder that
/// standard users can write to, and the development build lives in one.
/// </summary>
public static class HistoryServiceControl
{
    public const string ServiceName = "NaxTaskManagerHistory";
    public const string DisplayName = "Nax-TaskManager History";
    public const string ExecutableName = "Nax-TaskManager.HistoryService.exe";
    /// <summary>File name prefixes of earlier service builds, removed from the install folder on update.</summary>
    private static readonly string[] LegacyFilePrefixes = ["NaxTaskManager.HistoryService.", "BetterTaskManager.HistoryService."];
    /// <summary>Folder next to the app that holds the service build to install from.</summary>
    public const string BundledFolderName = "HistoryService";
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The service's own folder, separate from the app's: an install for all users puts the app (with its bundled
    /// service files) in Program Files\Nax-TaskManager, and the running service must never share that folder, or
    /// installing would copy files onto themselves and removing the service would delete the app's files.
    /// </summary>
    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nax-TaskManager History");

    /// <summary>Where versions up to 2.0.0-alpha.41 installed the service; moved to <see cref="InstallFolder"/> on update.</summary>
    public static string LegacyInstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nax-TaskManager", "HistoryService");

    /// <summary>Product version of the service build in <paramref name="folder"/>, or null.</summary>
    public static string? VersionIn(string folder)
    {
        string exe = Path.Combine(folder, ExecutableName);
        return File.Exists(exe) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
    }

    /// <summary>The folder Windows starts the service from (its registered binary path), or null when not installed.</summary>
    public static string? RegisteredFolder()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName);
            if (key?.GetValue("ImagePath") is not string imagePath) return null;
            string exe = Environment.ExpandEnvironmentVariables(imagePath.Trim()).Trim('"');
            return Path.GetDirectoryName(exe);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the installed service is another build than the one shipped with the app, or still runs from the
    /// folder of earlier versions.
    /// </summary>
    public static bool NeedsUpdate(string bundledFolder)
    {
        if (QueryState() == HistoryServiceState.NotInstalled || VersionIn(bundledFolder) is not { } bundled) return false;
        string folder = RegisteredFolder() ?? InstallFolder;
        if (!SamePath(folder, InstallFolder)) return true;
        return VersionIn(folder) is { } installed && bundled != installed;
    }

    /// <summary>Version of the service Windows actually runs, for display.</summary>
    public static string? InstalledVersion() => VersionIn(RegisteredFolder() ?? InstallFolder);

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    public static HistoryServiceState QueryState()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            return service.Status switch
            {
                ServiceControllerStatus.Running => HistoryServiceState.Running,
                ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => HistoryServiceState.Starting,
                ServiceControllerStatus.StopPending or ServiceControllerStatus.PausePending => HistoryServiceState.Stopping,
                _ => HistoryServiceState.Stopped
            };
        }
        catch (InvalidOperationException)
        {
            return HistoryServiceState.NotInstalled;
        }
    }

    /// <summary>PID of the running service process from the Service Control Manager, or 0.</summary>
    public static uint QueryProcessId()
    {
        const int ScManagerConnect = 0x0001, ServiceQueryStatus = 0x0004, ScStatusProcessInfo = 0, StatusSize = 36, ProcessIdOffset = 28;
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return 0;
        try
        {
            IntPtr service = OpenService(manager, ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero) return 0;
            try
            {
                byte[] status = new byte[StatusSize];
                return QueryServiceStatusEx(service, ScStatusProcessInfo, status, StatusSize, out _)
                    ? BitConverter.ToUInt32(status, ProcessIdOffset)
                    : 0;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, int access);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, int access);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, byte[] buffer, int size, out int needed);

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    /// <summary>Copies the service from <paramref name="sourceFolder"/>, registers it (automatic, delayed start) and starts it. Also updates an existing install.</summary>
    public static CommandResult Install(string sourceFolder)
    {
        string sourceExe = Path.Combine(sourceFolder, ExecutableName);
        if (!File.Exists(sourceExe)) return Failed("The service files were not found next to the app: " + sourceExe);
        if (SamePath(sourceFolder, InstallFolder)) return Failed("The service cannot be installed from its own install folder.");

        bool installed = QueryState() != HistoryServiceState.NotInstalled;
        if (installed && Stop() is { Succeeded: false } stopFailed) return stopFailed;

        try
        {
            CopyFolder(sourceFolder, InstallFolder);
            // A removal before the last restart may have left deletions scheduled in this folder. They are dropped only
            // once the new files are in place, so a failed copy keeps the cleanup of the old folder.
            CancelPendingDeletes(InstallFolder);
            RemoveLegacyFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("Could not copy the service files to " + InstallFolder + ": " + ex.Message);
        }

        string binPath = "\"" + Path.Combine(InstallFolder, ExecutableName) + "\"";
        CommandResult result = installed
            ? Sc("config", ServiceName, "binPath=", binPath, "start=", "delayed-auto", "DisplayName=", DisplayName)
            : Sc("create", ServiceName, "binPath=", binPath, "start=", "delayed-auto", "DisplayName=", DisplayName);
        if (!result.Succeeded) return result;

        Sc("description", ServiceName, "Records which apps connect where and how much data they use, for Nax-TaskManager's History page.");
        Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000");

        try
        {
            using var service = new ServiceController(ServiceName);
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StatusTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return Failed("The service was installed but did not start: " + ex.Message);
        }
        // The service now runs from its own folder; the folder of earlier versions goes, unless it is where these
        // files came from (an install for all users keeps its bundled copy there).
        RemoveLegacyFolder(keep: sourceFolder);
        return new CommandResult(0, "", "", false);
    }

    /// <summary>
    /// Stops and removes the service and its binaries. The recorded history stays in the data folder.
    /// <paramref name="keepFolder"/> is the app's bundled service folder, which is never deleted: in an install for
    /// all users it is the folder earlier versions ran the service from, and the uninstaller removes it itself.
    /// </summary>
    public static CommandResult Uninstall(string? keepFolder = null)
    {
        if (QueryState() == HistoryServiceState.NotInstalled) return new CommandResult(0, "", "", false);
        if (Stop() is { Succeeded: false } stopFailed) return stopFailed;
        CommandResult result = Sc("delete", ServiceName);
        if (!result.Succeeded) return result;
        DeleteFolder(InstallFolder);
        RemoveLegacyFolder(keepFolder);
        return new CommandResult(0, "", "", false);
    }

    private static void RemoveLegacyFolder(string? keep)
    {
        if (keep is not null && SamePath(keep, LegacyInstallFolder)) return;
        // An install for all users next to this one keeps its bundled service files in that folder.
        string? parent = Path.GetDirectoryName(LegacyInstallFolder);
        if (parent is not null && Directory.Exists(parent) && Directory.EnumerateFiles(parent, "*.exe").Any()) return;
        DeleteFolder(LegacyInstallFolder);
        // Its parent held nothing else for an install for one user.
        try
        {
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteFolder(string folder)
    {
        // The process can hold its files for a moment after reporting Stopped.
        for (int attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
        if (!Directory.Exists(folder)) return;
        // Still held (an antivirus scan, for example): Windows deletes what is left at the next restart, files first,
        // then the folders from the deepest up. The folder is renamed first where possible, so a service installed
        // into the same place before that restart is not deleted with it.
        string renamed = folder + ".delete-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.Move(folder, renamed);
            folder = renamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Install cancels pending deletions in its folder, so the original place is still safe to reuse.
        }
        // Inaccessible entries are skipped instead of ending the walk, so everything that can be scheduled is.
        var everything = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", everything)) MoveFileEx(file, null, DelayUntilReboot);
            foreach (string directory in Directory.EnumerateDirectories(folder, "*", everything).OrderByDescending(path => path.Length)) MoveFileEx(directory, null, DelayUntilReboot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        MoveFileEx(folder, null, DelayUntilReboot);
    }

    private const int DelayUntilReboot = 4;   // MOVEFILE_DELAY_UNTIL_REBOOT

    /// <summary>
    /// The PendingFileRenameOperations list without the deletions of <paramref name="folder"/> and anything inside it.
    /// The list holds pairs: the source as "\??\C:\...", then the target, which is empty for a deletion. A list that
    /// is not made of pairs was written by something else and is returned unchanged.
    /// </summary>
    public static string[] WithoutDeletionsIn(string[] entries, string folder)
    {
        if (entries.Length % 2 != 0) return entries;
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var kept = new List<string>();
        for (int index = 0; index + 1 < entries.Length; index += 2)
        {
            string source = entries[index].StartsWith(@"\??\", StringComparison.Ordinal) ? entries[index][4..] : entries[index];
            bool inFolder = SamePath(source, folder) || source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            if (inFolder && entries[index + 1].Length == 0) continue;
            kept.Add(entries[index]);
            kept.Add(entries[index + 1]);
        }
        return kept.ToArray();
    }

    /// <summary>
    /// Drops deletions scheduled for the next restart (PendingFileRenameOperations) inside <paramref name="folder"/>,
    /// so files installed there now are not deleted at that restart. Renames scheduled elsewhere stay as they are.
    /// The value is written back right after it was read and only when something was removed, which keeps the window
    /// in which another program's new entry could be overwritten as short as possible.
    /// </summary>
    internal static void CancelPendingDeletes(string folder)
    {
        const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager";
        const string ValueName = "PendingFileRenameOperations";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string[] entries) return;
            string[] kept = WithoutDeletionsIn(entries, folder);
            if (kept.Length == entries.Length) return;
            if (kept.Length == 0) key.DeleteValue(ValueName, throwOnMissingValue: false);
            else key.SetValue(ValueName, kept, Microsoft.Win32.RegistryValueKind.MultiString);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            // Without access the scheduled deletion stays; the service files are copied anyway.
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? replacement, int flags);

    private static CommandResult Stop()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, StatusTimeout);
            }
            return new CommandResult(0, "", "", false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return Failed("Could not stop the service: " + ex.Message);
        }
    }

    /// <summary>Deletes the files of earlier builds whose executable had another name; the service is stopped here.</summary>
    private static void RemoveLegacyFiles()
    {
        foreach (string file in Directory.EnumerateFiles(InstallFolder))
        {
            string name = Path.GetFileName(file);
            if (!LegacyFilePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover file is harmless; the service runs the new executable.
            }
        }
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static CommandResult Sc(params string[] arguments) =>
        CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments);

    private static CommandResult Failed(string message) => new(1, "", message, false);
}
