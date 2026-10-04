using System.Globalization;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

/// <summary>Task Manager's Details tab: one row per process, no grouping.</summary>
public sealed class DetailsViewModel : ObservableObject
{
    public const string SortName = "Name";
    public const string SortPid = "Pid";
    public const string SortStatus = "Status";
    public const string SortUser = "User";
    public const string SortCpu = "Cpu";
    public const string SortMemory = "Memory";
    public const string SortPriority = "Priority";
    public const string SortDescription = "Description";

    private readonly MonitorHost monitor;
    private readonly AppSettings settings;
    private string summary = "";

    public DetailsViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        Layout = new ColumnLayout("Details.", new Dictionary<string, double>
        {
            ["Name"] = 260, ["Pid"] = 80, ["Status"] = 150, ["User"] = 150, ["Cpu"] = 80, ["Memory"] = 110, ["Priority"] = 150
        }, settings.ColumnWidths);
        DetailSlot.SharedLayout = Layout;
        Rows = new SlotCollection<DetailSlot, (ProcessSample, long)>(DetailSlot.Load);
    }

    public ColumnLayout Layout { get; }
    public SlotCollection<DetailSlot, (ProcessSample Process, long TotalMemory)> Rows { get; }
    public string Summary { get => summary; private set => Set(ref summary, value); }

    public string SortColumn => settings.DetailsSortColumn;
    public bool SortDescending => settings.DetailsSortDescending;

    public void Refresh()
    {
        if (monitor.Latest is not { } snapshot) return;
        string query = monitor.SearchText.Trim();
        IEnumerable<ProcessSample> visible = snapshot.Processes.Where(process => process.Pid != 0 && Matches(process, query));
        List<ProcessSample> rows = Sort(visible, SortColumn, SortDescending).ToList();
        long total = snapshot.System.Memory.Total;
        Rows.Apply(rows.Select(process => (process, total)).ToList());
        Summary = Loc.F("Details_Summary", Format.Count(rows.Count), Format.Count(snapshot.Processes.Count(process => process.Efficiency)),
            Format.Count(snapshot.Processes.Count(process => process.Suspended)));
    }

    public void Sort(string column)
    {
        if (settings.DetailsSortColumn == column) settings.DetailsSortDescending = !settings.DetailsSortDescending;
        else
        {
            settings.DetailsSortColumn = column;
            settings.DetailsSortDescending = column is SortCpu or SortMemory or SortPriority;
        }
        Raise(nameof(SortColumn));
        Refresh();
    }

    private static IEnumerable<ProcessSample> Sort(IEnumerable<ProcessSample> processes, string column, bool descending)
    {
        IComparer<string> text = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<ProcessSample> ordered = column switch
        {
            SortPid => Order(processes, process => process.Pid, descending),
            SortStatus => Order(processes, process => StatusRank(process), descending),
            SortUser => descending ? processes.OrderByDescending(process => process.UserName, text) : processes.OrderBy(process => process.UserName, text),
            SortCpu => Order(processes, process => process.CpuPercent, descending),
            SortMemory => Order(processes, process => process.PrivateWorkingSet, descending),
            SortPriority => Order(processes, process => PriorityRank(process.Priority), descending),
            SortDescription => descending ? processes.OrderByDescending(process => process.Description, text) : processes.OrderBy(process => process.Description, text),
            _ => descending ? processes.OrderByDescending(process => process.ImageName, text) : processes.OrderBy(process => process.ImageName, text)
        };
        return ordered.ThenBy(process => process.ImageName, text).ThenBy(process => process.Pid);
    }

    private static IOrderedEnumerable<ProcessSample> Order<T>(IEnumerable<ProcessSample> processes, Func<ProcessSample, T> key, bool descending) =>
        descending ? processes.OrderByDescending(key) : processes.OrderBy(key);

    private static int StatusRank(ProcessSample process) => process.Suspended ? 2 : process.Efficiency ? 1 : 0;

    /// <summary>Unknown sorts below Idle.</summary>
    internal static int PriorityRank(PriorityClass priority) => priority switch
    {
        PriorityClass.Idle => 1,
        PriorityClass.BelowNormal => 2,
        PriorityClass.Normal => 3,
        PriorityClass.AboveNormal => 4,
        PriorityClass.High => 5,
        PriorityClass.Realtime => 6,
        _ => 0
    };

    private static bool Matches(ProcessSample process, string query) =>
        query.Length == 0 ||
        process.ImageName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        process.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        process.UserName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        process.Pid.ToString(CultureInfo.InvariantCulture) == query;
}

/// <summary>A reusable row of the Details table.</summary>
public sealed class DetailSlot : ObservableObject
{
    private string name = "", pidText = "", status = "", user = "", cpuText = "", memoryText = "", priorityText = "", description = "", path = "";
    private ImageSource? icon;
    private Brush cpuHeat = Heat.Level(0), memoryHeat = Heat.Level(0);

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;

    public ProcessSample? Process { get; private set; }
    public (int Pid, long CreateTime) Key => Process?.Key ?? default;

    /// <summary>What UI Automation and screen readers announce for the row.</summary>
    public override string ToString() => $"{Name} {PidText}";

    public string Name { get => name; private set => Set(ref name, value); }
    public string PidText { get => pidText; private set => Set(ref pidText, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string User { get => user; private set => Set(ref user, value); }
    public string CpuText { get => cpuText; private set => Set(ref cpuText, value); }
    public string MemoryText { get => memoryText; private set => Set(ref memoryText, value); }
    public string PriorityText { get => priorityText; private set => Set(ref priorityText, value); }
    public string Description { get => description; private set => Set(ref description, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public Brush CpuHeat { get => cpuHeat; private set => Set(ref cpuHeat, value); }
    public Brush MemoryHeat { get => memoryHeat; private set => Set(ref memoryHeat, value); }

    public static void Load(DetailSlot slot, (ProcessSample Process, long TotalMemory) input)
    {
        ProcessSample process = input.Process;
        slot.Process = process;
        slot.Name = process.ImageName;
        slot.PidText = process.Pid.ToString(CultureInfo.CurrentCulture);
        slot.Status = process.Suspended ? Loc.Get("ProcState_Suspended") : process.Efficiency ? Loc.Get("Proc_Efficiency") : Loc.Get("ProcState_Running");
        slot.User = process.UserName.Length > 0 ? process.UserName : "–";
        slot.CpuText = process.CpuSampled ? Format.Percent(process.CpuPercent) : "…";
        slot.MemoryText = Format.Memory(process.PrivateWorkingSet);
        slot.PriorityText = PriorityName(process.Priority);
        slot.Description = process.Description;
        slot.Path = process.Path;
        slot.Icon = IconCache.Get(process.Path);
        slot.CpuHeat = Heat.Level(Heat.Scale(process.CpuPercent, 0.5, 2, 5, 12, 25, 50));
        double memoryShare = input.TotalMemory == 0 ? 0 : process.PrivateWorkingSet * 100d / input.TotalMemory;
        slot.MemoryHeat = Heat.Level(Heat.Scale(memoryShare, 0.2, 0.5, 1, 2, 4, 8));
    }

    public static string PriorityName(PriorityClass priority) => priority switch
    {
        PriorityClass.Realtime => Loc.Get("Priority_Realtime"),
        PriorityClass.High => Loc.Get("Priority_High"),
        PriorityClass.AboveNormal => Loc.Get("Priority_AboveNormal"),
        PriorityClass.Normal => Loc.Get("Priority_Normal"),
        PriorityClass.BelowNormal => Loc.Get("Priority_BelowNormal"),
        PriorityClass.Idle => Loc.Get("Priority_Idle"),
        _ => "–"
    };
}
