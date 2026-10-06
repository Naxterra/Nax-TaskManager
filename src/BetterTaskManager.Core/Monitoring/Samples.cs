namespace BetterTaskManager.Core.Monitoring;

public sealed class ProcessSample
{
    public required int Pid { get; init; }
    public required int ParentPid { get; init; }
    public required string ImageName { get; init; }
    public required long CreateTime { get; init; }
    public required string Path { get; init; }
    public required string Description { get; init; }
    public required string Company { get; init; }
    public double CpuPercent { get; init; }
    public bool CpuSampled { get; init; }
    public long PrivateWorkingSet { get; init; }
    public long WorkingSet { get; init; }
    public long CommitCharge { get; init; }
    public double IoBytesPerSecond { get; init; }
    /// <summary>Bytes per second that reached a disk (not cached I/O); needs administrator rights or the history service.</summary>
    public double DiskReadBytesPerSecond { get; set; }
    public double DiskWriteBytesPerSecond { get; set; }
    public double DiskBytesPerSecond => DiskReadBytesPerSecond + DiskWriteBytesPerSecond;
    /// <summary>Utilization of the process's busiest GPU engine, in percent.</summary>
    public double GpuPercent { get; init; }
    /// <summary>That engine, e.g. "GPU 0 - 3D"; empty when the process uses no GPU.</summary>
    public string GpuEngine { get; init; } = "";
    public int Threads { get; init; }
    public int Handles { get; init; }
    public int SessionId { get; init; }
    public string? WindowTitle { get; init; }
    /// <summary>Display names of hosted Windows services (svchost and other service processes).</summary>
    public IReadOnlyList<string>? Services { get; init; }
    public int ConnectionCount { get; set; }
    public double NetworkReceiveBytesPerSecond { get; set; }
    public double NetworkSendBytesPerSecond { get; set; }
    /// <summary>Bytes received/sent since Nax-TaskManager started watching this process.</summary>
    public long NetworkReceivedTotal { get; set; }
    public long NetworkSentTotal { get; set; }
    public double NetworkBytesPerSecond => NetworkReceiveBytesPerSecond + NetworkSendBytesPerSecond;
    /// <summary>Every thread is suspended (parked Store apps, debugged or frozen processes).</summary>
    public bool Suspended { get; init; }
    /// <summary>Account the process runs as; empty when Windows does not tell this caller (another account, not elevated).</summary>
    public string UserName { get; init; } = "";
    /// <summary>Priority class; Unknown when the process cannot be opened.</summary>
    public BetterTaskManager.Core.Native.PriorityClass Priority { get; init; }
    /// <summary>Efficiency mode (EcoQoS), set by Task Manager, by this app or by the program itself.</summary>
    public bool Efficiency { get; init; }

    /// <summary>Stable identity across refreshes: PIDs are reused, creation times are not.</summary>
    public (int Pid, long CreateTime) Key => (Pid, CreateTime);

    public string DisplayName => !string.IsNullOrWhiteSpace(Description) ? Description : System.IO.Path.GetFileNameWithoutExtension(ImageName);
}

public sealed class ConnectionSample
{
    public required int Pid { get; init; }
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public required string State { get; init; }
    public double ReceiveBytesPerSecond { get; set; }
    public double SendBytesPerSecond { get; set; }
    /// <summary>Host name for the remote address, when known.</summary>
    public string? RemoteHost { get; set; }
    /// <summary>True when <see cref="RemoteHost"/> came from reverse DNS rather than the app's own lookup.</summary>
    public bool RemoteHostIsReverse { get; set; }
    /// <summary>Where the remote side lives (this PC, LAN, Internet…).</summary>
    public BetterTaskManager.Core.Network.IpScope Scope { get; set; }
    /// <summary>TCP only: the remote side connected to a port this process listens on.</summary>
    public bool Inbound { get; set; }
}

/// <summary>
/// Bytes one socket moved during the last interval, from the kernel trace (administrator only). Unlike the UDP
/// table, UDP flows keep their remote side here, so QUIC/HTTP3 traffic can be attributed to a host.
/// </summary>
public sealed class FlowSample
{
    public required int Pid { get; init; }
    public required string Protocol { get; init; }
    public required int LocalPort { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public long Received { get; init; }
    public long Sent { get; init; }
}

/// <summary>
/// Physical memory split so the parts add up to Task Manager's "In use" figure.
/// </summary>
public sealed class MemoryBreakdown
{
    public long Total { get; init; }
    public long Available { get; init; }
    public long InUse { get; init; }
    public long Modified { get; init; }
    public long Standby { get; init; }
    public long Free { get; init; }
    public long ProcessPrivate { get; init; }
    public long KernelPools { get; init; }
    public long FileCache { get; init; }
    public long Drivers { get; init; }
    /// <summary>In-use memory not attributed to the categories above: shared images, page tables, driver-locked pages.</summary>
    public long Unattributed => Math.Max(0, InUse - ProcessPrivate - KernelPools - FileCache - Drivers);
    public long CommitTotal { get; init; }
    public long CommitLimit { get; init; }
    public bool Detailed { get; init; }
    public double LoadPercent => Total == 0 ? 0 : InUse * 100d / Total;
}

public sealed class SystemSample
{
    public double CpuPercent { get; init; }
    public bool CpuSampled { get; init; }
    public required MemoryBreakdown Memory { get; init; }
    public double NetworkReceiveBytesPerSecond { get; init; }
    public double NetworkSendBytesPerSecond { get; init; }
    public bool NetworkSampled { get; init; }
    /// <summary>True when per-process network throughput is being measured (needs administrator rights).</summary>
    public bool PerProcessNetworkAvailable { get; init; }
    public string PerProcessNetworkStatus { get; init; } = "";
    /// <summary>True when the per-process figures come from the history service rather than this process's own trace.</summary>
    public bool PerProcessNetworkFromService { get; init; }
    public double IoBytesPerSecond { get; init; }
    /// <summary>Utilization of the busiest GPU engine over all processes, like Task Manager's GPU column header.</summary>
    public double GpuPercent { get; init; }
    public bool GpuAvailable { get; init; }
    /// <summary>True when per-process disk rates are measured (administrator rights, or the history service).</summary>
    public bool PerProcessDiskAvailable { get; init; }
    public bool PerProcessDiskFromService { get; init; }
    /// <summary>Physical disks; empty when the disk counters are not read.</summary>
    public IReadOnlyList<DiskSample> Disks { get; init; } = [];
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public TimeSpan Uptime { get; init; }
}

/// <summary>One physical disk, as on Task Manager's Performance page and Resource Monitor's Storage list.</summary>
public sealed record DiskSample(int Number, string Model, string Kind, IReadOnlyList<VolumeSample> Volumes,
    double ActivePercent, double ReadPerSecond, double WritePerSecond, double ResponseMilliseconds, double QueueLength);

/// <summary>A drive letter on a physical disk.</summary>
public sealed record VolumeSample(string Letter, string Label, long Free, long Total);

public sealed class MonitorSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required IReadOnlyList<ProcessSample> Processes { get; init; }
    public required IReadOnlyList<ConnectionSample> Connections { get; init; }
    /// <summary>Per-socket traffic since the previous snapshot; empty without administrator rights.</summary>
    public IReadOnlyList<FlowSample> Flows { get; init; } = [];
    public required IReadOnlyList<string> NetworkIssues { get; init; }
    public required SystemSample System { get; init; }
    public TimeSpan CollectionTime { get; init; }
}
