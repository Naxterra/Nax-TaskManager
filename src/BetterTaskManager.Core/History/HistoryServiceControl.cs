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

    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nax-TaskManager", "HistoryService");

    /// <summary>Product version of the service build in <paramref name="folder"/>, or null.</summary>
    public static string? VersionIn(string folder)
    {
        string exe = Path.Combine(folder, ExecutableName);
        return File.Exists(exe) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
    }

    /// <summary>True when the installed service is another build than the one shipped with the app.</summary>
    public static bool NeedsUpdate(string bundledFolder) =>
        QueryState() != HistoryServiceState.NotInstalled && VersionIn(bundledFolder) is { } bundled &&
        VersionIn(InstallFolder) is { } installed && bundled != installed;

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

        bool installed = QueryState() != HistoryServiceState.NotInstalled;
        if (installed && Stop() is { Succeeded: false } stopFailed) return stopFailed;

        try
        {
            CopyFolder(sourceFolder, InstallFolder);
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
        return new CommandResult(0, "", "", false);
    }

    /// <summary>Stops and removes the service and its binaries. The recorded history stays in the data folder.</summary>
    public static CommandResult Uninstall()
    {
        if (QueryState() == HistoryServiceState.NotInstalled) return new CommandResult(0, "", "", false);
        if (Stop() is { Succeeded: false } stopFailed) return stopFailed;
        CommandResult result = Sc("delete", ServiceName);
        if (!result.Succeeded) return result;

        // The process can hold its files for a moment after reporting Stopped.
        for (int attempt = 0; attempt < 10 && Directory.Exists(InstallFolder); attempt++)
        {
            try
            {
                Directory.Delete(InstallFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
        return new CommandResult(0, "", "", false);
    }

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
