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
    internal const string UninstallCleanupArgument = "--uninstall-cleanup";
    internal const string CacheTrimOnArgument = "--cache-trim-on";
    internal const string CacheTrimOffArgument = "--cache-trim-off";
    internal const string StandbyLimitArgument = "--standby-limit";
    internal const string PolicyApplyArgument = "--policy-apply";
    internal const string RulesApplyArgument = "--rules-apply";
    internal const string MemoryFeatureArgument = "--memory-feature";

    private static Mutex? s_singleInstance;

    /// <summary>
    /// Restores Windows Task Manager if it points at <paramref name="executablePath"/>, removes every block this app
    /// made (WFP filters and older Windows Firewall rules) and the History service. Each step runs even if another
    /// failed. The exit code has one bit per failed step (1 Task Manager, 2 blocks, 4 service); details go to
    /// <paramref name="resultFile"/>.
    /// </summary>
    private static int UninstallCleanup(string executablePath, string resultFile)
    {
        int code = 0;
        var details = new List<string>();
        try
        {
            Core.Native.TaskManagerReplacement.DisableIfOurs(executablePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            code |= 1;
            details.Add("Task Manager: " + ex.Message);
        }
        try
        {
            FirewallRules.RemoveAll();
        }
        catch (Exception ex) when (ex is Core.Firewall.WfpException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            code |= 2;
            details.Add("Blocks: " + ex.Message);
        }
        CommandResult service = Core.History.HistoryServiceControl.Uninstall();
        if (!service.Succeeded)
        {
            code |= 4;
            details.Add("History service: " + service.FailureSummary());
        }
        try
        {
            File.WriteAllText(resultFile, code == 0 ? "OK" : string.Join(Environment.NewLine, details));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return code;
    }

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

        // Elevated helper mode: apply one app's network rules (JSON, Base64) and exit.
        if (args.Length == 2 && args[0] == PolicyApplyArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            AppNetworkPolicy? policy;
            try
            {
                policy = System.Text.Json.JsonSerializer.Deserialize<AppNetworkPolicy>(Convert.FromBase64String(args[1]));
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
            {
                return 87;
            }
            if (policy is null) return 87;
            CommandResult result = FirewallRules.ApplyPolicy(policy);
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

        // Elevated helper mode: switch the service's removal of written files from RAM on or off, then exit.
        if (args.Length == 1 && (args[0] == CacheTrimOnArgument || args[0] == CacheTrimOffArgument))
        {
            if (!FirewallRules.IsElevated) return 5;
            try
            {
                Core.Disk.CacheTrimmer.SetEnabled(args[0] == CacheTrimOnArgument);
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return 1;
            }
        }

        // Elevated helper mode: set the standby limit in megabytes, or "off", for the History service, then exit.
        if (args.Length == 2 && args[0] == StandbyLimitArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            int? megabytes = args[1] == "off" ? null : int.TryParse(args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : -1;
            if (megabytes is < 0) return 87;
            try
            {
                Core.Native.StandbyLimiter.SetLimit(megabytes);
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return 1;
            }
        }

        // Elevated helper mode: save one app's rules from the Rules page (JSON, Base64) in one step, then exit. With a
        // third argument, what failed is written to that file for the window that started the helper.
        if ((args.Length == 2 || args.Length == 3) && args[0] == RulesApplyArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            var details = new List<string>();
            int code;
            try
            {
                RuleChange? change = System.Text.Json.JsonSerializer.Deserialize<RuleChange>(Convert.FromBase64String(args[1]));
                code = change?.Apply(details) ?? RuleChange.Invalid;
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
            {
                details.Add(ex.Message);
                code = RuleChange.Invalid;
            }
            if (args.Length == 3 && code != 0)
            {
                try
                {
                    File.WriteAllText(args[2], string.Join(Environment.NewLine, details));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
            return code;
        }

        // Elevated helper mode: switch one of Windows' memory manager features on or off, then exit.
        if (args.Length == 3 && args[0] == MemoryFeatureArgument && (args[2] == "on" || args[2] == "off"))
        {
            if (!FirewallRules.IsElevated) return 5;
            if (!Core.Native.MemoryManager.Features.Contains(args[1])) return 87;
            try
            {
                Core.Native.MemoryManager.Set(args[1], args[2] == "on");
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
                or System.Management.ManagementException or System.Runtime.InteropServices.COMException or System.ServiceProcess.TimeoutException)
            {
                return 1;
            }
        }

        // Elevated helper mode for the uninstaller: undo everything outside the app folder, then exit.
        if (args.Length == 3 && args[0] == UninstallCleanupArgument)
        {
            if (!FirewallRules.IsElevated) return 5;
            return UninstallCleanup(args[1], args[2]);
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
