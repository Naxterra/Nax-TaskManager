using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Fluent.ViewModels;

public sealed class ProcessesViewModel : ObservableObject
{
    private readonly MonitorHost monitor;
    private readonly AppSettings settings;
    private readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    private string cpuHeader = "", memoryHeader = "", ioHeader = "", networkHeader = "";

    public ProcessesViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        Layout = new ColumnLayout("Processes.", new Dictionary<string, double>
        {
            ["Name"] = 380, ["Cpu"] = 92, ["Memory"] = 112, ["Io"] = 96, ["Network"] = 104, ["Publisher"] = 220
        }, settings.ColumnWidths);
        ProcessSlot.SharedLayout = Layout;
        Rows = new SlotCollection<ProcessSlot, (ProcessRowData, long, Func<string, bool>)>(ProcessSlot.Load);
    }

    public ColumnLayout Layout { get; }
    public SlotCollection<ProcessSlot, (ProcessRowData, long, Func<string, bool>)> Rows { get; }

    public string SortColumn => settings.ProcessSortColumn;
    public bool SortDescending => settings.ProcessSortDescending;

    public string CpuHeader { get => cpuHeader; private set => Set(ref cpuHeader, value); }
    public string MemoryHeader { get => memoryHeader; private set => Set(ref memoryHeader, value); }
    public string IoHeader { get => ioHeader; private set => Set(ref ioHeader, value); }
    public string NetworkHeader { get => networkHeader; private set => Set(ref networkHeader, value); }

    public void Refresh()
    {
        MonitorSnapshot? snapshot = monitor.Latest;
        if (snapshot is null) return;

        SystemSample system = snapshot.System;
        CpuHeader = system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "…";
        MemoryHeader = Format.WholePercent(system.Memory.LoadPercent);
        IoHeader = Format.Rate(system.IoBytesPerSecond);
        NetworkHeader = Format.Count(snapshot.Connections.Count);

        List<ProcessRowData> rows = ProcessTree.Build(snapshot.Processes, monitor.SearchText, SortColumn, SortDescending, expanded);
        long total = system.Memory.Total;
        Func<string, bool> isBlocked = monitor.IsBlocked;
        Rows.Apply(rows.Select(row => (row, total, isBlocked)).ToList());
    }

    public void Sort(string column)
    {
        if (settings.ProcessSortColumn == column)
        {
            settings.ProcessSortDescending = !settings.ProcessSortDescending;
        }
        else
        {
            settings.ProcessSortColumn = column;
            // Names read naturally A→Z; resource columns are most useful largest-first.
            settings.ProcessSortDescending = column is not (ProcessTree.SortName or ProcessTree.SortPublisher);
        }
        Raise(nameof(SortColumn));
        Refresh();
    }

    public void ToggleExpanded(string key)
    {
        if (!expanded.Remove(key)) expanded.Add(key);
        Refresh();
    }
}
