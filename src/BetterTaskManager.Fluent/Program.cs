using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BetterTaskManager.Fluent;

public static class Program
{
    private const string SingleInstanceMutexName = @"Local\Naxterra.BetterTaskManager.Fluent.SingleInstance";
    internal const string FirewallBlockArgument = "--firewall-block";
    internal const string FirewallUnblockArgument = "--firewall-unblock";
    internal const string WaitForProcessArgument = "--wait-for-pid";

    private static Mutex? s_singleInstance;

    [STAThread]
    private static int Main(string[] args)
    {
        // Elevated helper mode: change one firewall rule and exit without starting the UI.
        if (args.Length == 2 && (args[0] == FirewallBlockArgument || args[0] == FirewallUnblockArgument))
        {
            if (!FirewallRules.IsElevated) return 5;
            CommandResult result = FirewallRules.Apply(args[1], args[0] == FirewallBlockArgument);
            return result.Succeeded ? 0 : (result.ExitCode == 0 ? 1 : result.ExitCode);
        }

        // Elevated helper mode: install or remove the background history service and exit.
        if (args.Length == 2 && (args[0] == HistoryServiceSetup.InstallArgument || args[0] == HistoryServiceSetup.UninstallArgument))
        {
            if (!FirewallRules.IsElevated) return 5;
            return HistoryServiceSetup.RunHelper(args[0], args[1]);
        }

        WaitForPreviousInstance(args);
        if (!TryAcquireSingleInstance())
        {
            ActivateExistingWindow();
            return 0;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
            return 0;
        }
        finally
        {
            ReleaseSingleInstance();
        }
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            s_singleInstance = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance created the mutex; a standard-user process may not open it. That still means
            // another instance is running, so hand over to it instead of crashing.
            return false;
        }

        try
        {
            return s_singleInstance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    internal static void ReleaseSingleInstance()
    {
        if (s_singleInstance is null) return;
        try
        {
            s_singleInstance.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread/process.
        }
        s_singleInstance.Dispose();
        s_singleInstance = null;
    }

    /// <summary>After "Restart as administrator" the new process waits until the old window has closed.</summary>
    private static void WaitForPreviousInstance(string[] args)
    {
        int index = Array.IndexOf(args, WaitForProcessArgument);
        if (index < 0 || index + 1 >= args.Length) return;
        if (!int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) return;
        try
        {
            using Process previous = Process.GetProcessById(pid);
            previous.WaitForExit(10000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private static void ActivateExistingWindow()
    {
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "BetterTaskManager.Fluent");
        foreach (Process process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, 9);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
