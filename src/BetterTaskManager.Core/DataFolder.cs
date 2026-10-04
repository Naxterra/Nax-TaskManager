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

    /// <summary>
    /// Creates the folder with its own ACL: ProgramData lets every user create files in subfolders, so the inherited
    /// rights are replaced by SYSTEM and Administrators full control and Users read-only. Needs administrator rights.
    /// </summary>
    public static void EnsureSecured()
    {
        var directory = new DirectoryInfo(Path);
        directory.Create();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
