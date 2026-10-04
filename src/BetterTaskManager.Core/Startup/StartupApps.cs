using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BetterTaskManager.Core.Startup;

/// <summary>Where a startup entry lives. The "AllUsers" ones need administrator rights to change.</summary>
public enum StartupSource
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserFolder,
    MachineFolder,
    /// <summary>Startup task of a Store or packaged desktop app (per user).</summary>
    Package
}

public sealed record StartupApp(
    StartupSource Source,
    /// <summary>Registry value name, or the shortcut's file name in a startup folder.</summary>
    string Name,
    string Command,
    /// <summary>The program that starts; empty when it cannot be worked out from the command.</summary>
    string ExecutablePath,
    string DisplayName,
    string Publisher,
    bool Enabled,
    /// <summary>When the entry was turned off (Windows records it), else null.</summary>
    DateTime? DisabledAt)
{
    public bool AllUsers => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.MachineFolder;
    /// <summary>Package family name for <see cref="StartupSource.Package"/> entries.</summary>
    public string PackageFamily { get; init; } = "";
    /// <summary>Set by an administrator or group policy; cannot be changed here.</summary>
    public bool Locked { get; init; }
    public string Key => $"{Source}|{PackageFamily}|{Name}";
}

/// <summary>
/// The startup apps Task Manager lists from the Run keys and startup folders. Turning one off works like Task
/// Manager: the entry stays and Windows' StartupApproved flag is set, so it can be turned on again.
/// </summary>
public static class StartupApps
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupApp> Read()
    {
        var result = new List<StartupApp>();
        ReadRun(result, RegistryHive.CurrentUser, RegistryView.Default, StartupSource.UserRun);
        ReadRun(result, RegistryHive.LocalMachine, RegistryView.Registry64, StartupSource.MachineRun);
        ReadRun(result, RegistryHive.LocalMachine, RegistryView.Registry32, StartupSource.MachineRun32);
        ReadFolder(result, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
        ReadFolder(result, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.MachineFolder);
        result.AddRange(PackageStartupTasks.Read());
        return result;
    }

    public static void SetEnabled(StartupApp app, bool enabled)
    {
        if (app.Source == StartupSource.Package) PackageStartupTasks.SetEnabled(app.PackageFamily, app.Name, enabled);
        else SetEnabled(app.Source, app.Name, enabled);
    }

    /// <summary>Firmware (POST) time of the last boot, which Task Manager shows as "Last BIOS time".</summary>
    public static TimeSpan? LastBiosTime()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        return key?.GetValue("FwPOSTTime") is int milliseconds && milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : null;
    }

    /// <summary>Sets Windows' StartupApproved flag. Entries for all users need administrator rights.</summary>
    public static void SetEnabled(StartupSource source, string name, bool enabled)
    {
        (RegistryHive hive, string subKey) = ApprovedLocation(source);
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey key = root.CreateSubKey(subKey, writable: true);
        byte[] value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
        key.SetValue(name, value, RegistryValueKind.Binary);
    }

    private static void ReadRun(List<StartupApp> result, RegistryHive hive, RegistryView view, StartupSource source)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? run = root.OpenSubKey(RunKey);
            if (run is null) return;
            foreach (string name in run.GetValueNames())
            {
                if (name.Length == 0 || run.GetValue(name) is not string command || command.Trim().Length == 0) continue;
                result.Add(Create(source, name, command, ExecutableFromCommand(command)));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // An unreadable key simply contributes nothing.
        }
    }

    private static void ReadFolder(List<StartupApp> result, string folder, StartupSource source)
    {
        if (folder.Length == 0 || !Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            string target = file, command = file;
            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ShellLinks.TryResolve(file, out string linkTarget, out string arguments))
            {
                target = linkTarget;
                command = arguments.Length > 0 ? $"\"{linkTarget}\" {arguments}" : $"\"{linkTarget}\"";
            }
            result.Add(Create(source, name, command, target));
        }
    }

    private static StartupApp Create(StartupSource source, string name, string command, string executable)
    {
        string displayName = Path.GetFileNameWithoutExtension(name), publisher = "";
        if (executable.Length > 0 && File.Exists(executable))
        {
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
                if (!string.IsNullOrWhiteSpace(version.FileDescription)) displayName = version.FileDescription.Trim();
                publisher = (version.CompanyName ?? "").Trim();
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
            {
            }
        }
        (bool enabled, DateTime? disabledAt) = ReadApproved(source, name);
        return new StartupApp(source, name, command, File.Exists(executable) ? executable : "", displayName, publisher, enabled, disabledAt);
    }

    /// <summary>No flag means enabled; an odd first byte (3, 7) means turned off, with the time in bytes 4–11.</summary>
    private static (bool Enabled, DateTime? DisabledAt) ReadApproved(StartupSource source, string name)
    {
        try
        {
            (RegistryHive hive, string subKey) = ApprovedLocation(source);
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(subKey);
            if (key?.GetValue(name) is not byte[] { Length: > 0 } value || (value[0] & 1) == 0) return (true, null);
            long fileTime = value.Length >= 12 ? BitConverter.ToInt64(value, 4) : 0;
            return (false, fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime).ToLocalTime() : null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentOutOfRangeException)
        {
            return (true, null);
        }
    }

    private static (RegistryHive Hive, string SubKey) ApprovedLocation(StartupSource source) => source switch
    {
        StartupSource.UserRun => (RegistryHive.CurrentUser, ApprovedKey + "Run"),
        StartupSource.MachineRun => (RegistryHive.LocalMachine, ApprovedKey + "Run"),
        StartupSource.MachineRun32 => (RegistryHive.LocalMachine, ApprovedKey + "Run32"),
        StartupSource.UserFolder => (RegistryHive.CurrentUser, ApprovedKey + "StartupFolder"),
        _ => (RegistryHive.LocalMachine, ApprovedKey + "StartupFolder")
    };

    /// <summary>The program part of a command line: quoted, or up to the first ".exe", or the first word.</summary>
    internal static string ExecutableFromCommand(string command)
    {
        string text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : "";
        }
        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return text[..(exe + 4)];
        int space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}

/// <summary>Reads a .lnk file's target and arguments through the shell's own IShellLink.</summary>
internal static class ShellLinks
{
    private const uint SlgpRawPath = 0x4;

    public static bool TryResolve(string linkPath, out string target, out string arguments)
    {
        target = arguments = "";
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(linkPath, 0);
            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            if (buffer.Length == 0)
            {
                // Shortcuts that only carry an environment-variable target (0install, some installers) need the raw path.
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, SlgpRawPath);
            }
            target = Environment.ExpandEnvironmentVariables(buffer.ToString());
            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            arguments = buffer.ToString();
            return target.Length > 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
