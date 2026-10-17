using System.Security.AccessControl;
using System.Security.Principal;

namespace BetterTaskManager.Core;

/// <summary>
/// The machine-wide data folder (history database, block list mirror). Only elevated code writes here; everybody
/// may read.
/// </summary>
public static class DataFolder
{
    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NaxTaskManager");

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    // NT SERVICE\TrustedInstaller
    private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    /// <summary>
    /// Creates the folder with its own owner and ACL: ProgramData lets every user create subfolders, so the inherited
    /// rights are replaced by SYSTEM and Administrators full control and Users read-only, and the owner is set to
    /// Administrators (an owner can always change the ACL again). A folder that is a link (junction or symlink, which
    /// would redirect what the LocalSystem service writes) or that someone else owns was not made by this app: it is
    /// moved aside, or for a link only the link is removed, and a fresh folder is created. Needs administrator rights.
    /// </summary>
    public static void EnsureSecured() => EnsureSecured(Path);

    /// <summary>The same for another folder (tests use a scratch folder instead of the real data folder).</summary>
    public static void EnsureSecured(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists && !IsTrusted(directory))
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(path, recursive: false);
            else directory.MoveTo(path + ".untrusted-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            directory = new DirectoryInfo(path);
        }
        directory.Create();

        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    /// <summary>A real folder owned by SYSTEM, Administrators or TrustedInstaller, as this app creates it.</summary>
    private static bool IsTrusted(DirectoryInfo directory)
    {
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        try
        {
            IdentityReference? owner = directory.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            return owner is SecurityIdentifier sid && (sid == LocalSystem || sid == Administrators || sid == TrustedInstaller);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
