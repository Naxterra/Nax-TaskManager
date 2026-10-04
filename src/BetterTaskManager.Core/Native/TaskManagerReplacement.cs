using Microsoft.Win32;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// Makes Nax-TaskManager open in place of Windows Task Manager, the same mechanism Sysinternals Process Explorer's
/// "Replace Task Manager" option uses: an Image File Execution Options "Debugger" value for <c>taskmgr.exe</c>.
/// Windows then starts this app whenever <c>taskmgr.exe</c> would start — the taskbar menu, Ctrl+Shift+Esc,
/// Ctrl+Alt+Del and the Start menu all launch it by image name, so all of them open this app instead.
///
/// The value lives under HKLM (there is no per-user Image File Execution Options), so changing it needs
/// administrator rights. It is removed again when the user turns the option off and when the app is uninstalled,
/// so the built-in Task Manager keeps working. The replacement is launched without Task Manager's automatic
/// elevation, so this app opens in limited mode with "Restart as administrator" available.
/// </summary>
public static class TaskManagerReplacement
{
    private const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe";
    private const string DebuggerValue = "Debugger";

    /// <summary>The executable Windows currently redirects <c>taskmgr.exe</c> to, or null when nothing replaces it.</summary>
    public static string? CurrentTarget()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(IfeoKey);
            return key?.GetValue(DebuggerValue) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>True when Task Manager is redirected to this executable.</summary>
    public static bool IsReplacedBy(string executablePath) =>
        CurrentTarget() is { } target && PathsEqual(Unquote(target), executablePath);

    /// <summary>
    /// The program that currently replaces Task Manager when it is some other tool (for example Process Explorer),
    /// or null when nothing replaces it or this app does.
    /// </summary>
    public static string? OtherTarget(string executablePath)
    {
        string? target = CurrentTarget();
        if (string.IsNullOrWhiteSpace(target)) return null;
        string path = Unquote(target);
        return PathsEqual(path, executablePath) ? null : path;
    }

    /// <summary>Redirects <c>taskmgr.exe</c> to this app. Needs administrator rights (HKLM).</summary>
    public static void Enable(string executablePath)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(IfeoKey, writable: true);
        key.SetValue(DebuggerValue, "\"" + executablePath + "\"", RegistryValueKind.String);
    }

    /// <summary>Restores the built-in Task Manager. Needs administrator rights (HKLM).</summary>
    public static void Disable()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(IfeoKey, writable: true);
        key?.DeleteValue(DebuggerValue, throwOnMissingValue: false);
    }

    /// <summary>Restores the built-in Task Manager only if the redirect points at this app, so another tool's entry is left alone (used on uninstall).</summary>
    public static void DisableIfOurs(string executablePath)
    {
        if (IsReplacedBy(executablePath)) Disable();
    }

    private static string Unquote(string value) => value.Trim().Trim('"');

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
