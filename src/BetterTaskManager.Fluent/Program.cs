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
    private const string SingleInstanceMutexName = @"Local\Naxterra.NaxTaskManager.SingleInstance";
    internal const string FirewallBlockArgument = "--firewall-block";
    internal const string FirewallUnblockArgument = "--firewall-unblock";
    internal const string WaitForProcessArgument = "--wait-for-pid";
    private const string TestInstanceArgument = "--test-instance";
    internal const string StartupEnableArgument = "--startup-enable";
    internal const string StartupDisableArgument = "--startup-disable";
    internal const string ReplaceTaskManagerOnArgument = "--replace-taskmanager-on";
    internal const string ReplaceTaskManagerOffArgument = "--replace-taskmanager-off";

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

        // Elevated helper mode: turn an all-users startup entry on or off and exit.
        if (args.Length == 3 && (args[0] == StartupEnableArgument || args[0] == StartupDisableArgument))
        {
            if (!FirewallRules.IsElevated) return 5;
            if (!Enum.TryParse(args[1], out Core.Startup.StartupSource source)) return 2;
            try
            {
                Core.Startup.StartupApps.SetEnabled(source, args[2], args[0] == StartupEnableArgument);
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return 1;
            }
        }

        // Elevated helper mode: make this app replace Windows Task Manager, or restore the built-in one, then exit.
        if (args.Length == 2 && args[0] == ReplaceTaskManagerOnArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            try
            {
                Core.Native.TaskManagerReplacement.Enable(args[1]);
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return 1;
            }
        }
        if (args.Length >= 1 && args[0] == ReplaceTaskManagerOffArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            try
            {
                // With a path (uninstall), only restore if the redirect is ours, so another tool's entry stays.
                if (args.Length == 2) Core.Native.TaskManagerReplacement.DisableIfOurs(args[1]);
                else Core.Native.TaskManagerReplacement.Disable();
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return 1;
            }
        }

        WaitForPreviousInstance(args);
        // --test-instance: automated UI tests run a build next to the copy the user has open.
        if (!args.Contains(TestInstanceArgument) && !TryAcquireSingleInstance())
        {
            ActivateExistingWindow();
            return 0;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            // The language must be chosen before XAML loads any resource; --lang overrides the setting for tests.
            int langIndex = Array.IndexOf(args, "--lang");
            Loc.ApplyLanguage(langIndex >= 0 && langIndex + 1 < args.Length ? args[langIndex + 1] : AppSettings.Load().Language);
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
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "NaxTaskManager");
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
