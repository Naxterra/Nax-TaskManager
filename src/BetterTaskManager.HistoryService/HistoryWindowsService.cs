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
        BetterTaskManager.Core.DataFolder.EnsureSecured();
        BootMemoryReport? bootReport = BootMemoryReport.ForThisBoot(Path.Combine(HistoryStore.DataFolder, "boot-memory.txt"), Log);
        worker = new HistoryWorker(HistoryStore.DefaultPath, "NaxTaskManager-History", Log, publishFeed: true, bootReport, trimCache: true);
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
