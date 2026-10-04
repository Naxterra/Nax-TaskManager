using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using BetterTaskManager.Core.History;

namespace BetterTaskManager.HistoryService;

public sealed class HistoryWindowsService : ServiceBase
{
    private const long MaxLogBytes = 1024 * 1024;
    private HistoryWorker? worker;

    public HistoryWindowsService()
    {
        ServiceName = HistoryServiceControl.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        SecureDataFolder(HistoryStore.DataFolder);
        worker = new HistoryWorker(HistoryStore.DefaultPath, "NaxTaskManager-History", Log, publishFeed: true);
        worker.Start();
        Log("Service started");
    }

    protected override void OnStop()
    {
        worker?.Dispose();
        worker = null;
        Log("Service stopped");
    }

    protected override void OnShutdown() => OnStop();

    /// <summary>
    /// ProgramData lets every user create files in subfolders. The history folder gets its own ACL instead: SYSTEM and
    /// administrators may change it, users may only read, so nobody can plant files the service would open.
    /// </summary>
    private static void SecureDataFolder(string folder)
    {
        var directory = new DirectoryInfo(folder);
        directory.Create();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static void Log(string message)
    {
        try
        {
            string path = Path.Combine(HistoryStore.DataFolder, "service.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes) File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
