using System.Diagnostics;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Turns "Replace Windows Task Manager" on or off and remembers the choice, so the app can tell when the setting was
/// removed behind its back (security software or Windows sometimes delete such redirects).
/// </summary>
internal static class TaskManagerSwitch
{
    /// <summary>
    /// True when the user turned the replacement on and no redirect exists any more. Only an absent redirect counts:
    /// one pointing at another tool, or at another Nax-TaskManager copy (a development build), is not "removed".
    /// </summary>
    public static bool WasRemoved => App.Settings.ReplaceTaskManagerWanted && TaskManagerReplacement.CurrentTarget() is null;

    /// <summary>Settings from before this flag existed: a redirect to Nax-TaskManager means the user wanted it.</summary>
    public static void RememberExistingChoice()
    {
        if (TaskManagerReplacement.CurrentTarget() is { } target &&
            Path.GetFileName(target.Trim('"')).Equals("Nax-TaskManager.exe", StringComparison.OrdinalIgnoreCase))
        {
            App.Settings.ReplaceTaskManagerWanted = true;
        }
    }

    /// <summary>Applies the setting (elevated helper when needed). Returns an error text, or null on success.</summary>
    public static async Task<string?> SetAsync(bool enable)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return Loc.Get("Uac_CancelledNothing");
        string? error = null;
        if (App.Monitor.IsElevated)
        {
            try
            {
                if (enable) TaskManagerReplacement.Enable(exe);
                else TaskManagerReplacement.Disable();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                error = ex.Message;
            }
        }
        else
        {
            // No administrator rights: a short-lived elevated copy changes the machine-wide setting (one UAC prompt).
            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            startInfo.ArgumentList.Add(enable ? Program.ReplaceTaskManagerOnArgument : Program.ReplaceTaskManagerOffArgument);
            if (enable) startInfo.ArgumentList.Add(exe);
            try
            {
                using Process helper = Process.Start(startInfo)!;
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) error = Loc.F("Common_ExitCode", helper.ExitCode);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                error = Loc.Get("Uac_CancelledNothing");
            }
        }
        if (error is null) App.Settings.ReplaceTaskManagerWanted = enable;
        return error;
    }
}
