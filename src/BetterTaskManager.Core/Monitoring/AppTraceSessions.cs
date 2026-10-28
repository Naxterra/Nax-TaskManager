using BetterTaskManager.Core.Disk;
using BetterTaskManager.Core.Network;
using Microsoft.Diagnostics.Tracing.Session;

namespace BetterTaskManager.Core.Monitoring;

/// <summary>
/// The kernel trace sessions an elevated app window starts. Windows keeps a session running after the process that
/// started it is killed or crashes, with its non-pageable buffers, until the next restart. The History service calls
/// <see cref="StopIfAppNotRunning"/> regularly, and an elevated app calls <see cref="StopAll"/> at start.
/// </summary>
public static class AppTraceSessions
{
    public const string AppExecutableName = "Nax-TaskManager.exe";

    public static readonly string[] Names =
    [
        BandwidthMonitor.DefaultSessionName,
        HostNameResolver.DefaultSessionName,
        DiskFileActivity.DefaultSessionName,
        DiskFileActivity.DefaultSessionName + "-Names"
    ];

    /// <summary>Stops the app's sessions when no process in <paramref name="snapshot"/> is the app. Returns how many were stopped.</summary>
    public static int StopIfAppNotRunning(MonitorSnapshot snapshot)
    {
        if (snapshot.Processes.Any(IsApp)) return 0;
        // The snapshot is a few seconds old, and a window can start at any moment: look again right before each stop.
        return StopAll(keep: AppIsRunning);
    }

    /// <summary>
    /// For a window that starts: stops sessions left behind by a killed copy, unless another copy runs. Its sessions
    /// may be live, for example in another user's session, where the single-instance check does not reach, and the
    /// session names are the same for the whole machine.
    /// </summary>
    public static int StopIfNoOtherCopy() => StopAll(keep: () => AppIsRunning(exceptSelf: true));

    private static bool AppIsRunning() => AppIsRunning(exceptSelf: false);

    private static bool AppIsRunning(bool exceptSelf)
    {
        System.Diagnostics.Process[] running = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(AppExecutableName));
        bool found = running.Any(process => !exceptSelf || process.Id != Environment.ProcessId);
        foreach (System.Diagnostics.Process process in running) process.Dispose();
        return found;
    }

    /// <summary>
    /// Stops every one of the app's sessions that is running, unless <paramref name="keep"/> says otherwise just
    /// before a stop. Returns how many were stopped.
    /// </summary>
    public static int StopAll(Func<bool>? keep = null)
    {
        int stopped = 0;
        List<string> active;
        try
        {
            active = TraceEventSession.GetActiveSessionNames();
        }
        catch (Exception)
        {
            return 0;
        }
        foreach (string name in Names.Where(name => active.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            if (keep?.Invoke() == true) break;
            try
            {
                TraceEventSession.GetActiveSession(name)?.Stop();
                stopped++;
            }
            catch (Exception)
            {
                // Already gone, or no rights to stop it.
            }
        }
        return stopped;
    }

    private static bool IsApp(ProcessSample process) =>
        string.Equals(process.ImageName, AppExecutableName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(System.IO.Path.GetFileName(process.Path), AppExecutableName, StringComparison.OrdinalIgnoreCase);
}
