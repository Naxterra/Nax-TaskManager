using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace BetterTaskManager.Core.Startup;

/// <summary>
/// Startup tasks of Store and packaged desktop apps (the windows.startupTask manifest extension). Their state is kept
/// per user under SystemAppData\&lt;package family&gt;\&lt;task id&gt;; Task Manager lists and changes them there too.
/// </summary>
internal static class PackageStartupTasks
{
    private const string StateRoot = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\";

    // State values (documented for StartupTaskState): 0 disabled, 1 disabled by user, 2 enabled,
    // 3 disabled by policy, 4 enabled by policy.
    private static readonly long MinimumFileTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private const int StateDisabledByUser = 1, StateEnabled = 2, StateDisabledByPolicy = 3, StateEnabledByPolicy = 4;

    public static List<StartupApp> Read()
    {
        var result = new List<StartupApp>();
        IEnumerable<Windows.ApplicationModel.Package> packages;
        try
        {
            packages = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("");
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (Windows.ApplicationModel.Package package in packages)
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage) continue;
                string folder = package.InstalledPath;
                string manifest = Path.Combine(folder, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;
                string text = File.ReadAllText(manifest);
                if (!text.Contains("windows.startupTask", StringComparison.Ordinal)) continue;
                AddTasks(result, package, folder, XDocument.Parse(text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or System.Xml.XmlException or ArgumentException)
            {
                // One unreadable package must not hide the others.
            }
        }
        return result;
    }

    private static void AddTasks(List<StartupApp> result, Windows.ApplicationModel.Package package, string folder, XDocument manifest)
    {
        string family = package.Id.FamilyName;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement extension in manifest.Descendants().Where(element => element.Name.LocalName == "Extension" &&
                     (string?)element.Attribute("Category") == "windows.startupTask"))
        {
            XElement? task = extension.Elements().FirstOrDefault(element => element.Name.LocalName == "StartupTask");
            string? taskId = (string?)task?.Attribute("TaskId");
            // Some packages declare the same task once per app entry; Task Manager then lists it twice.
            if (task is null || string.IsNullOrEmpty(taskId) || !seen.Add(taskId)) continue;

            // The program is on the extension, or else on the app entry that declares it (Windows Terminal).
            string executable = (string?)extension.Attribute("Executable") ??
                (string?)extension.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Application")?.Attribute("Executable") ?? "";
            string path = executable.Length > 0 ? Path.Combine(folder, executable) : "";
            bool manifestEnabled = string.Equals((string?)task.Attribute("Enabled"), "true", StringComparison.OrdinalIgnoreCase);
            (bool enabled, bool locked, DateTime? disabledAt) = ReadState(family, taskId, manifestEnabled);
            string name = DisplayName((string?)task.Attribute("DisplayName"), package);
            result.Add(new StartupApp(StartupSource.Package, taskId, path, File.Exists(path) ? path : "", name,
                SafePublisher(package), enabled, disabledAt) { PackageFamily = family, Locked = locked });
        }
    }

    /// <summary>
    /// Enabled means State 2 or 4. A pending "ShowNotification" (Windows has not told the user yet that the app
    /// turned itself on) is treated as not started: on this PC such tasks did not run at sign-in and Task Manager
    /// shows them as disabled. This is observed behaviour; the value is not documented.
    /// </summary>
    private static (bool Enabled, bool Locked, DateTime? DisabledAt) ReadState(string family, string taskId, bool manifestEnabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StateRoot + family + "\\" + taskId);
        if (key?.GetValue("State") is not int state) return (manifestEnabled, false, null);
        bool pendingNotice = key.GetValue("ShowNotification") is 1;
        bool enabled = state is StateEnabled or StateEnabledByPolicy && !pendingNotice;
        // Some apps leave a value that is not a FILETIME; only plausible dates are shown.
        DateTime? disabledAt = !enabled && key.GetValue("LastDisabledTime") is long time && time > MinimumFileTime ? DateTime.FromFileTimeUtc(time).ToLocalTime() : null;
        return (enabled, state is StateDisabledByPolicy or StateEnabledByPolicy, disabledAt);
    }

    /// <summary>Like Task Manager: the user's choice, so the app cannot switch itself back on.</summary>
    public static void SetEnabled(string family, string taskId, bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(StateRoot + family + "\\" + taskId, writable: true);
        if (enabled)
        {
            key.SetValue("State", StateEnabled, RegistryValueKind.DWord);
            key.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);
            key.SetValue("ShowNotification", 0, RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue("State", StateDisabledByUser, RegistryValueKind.DWord);
            key.SetValue("LastDisabledTime", DateTime.UtcNow.ToFileTimeUtc(), RegistryValueKind.QWord);
        }
    }

    /// <summary>Resolves "ms-resource:" names through the package's resources; falls back to the package name.</summary>
    private static string DisplayName(string? name, Windows.ApplicationModel.Package package)
    {
        string fallback = SafeDisplayName(package);
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        if (!name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return name;

        string resource = name["ms-resource:".Length..];
        string uri = resource.StartsWith("//", StringComparison.Ordinal) ? "ms-resource:" + resource
            : resource.Contains('/') ? $"ms-resource://{package.Id.Name}/{resource.TrimStart('/')}"
            : $"ms-resource://{package.Id.Name}/Resources/{resource}";
        var buffer = new StringBuilder(512);
        return SHLoadIndirectString($"@{{{package.Id.FullName}? {uri}}}", buffer, buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0
            ? buffer.ToString()
            : fallback;
    }

    private static string SafeDisplayName(Windows.ApplicationModel.Package package)
    {
        try { return package.DisplayName; } catch (COMException) { return package.Id.Name; }
    }

    private static string SafePublisher(Windows.ApplicationModel.Package package)
    {
        try { return package.PublisherDisplayName; } catch (COMException) { return ""; }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);
}
