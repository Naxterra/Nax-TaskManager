using System.Globalization;
using BetterTaskManager.Core.Disk;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

public sealed record DiskProcessRow(int Pid, long CreateTime, string Name, string Path, double Read, double Write)
{
    public double Total => Read + Write;
}

public sealed record DiskFileRow(int Pid, string Name, string ProcessPath, string File, double Read, double Write, double Response, string Priority)
{
    public double Total => Read + Write;
}

/// <summary>
/// The Disk page, modelled on Resource Monitor's Disk tab: the physical disks, then either the processes or the files
/// with disk traffic. Process and file rates are averages over the last minute, like Resource Monitor's B/s columns.
/// </summary>
public sealed class DiskViewModel : ObservableObject
{
    public const string SortName = "Name";
    public const string SortPid = "Pid";
    public const string SortRead = "Read";
    public const string SortWrite = "Write";
    public const string SortTotal = "Total";
    public const string SortFile = "File";
    public const string SortResponse = "Response";
    public const string SortPriority = "Priority";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly MonitorHost monitor;
    private readonly Dictionary<(int Pid, long CreateTime), Queue<(DateTime Time, double Read, double Write)>> processBytes = new();
    /// <summary>
    /// Name and path of every process seen with disk traffic. The rates are a one-minute average, so a process can
    /// stay in the list for up to a minute after it exited, when it is no longer in the snapshot.
    /// </summary>
    private readonly Dictionary<(int Pid, long CreateTime), (string Name, string Path)> identities = new();
    private DateTime lastSnapshot;
    private DateTime firstSnapshot;
    private string summary = "", notice = "";
    private Visibility noticeVisibility = Visibility.Collapsed, restartVisibility = Visibility.Collapsed;

    public DiskViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        StorageLayout = new ColumnLayout("DiskStorage.", new Dictionary<string, double>
        {
            ["Name"] = 300, ["Kind"] = 120, ["Active"] = 100, ["Read"] = 110, ["Write"] = 110, ["Response"] = 110, ["Queue"] = 120
        }, settings.ColumnWidths, ["Kind", "Active", "Read", "Write", "Response", "Queue"], settings.ColumnOrder);
        ProcessLayout = new ColumnLayout("DiskProcesses.", new Dictionary<string, double>
        {
            ["Name"] = 300, ["Pid"] = 80, ["Read"] = 120, ["Write"] = 120, ["Total"] = 120
        }, settings.ColumnWidths, ["Pid", "Read", "Write", "Total"], settings.ColumnOrder);
        FileLayout = new ColumnLayout("DiskFiles.", new Dictionary<string, double>
        {
            ["Name"] = 220, ["Pid"] = 80, ["File"] = 520, ["Read"] = 110, ["Write"] = 110, ["Total"] = 110, ["Priority"] = 100, ["Response"] = 110
        }, settings.ColumnWidths, ["Pid", "File", "Read", "Write", "Total", "Priority", "Response"], settings.ColumnOrder);
        DiskSlot.SharedLayout = StorageLayout;
        DiskProcessSlot.SharedLayout = ProcessLayout;
        DiskFileSlot.SharedLayout = FileLayout;
        Disks = new SlotCollection<DiskSlot, DiskSample>(DiskSlot.Load);
        Processes = new SlotCollection<DiskProcessSlot, DiskProcessRow>(DiskProcessSlot.Load);
        Files = new SlotCollection<DiskFileSlot, DiskFileRow>(DiskFileSlot.Load);
    }

    public ColumnLayout StorageLayout { get; }
    public ColumnLayout ProcessLayout { get; }
    public ColumnLayout FileLayout { get; }
    public SlotCollection<DiskSlot, DiskSample> Disks { get; }
    public SlotCollection<DiskProcessSlot, DiskProcessRow> Processes { get; }
    public SlotCollection<DiskFileSlot, DiskFileRow> Files { get; }

    public string Summary { get => summary; private set => Set(ref summary, value); }
    /// <summary>Why the current list is empty or incomplete (missing rights), shown above it.</summary>
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public Visibility NoticeVisibility { get => noticeVisibility; private set => Set(ref noticeVisibility, value); }
    public Visibility RestartVisibility { get => restartVisibility; private set => Set(ref restartVisibility, value); }

    public bool FilesMode { get; set; }
    public string ProcessSortColumn { get; private set; } = SortTotal;
    public bool ProcessSortDescending { get; private set; } = true;
    public string FileSortColumn { get; private set; } = SortTotal;
    public bool FileSortDescending { get; private set; } = true;

    public void Sort(string column)
    {
        bool natural = column is not (SortName or SortFile or SortPid);
        if (FilesMode)
        {
            FileSortDescending = FileSortColumn == column ? !FileSortDescending : natural;
            FileSortColumn = column;
        }
        else
        {
            ProcessSortDescending = ProcessSortColumn == column ? !ProcessSortDescending : natural;
            ProcessSortColumn = column;
        }
        Refresh();
    }

    public void Refresh()
    {
        if (monitor.Latest is not { } snapshot) return;
        Accumulate(snapshot);
        string query = monitor.SearchText.Trim();

        // Card readers without a card report a little "active time" but have no volume and no traffic: leave them out.
        List<DiskSample> disks = snapshot.System.Disks
            .Where(disk => disk.Volumes.Any(volume => volume.Total > 0) || disk.ReadPerSecond + disk.WritePerSecond > 0).ToList();
        Disks.Apply(disks);
        Summary = Loc.F("Disk_Summary", Format.Count(disks.Count), Format.ByteRate(disks.Sum(disk => disk.ReadPerSecond)),
            Format.ByteRate(disks.Sum(disk => disk.WritePerSecond)));

        if (FilesMode) RefreshFiles(snapshot, query);
        else RefreshProcesses(snapshot, query);
    }

    /// <summary>Adds the newest snapshot's bytes to each process's one-minute window (once per snapshot).</summary>
    private void Accumulate(MonitorSnapshot snapshot)
    {
        if (snapshot.Timestamp == lastSnapshot) return;
        double seconds = lastSnapshot == default ? 1 : Math.Clamp((snapshot.Timestamp - lastSnapshot).TotalSeconds, 0.1, 5);
        if (firstSnapshot == default) firstSnapshot = snapshot.Timestamp;
        lastSnapshot = snapshot.Timestamp;
        foreach (ProcessSample process in snapshot.Processes)
        {
            if (process.DiskBytesPerSecond <= 0) continue;
            if (!processBytes.TryGetValue(process.Key, out var queue)) processBytes[process.Key] = queue = new();
            queue.Enqueue((snapshot.Timestamp, process.DiskReadBytesPerSecond * seconds, process.DiskWriteBytesPerSecond * seconds));
            identities[process.Key] = (process.ImageName, process.Path);
        }
        foreach (var (key, queue) in processBytes.ToList())
        {
            while (queue.Count > 0 && snapshot.Timestamp - queue.Peek().Time > Window) queue.Dequeue();
            if (queue.Count == 0)
            {
                processBytes.Remove(key);
                identities.Remove(key);
            }
        }
    }

    private void RefreshProcesses(MonitorSnapshot snapshot, string query)
    {
        bool available = snapshot.System.PerProcessDiskAvailable;
        ShowNotice(available ? "" : Loc.Get("Disk_ProcessNotice"), restart: !available && !monitor.IsElevated);
        // Until a full minute has been seen, average over the time observed so far.
        double window = Math.Clamp((lastSnapshot - firstSnapshot).TotalSeconds + 1, 1, Window.TotalSeconds);
        var processes = snapshot.Processes.ToDictionary(process => process.Key);
        var rows = new List<DiskProcessRow>();
        foreach (var (key, queue) in processBytes)
        {
            identities.TryGetValue(key, out var known);
            bool alive = processes.ContainsKey(key);
            string name = known.Name is { Length: > 0 } ? known.Name : "PID " + key.Pid;
            if (!alive) name = Loc.F("Disk_Exited", name);
            var row = new DiskProcessRow(key.Pid, key.CreateTime, name, known.Path ?? "",
                queue.Sum(sample => sample.Read) / window, queue.Sum(sample => sample.Write) / window);
            if (Matches(query, row.Pid, row.Name, row.Path)) rows.Add(row);
        }
        Func<DiskProcessRow, object> sortKey = ProcessSortColumn switch
        {
            SortPid => row => row.Pid,
            SortRead => row => row.Read,
            SortWrite => row => row.Write,
            SortTotal => row => row.Total,
            _ => row => row.Name
        };
        Processes.Apply(Order(rows, sortKey, ProcessSortDescending, row => row.Name).ToList());
    }

    private void RefreshFiles(MonitorSnapshot snapshot, string query)
    {
        if (!monitor.IsElevated)
        {
            ShowNotice(Loc.Get("Disk_FileNotice"), restart: true);
            Files.Apply([]);
            return;
        }
        ShowNotice(monitor.DiskFiles.IsRunning ? "" : Loc.Get("Disk_FileStarting") + " " + monitor.DiskFiles.Status, restart: false);
        var processes = new Dictionary<int, ProcessSample>();
        foreach (ProcessSample process in snapshot.Processes) processes[process.Pid] = process;
        var rows = new List<DiskFileRow>();
        foreach (FileActivity activity in monitor.DiskFiles.Read())
        {
            string name, path;
            if (activity.Pid == 4) (name, path) = ("System", "");
            else if (processes.TryGetValue(activity.Pid, out ProcessSample? process)) (name, path) = (process.ImageName, process.Path);
            else
            {
                // Exited: the name from the disk list, or from the trace's own process events for processes that
                // lived too briefly to appear in any snapshot.
                var known = identities.FirstOrDefault(pair => pair.Key.Pid == activity.Pid).Value;
                string traced = monitor.DiskFiles.ProcessName(activity.Pid);
                string label = known.Name is { Length: > 0 } ? known.Name : traced.Length > 0 ? traced : activity.Pid > 0 ? "PID " + activity.Pid : "–";
                (name, path) = (activity.Pid > 0 ? Loc.F("Disk_Exited", label) : label, known.Path ?? "");
            }
            var row = new DiskFileRow(activity.Pid, name, path, activity.File, activity.ReadPerSecond, activity.WritePerSecond,
                activity.ResponseMilliseconds, activity.Priority);
            if (Matches(query, row.Pid, row.Name, row.File)) rows.Add(row);
        }
        Func<DiskFileRow, object> sortKey = FileSortColumn switch
        {
            SortPid => row => row.Pid,
            SortFile => row => row.File,
            SortRead => row => row.Read,
            SortWrite => row => row.Write,
            SortTotal => row => row.Total,
            SortResponse => row => row.Response,
            SortPriority => row => row.Priority,
            _ => row => row.Name
        };
        Files.Apply(Order(rows, sortKey, FileSortDescending, row => row.File).ToList());
    }

    private void ShowNotice(string text, bool restart)
    {
        Notice = text;
        NoticeVisibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestartVisibility = restart ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(string query, int pid, string name, string path) =>
        query.Length == 0 ||
        name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        path.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        pid.ToString(CultureInfo.InvariantCulture) == query;

    private static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, object> key, bool descending, Func<T, string> tieBreaker)
    {
        IComparer<object> comparer = Comparer<object>.Create((left, right) => left switch
        {
            string text => StringComparer.CurrentCultureIgnoreCase.Compare(text, right as string),
            _ => Comparer<object>.Default.Compare(left, right)
        });
        IOrderedEnumerable<T> ordered = descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
        return ordered.ThenBy(tieBreaker, StringComparer.CurrentCultureIgnoreCase);
    }

    public static string PriorityName(string priority) => priority switch
    {
        "VeryLow" => Loc.Get("IoPriority_VeryLow"),
        "Low" => Loc.Get("IoPriority_Low"),
        "Normal" => Loc.Get("IoPriority_Normal"),
        "High" => Loc.Get("IoPriority_High"),
        "Critical" => Loc.Get("IoPriority_Critical"),
        _ => priority
    };
}

/// <summary>A row of the disks table.</summary>
public sealed class DiskSlot : ObservableObject
{
    private string name = "", model = "", kind = "", activeText = "", readText = "", writeText = "", responseText = "", queueText = "", spaceText = "";
    private Brush activeHeat = Heat.Level(0);

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;
    public override string ToString() => Name;

    public string Name { get => name; private set => Set(ref name, value); }
    public string Model { get => model; private set => Set(ref model, value); }
    public string Kind { get => kind; private set => Set(ref kind, value); }
    public string ActiveText { get => activeText; private set => Set(ref activeText, value); }
    public string ReadText { get => readText; private set => Set(ref readText, value); }
    public string WriteText { get => writeText; private set => Set(ref writeText, value); }
    public string ResponseText { get => responseText; private set => Set(ref responseText, value); }
    public string QueueText { get => queueText; private set => Set(ref queueText, value); }
    public string SpaceText { get => spaceText; private set => Set(ref spaceText, value); }
    public Brush ActiveHeat { get => activeHeat; private set => Set(ref activeHeat, value); }

    public static void Load(DiskSlot slot, DiskSample disk)
    {
        string letters = string.Join(" ", disk.Volumes.Select(volume => volume.Letter));
        slot.Name = letters.Length > 0 ? Loc.F("Disk_Name", disk.Number, letters) : Loc.F("Disk_NameNoLetter", disk.Number);
        slot.Model = disk.Model;
        slot.Kind = disk.Kind;
        slot.ActiveText = Format.Percent(disk.ActivePercent);
        slot.ActiveHeat = Heat.Level(Heat.Scale(disk.ActivePercent, 1, 5, 15, 30, 60, 90));
        slot.ReadText = Format.ByteRate(disk.ReadPerSecond);
        slot.WriteText = Format.ByteRate(disk.WritePerSecond);
        slot.ResponseText = disk.ResponseMilliseconds.ToString("0.0", CultureInfo.CurrentCulture) + " ms";
        slot.QueueText = disk.QueueLength.ToString("0", CultureInfo.CurrentCulture);
        slot.SpaceText = string.Join("   ", disk.Volumes.Where(volume => volume.Total > 0).Select(volume =>
            (volume.Label.Length > 0 ? $"{volume.Letter} {volume.Label}: " : volume.Letter + " ") +
            Loc.F("Disk_Free", Format.Gigabytes(volume.Free), Format.Gigabytes(volume.Total))));
    }
}

/// <summary>A row of the processes-with-disk-activity list.</summary>
public sealed class DiskProcessSlot : ObservableObject
{
    private string name = "", pidText = "", readText = "", writeText = "", totalText = "", path = "";
    private ImageSource? icon;
    private Brush totalHeat = Heat.Level(0);

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;
    public DiskProcessRow? Row { get; private set; }
    public override string ToString() => $"{Name} {PidText}";

    public string Name { get => name; private set => Set(ref name, value); }
    public string PidText { get => pidText; private set => Set(ref pidText, value); }
    public string ReadText { get => readText; private set => Set(ref readText, value); }
    public string WriteText { get => writeText; private set => Set(ref writeText, value); }
    public string TotalText { get => totalText; private set => Set(ref totalText, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public Brush TotalHeat { get => totalHeat; private set => Set(ref totalHeat, value); }

    public static void Load(DiskProcessSlot slot, DiskProcessRow row)
    {
        slot.Row = row;
        slot.Name = row.Name;
        slot.PidText = row.Pid.ToString(CultureInfo.CurrentCulture);
        slot.ReadText = Format.ByteRate(row.Read);
        slot.WriteText = Format.ByteRate(row.Write);
        slot.TotalText = Format.ByteRate(row.Total);
        slot.TotalHeat = Heat.Level(Heat.Scale(row.Total / 1048576d, 0.1, 1, 5, 20, 50, 100));
        slot.Path = row.Path;
        slot.Icon = IconCache.Get(row.Path);
    }
}

/// <summary>A row of the per-file disk activity list.</summary>
public sealed class DiskFileSlot : ObservableObject
{
    private string name = "", pidText = "", file = "", readText = "", writeText = "", totalText = "", priority = "", responseText = "";
    private ImageSource? icon;
    private Brush totalHeat = Heat.Level(0);

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;
    public DiskFileRow? Row { get; private set; }
    public override string ToString() => $"{Name} {File}";

    public string Name { get => name; private set => Set(ref name, value); }
    public string PidText { get => pidText; private set => Set(ref pidText, value); }
    public string File { get => file; private set => Set(ref file, value); }
    public string ReadText { get => readText; private set => Set(ref readText, value); }
    public string WriteText { get => writeText; private set => Set(ref writeText, value); }
    public string TotalText { get => totalText; private set => Set(ref totalText, value); }
    public string Priority { get => priority; private set => Set(ref priority, value); }
    public string ResponseText { get => responseText; private set => Set(ref responseText, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public Brush TotalHeat { get => totalHeat; private set => Set(ref totalHeat, value); }

    public static void Load(DiskFileSlot slot, DiskFileRow row)
    {
        slot.Row = row;
        slot.Name = row.Name;
        slot.PidText = row.Pid > 0 ? row.Pid.ToString(CultureInfo.CurrentCulture) : "–";
        slot.File = row.File.Length > 0 ? row.File : "–";
        slot.ReadText = Format.ByteRate(row.Read);
        slot.WriteText = Format.ByteRate(row.Write);
        slot.TotalText = Format.ByteRate(row.Total);
        slot.TotalHeat = Heat.Level(Heat.Scale(row.Total / 1048576d, 0.1, 1, 5, 20, 50, 100));
        slot.Priority = DiskViewModel.PriorityName(row.Priority);
        slot.ResponseText = row.Response.ToString("0.0", CultureInfo.CurrentCulture) + " ms";
        slot.Icon = IconCache.Get(row.ProcessPath);
    }
}
