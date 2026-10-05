using System.Globalization;
using System.Text;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.HistoryService;

/// <summary>
/// Writes what holds memory shortly after Windows starts: the in-use breakdown, the processes and vendors with the most
/// private memory, and kernel pool usage per tag with the drivers that use each tag. One file per boot; the previous
/// boot's report is kept next to it.
/// </summary>
public sealed class BootMemoryReport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly string path;
    private readonly Queue<TimeSpan> due;
    private readonly Action<string> log;
    private readonly object gate = new();
    private Dictionary<string, List<string>>? drivers;
    private bool started;

    /// <param name="atUptimes">System uptimes at which to take a snapshot.</param>
    public BootMemoryReport(string path, IEnumerable<TimeSpan> atUptimes, Action<string> log)
    {
        this.path = path;
        this.log = log;
        due = new Queue<TimeSpan>(atUptimes.OrderBy(time => time));
    }

    /// <summary>
    /// A report for the current boot, or null when the service started long after Windows did (for example after an
    /// update), because then the memory no longer reflects startup.
    /// </summary>
    public static BootMemoryReport? ForThisBoot(string path, Action<string> log)
    {
        if (TimeSpan.FromMilliseconds(Environment.TickCount64) > TimeSpan.FromMinutes(10)) return null;
        return new BootMemoryReport(path, [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)], log);
    }

    /// <summary>Called with every snapshot; writes in the background when the next uptime mark has passed.</summary>
    public void Offer(MonitorSnapshot snapshot)
    {
        if (due.Count == 0 || snapshot.System.Uptime < due.Peek()) return;
        due.Dequeue();
        _ = Task.Run(() =>
        {
            try
            {
                Write(snapshot);
            }
            catch (Exception ex)
            {
                log("Boot memory report failed: " + ex.Message);
            }
        });
    }

    private void Write(MonitorSnapshot snapshot)
    {
        string text = Format(snapshot);
        lock (gate)
        {
            if (!started)
            {
                started = true;
                if (File.Exists(path)) File.Move(path, Path.ChangeExtension(path, ".previous.txt"), overwrite: true);
            }
            File.AppendAllText(path, text, Encoding.UTF8);
        }
    }

    private string Format(MonitorSnapshot snapshot)
    {
        static string Gb(long bytes) => (bytes / 1073741824d).ToString("0.00", Invariant).PadLeft(6) + " GB";
        static string Mb(long bytes) => (bytes / 1048576d).ToString("0", Invariant).PadLeft(6) + " MB";
        static string Vendor(ProcessSample process) =>
            !string.IsNullOrWhiteSpace(process.Company) ? process.Company : process.Path.Length == 0 ? "(system or protected)" : "(no publisher)";

        MemoryBreakdown memory = snapshot.System.Memory;
        var text = new StringBuilder();
        DateTime bootTime = DateTime.Now - snapshot.System.Uptime;
        text.AppendLine($"===== Boot {bootTime.ToString("yyyy-MM-dd HH:mm", Invariant)}, snapshot at uptime {snapshot.System.Uptime.ToString(@"hh\:mm\:ss", Invariant)} =====");
        text.AppendLine($"Total {Gb(memory.Total)}   In use {Gb(memory.InUse)} ({memory.LoadPercent.ToString("0", Invariant)}%)   Available {Gb(memory.Available)}");
        text.AppendLine($"  apps private     {Gb(memory.ProcessPrivate)}");
        text.AppendLine($"  kernel pools     {Gb(memory.KernelPools)}");
        text.AppendLine($"  file cache       {Gb(memory.FileCache)}");
        text.AppendLine($"  driver code      {Gb(memory.Drivers)}");
        text.AppendLine($"  shared / other   {Gb(memory.Unattributed)}");
        text.AppendLine($"Modified {Gb(memory.Modified)}   Standby {Gb(memory.Standby)}   Free {Gb(memory.Free)}   Committed {Gb(memory.CommitTotal)} of {Gb(memory.CommitLimit)}");

        text.AppendLine();
        text.AppendLine("Private memory by vendor (service = session 0, user = signed-in session):");
        foreach (var group in snapshot.Processes
                     .GroupBy(process => (Vendor: Vendor(process), Kind: process.SessionId == 0 ? "service" : "user"))
                     .Select(group => (group.Key, Bytes: group.Sum(process => process.PrivateWorkingSet), Count: group.Count()))
                     .OrderByDescending(group => group.Bytes).Take(15))
        {
            text.AppendLine($"  {Mb(group.Bytes)}  {group.Key.Kind,-7} {group.Count,3}x  {group.Key.Vendor}");
        }

        text.AppendLine();
        text.AppendLine("Processes with the most private memory:");
        foreach (ProcessSample process in snapshot.Processes.OrderByDescending(process => process.PrivateWorkingSet).Take(25))
        {
            text.AppendLine($"  {Mb(process.PrivateWorkingSet)}  {(process.SessionId == 0 ? "service" : "user"),-7} {process.ImageName} ({process.Pid})  {Vendor(process)}");
        }

        List<PoolTagUsage> tags = PoolTags.Read().OrderByDescending(tag => tag.TotalBytes).Take(15).ToList();
        drivers ??= new Dictionary<string, List<string>>();
        List<string> unknown = tags.Select(tag => tag.Tag).Where(tag => !drivers.ContainsKey(tag)).ToList();
        if (unknown.Count > 0)
        {
            foreach (var (tag, files) in PoolTags.FindDrivers(unknown)) drivers[tag] = files;
        }
        text.AppendLine();
        text.AppendLine("Kernel pool by tag (driver files that contain the tag; none listed = Windows kernel itself):");
        foreach (PoolTagUsage tag in tags)
        {
            string owners = drivers.TryGetValue(tag.Tag, out List<string>? files) && files.Count > 0 ? string.Join(", ", files.Take(4)) : "";
            text.AppendLine($"  '{tag.Tag}'  paged {Mb(tag.PagedBytes)}  nonpaged {Mb(tag.NonPagedBytes)}  {owners}");
        }
        text.AppendLine();
        return text.ToString();
    }
}
