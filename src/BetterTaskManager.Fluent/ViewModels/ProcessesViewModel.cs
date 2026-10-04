using BetterTaskManager.Core.Network;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Fluent.ViewModels;

public sealed class ProcessesViewModel : ObservableObject
{
    private readonly MonitorHost monitor;
    private readonly AppSettings settings;
    private readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    private string cpuHeader = "", memoryHeader = "", ioHeader = "", networkHeader = "", bandwidthHeader = "", bandwidthTooltip = "";

    public ProcessesViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        Layout = new ColumnLayout("Processes.", new Dictionary<string, double>
        {
            ["Name"] = 360, ["Cpu"] = 88, ["Memory"] = 130, ["Io"] = 140, ["Bandwidth"] = 104, ["Network"] = 104, ["Publisher"] = 200
        }, settings.ColumnWidths);
        ProcessSlot.SharedLayout = Layout;
        Rows = new SlotCollection<ProcessSlot, (ProcessRowData, ProcessRowContext)>(ProcessSlot.Load);
    }

    public ColumnLayout Layout { get; }
    public SlotCollection<ProcessSlot, (ProcessRowData, ProcessRowContext)> Rows { get; }

    public string SortColumn => settings.ProcessSortColumn;
    public bool SortDescending => settings.ProcessSortDescending;

    public string CpuHeader { get => cpuHeader; private set => Set(ref cpuHeader, value); }
    public string MemoryHeader { get => memoryHeader; private set => Set(ref memoryHeader, value); }
    public string IoHeader { get => ioHeader; private set => Set(ref ioHeader, value); }
    public string NetworkHeader { get => networkHeader; private set => Set(ref networkHeader, value); }
    public string BandwidthHeader { get => bandwidthHeader; private set => Set(ref bandwidthHeader, value); }
    public string BandwidthTooltip { get => bandwidthTooltip; private set => Set(ref bandwidthTooltip, value); }

    public void Refresh()
    {
        MonitorSnapshot? snapshot = monitor.Latest;
        if (snapshot is null) return;

        SystemSample system = snapshot.System;
        CpuHeader = system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "…";
        MemoryHeader = Format.WholePercent(system.Memory.LoadPercent);
        IoHeader = Format.Rate(system.IoBytesPerSecond);
        NetworkHeader = Format.Count(snapshot.Connections.Count);
        bool bandwidth = system.PerProcessNetworkAvailable;
        BandwidthHeader = bandwidth ? Format.Mbps(snapshot.Processes.Where(process => !VpnTunnels.IsTunnel(process)).Sum(process => process.NetworkBytesPerSecond))
            : monitor.IsElevated ? Loc.Get("Header_Paused") : Loc.Get("Header_Admin");
        BandwidthTooltip = bandwidth
            ? system.PerProcessNetworkFromService
                ? Loc.Get("Bandwidth_TooltipService")
                : Loc.Get("Bandwidth_TooltipKernel")
            : monitor.IsElevated ? Loc.NetworkStatus(system.PerProcessNetworkStatus)
            : Loc.NetworkStatus(system.PerProcessNetworkStatus) + Loc.Get("Bandwidth_UseRestart");

        List<ProcessRowData> rows = ProcessTree.Build(snapshot.Processes, monitor.SearchText, SortColumn, SortDescending, expanded);
        var context = new ProcessRowContext(system.Memory.Total, bandwidth, monitor.IsBlocked);
        Rows.Apply(rows.Select(row => (row, context)).ToList());
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
            settings.ProcessSortDescending = column is not (ProcessTree.SortName or ProcessTree.SortPublisher or ProcessTree.SortPath);
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
