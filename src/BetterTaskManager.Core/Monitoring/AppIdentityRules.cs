using System.Text.RegularExpressions;

namespace BetterTaskManager.Core.Monitoring;

/// <summary>
/// Which "app" a process belongs to, for history and network grouping. Modelled on Portmaster's profiles: service
/// hosts are identified by their service, Windows Store apps by package name and publisher (their install folder
/// changes with every update), everything else by executable path with version numbers ignored.
/// </summary>
public static partial class AppIdentityRules
{
    public const string SystemKey = "system";
    public const string SystemName = "Operating System";

    /// <summary>Per-user services carry a random suffix ("…User Service_1bf5729"); one name per service is clearer.</summary>
    public static string ServiceName(string displayName) => PerUserServiceSuffix().Replace(displayName, "");

    public static bool IsServiceHost(ProcessSample process) =>
        process.ImageName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stable key across restarts and updates.</summary>
    public static string AppKey(ProcessSample process)
    {
        if (process.Pid == 4) return SystemKey;
        if (IsServiceHost(process) && process.Services is [string service, ..]) return "svchost:" + ServiceName(service);
        return PathKey(process.Path) ?? process.ImageName.ToLowerInvariant();
    }

    /// <summary>The name shown for the app.</summary>
    public static string AppName(ProcessSample process)
    {
        if (process.Pid == 4) return SystemName;
        if (process.Services is [string service, ..])
        {
            if (IsServiceHost(process)) return "Service Host: " + ServiceName(service);
            // A windowless program that runs exactly one service: its service name says more than its file
            // description. Hosts of several services (lsass.exe) keep their own name.
            if (process.WindowTitle is null && process.Services.Count == 1) return ServiceName(service);
        }
        return process.DisplayName;
    }

    /// <summary>Key for an executable path, or null when the path is unknown.</summary>
    public static string? PathKey(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (TryStorePackage(path, out string package, out string publisher)) return $"store:{package}__{publisher}".ToLowerInvariant();
        return VersionNumber().Replace(path.ToLowerInvariant(), "*");
    }

    /// <summary>
    /// Store apps live in "…\WindowsApps\Name_Version_Architecture_ResourceId_PublisherId\…"; the name and publisher
    /// identify the app across updates.
    /// </summary>
    public static bool TryStorePackage(string path, out string package, out string publisher)
    {
        package = publisher = "";
        int marker = path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;
        string rest = path[(marker + @"\WindowsApps\".Length)..];
        int end = rest.IndexOf('\\');
        string folder = end < 0 ? rest : rest[..end];
        string[] parts = folder.Split('_');
        if (parts.Length != 5 || parts[0].Length == 0 || parts[4].Length == 0) return false;
        package = parts[0];
        publisher = parts[4];
        return true;
    }

    /// <summary>The current key for a key stored by an older version (history migration).</summary>
    public static string Rekey(string storedKey, string path)
    {
        if (storedKey.StartsWith("svchost:", StringComparison.Ordinal)) return "svchost:" + ServiceName(storedKey["svchost:".Length..]);
        if (storedKey.StartsWith("pid:", StringComparison.Ordinal) || storedKey == SystemKey) return storedKey;
        return PathKey(path) ?? storedKey;
    }

    [GeneratedRegex(@"_[0-9a-f]{4,8}$", RegexOptions.IgnoreCase)]
    private static partial Regex PerUserServiceSuffix();

    /// <summary>Version numbers in install paths (…\app-4.0.824\…) change with every update.</summary>
    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionNumber();
}
