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
    public int Threads { get; init; }
    public int Handles { get; init; }
    public int SessionId { get; init; }
    public string? WindowTitle { get; init; }
    /// <summary>Display names of hosted Windows services (svchost and other service processes).</summary>
    public IReadOnlyList<string>? Services { get; init; }
    public int ConnectionCount { get; set; }
    public double NetworkReceiveBytesPerSecond { get; set; }
    public double NetworkSendBytesPerSecond { get; set; }
    /// <summary>Bytes received/sent since Better Task Manager started watching this process.</summary>
    public long NetworkReceivedTotal { get; set; }
    public long NetworkSentTotal { get; set; }
    public double NetworkBytesPerSecond => NetworkReceiveBytesPerSecond + NetworkSendBytesPerSecond;

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
    public double IoBytesPerSecond { get; init; }
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public TimeSpan Uptime { get; init; }
}

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
