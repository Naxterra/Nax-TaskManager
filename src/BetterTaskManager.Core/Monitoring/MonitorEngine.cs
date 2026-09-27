using System.Diagnostics;
using System.Net.NetworkInformation;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Core.Network;

namespace BetterTaskManager.Core.Monitoring;

/// <summary>
/// Collects one consistent snapshot per interval on a background thread. Consumers receive immutable
/// snapshots through <see cref="SnapshotReady"/> and never block collection.
/// </summary>
public sealed class MonitorEngine : IDisposable
{
    private readonly record struct Identity(string Path, string Description, string Company);
    private readonly record struct CpuMark(long CpuTime, long IoBytes);

    private readonly Dictionary<(int, long), Identity> identities = new();
    private readonly Dictionary<string, (string Description, string Company)> fileInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int, long), CpuMark> previousMarks = new();
    private readonly MemoryCounters memoryCounters = new();
    private readonly BandwidthMonitor bandwidth = new();
    private readonly Dictionary<(int, long), ByteCounts> networkTotals = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly SemaphoreSlim wake = new(0);
    private Dictionary<string, (long Received, long Sent)> previousAdapters = new();
    private NetworkInterface[] adapters = [];
    private long adaptersReadAt;
    private long previousTimestamp;
    private long previousIdle, previousKernel, previousUser;
    private Dictionary<int, List<string>> services = new();
    private long servicesReadAt;
    private Task? loop;
    private volatile bool paused;

    public event Action<MonitorSnapshot>? SnapshotReady;
    public event Action<Exception>? CollectionFailed;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            if (!value) RequestRefresh();
        }
    }

    public MonitorSnapshot? Latest { get; private set; }

    public void Start()
    {
        bandwidth.TryStart();
        loop ??= Task.Run(RunAsync);
    }

    public void RequestRefresh()
    {
        if (wake.CurrentCount == 0) wake.Release();
    }

    private async Task RunAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            if (!paused)
            {
                try
                {
                    MonitorSnapshot snapshot = Collect();
                    Latest = snapshot;
                    SnapshotReady?.Invoke(snapshot);
                }
                catch (Exception ex)
                {
                    CollectionFailed?.Invoke(ex);
                }
            }

            try
            {
                await wake.WaitAsync(Interval, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Collects a snapshot synchronously. Public for tests and the self-check.</summary>
    public MonitorSnapshot Collect()
    {
        var stopwatch = Stopwatch.StartNew();
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(previousTimestamp, now).TotalSeconds;
        previousTimestamp = now;

        // Flush first; the counts are drained after the rest of the collection, giving the trace thread time to catch up.
        bandwidth.Flush();
        List<RawProcess> raw = NtProcessReader.Read();
        Dictionary<int, string> windowTitles = VisibleWindows.ReadTitlesByProcess();
        if (servicesReadAt == 0 || Stopwatch.GetElapsedTime(servicesReadAt).TotalSeconds >= 10)
        {
            services = ServiceNames.ReadByProcess();
            servicesReadAt = Stopwatch.GetTimestamp();
        }
        int processorCount = Environment.ProcessorCount;

        var processes = new List<ProcessSample>(raw.Count);
        var liveKeys = new HashSet<(int, long)>();
        double totalIo = 0;
        int threads = 0, handles = 0;

        foreach (RawProcess process in raw)
        {
            var key = (process.Pid, process.CreateTime);
            liveKeys.Add(key);
            threads += process.ThreadCount;
            handles += process.HandleCount;

            Identity identity = ResolveIdentity(process);
            long ioBytes = process.ReadBytes + process.WriteBytes + process.OtherBytes;
            double cpu = 0, io = 0;
            bool sampled = false;
            if (process.Pid != 0 && elapsedSeconds > 0 && previousMarks.TryGetValue(key, out CpuMark previous))
            {
                cpu = Math.Clamp((process.CpuTime100ns - previous.CpuTime) / (elapsedSeconds * 1e7 * processorCount) * 100, 0, 100);
                io = Math.Max(0, (ioBytes - previous.IoBytes) / elapsedSeconds);
                sampled = true;
            }
            previousMarks[key] = new CpuMark(process.CpuTime100ns, ioBytes);
            totalIo += io;

            windowTitles.TryGetValue(process.Pid, out string? title);
            services.TryGetValue(process.Pid, out List<string>? hosted);
            processes.Add(new ProcessSample
            {
                Pid = process.Pid,
                ParentPid = process.ParentPid,
                ImageName = process.Name,
                CreateTime = process.CreateTime,
                Path = identity.Path,
                Description = identity.Description,
                Company = identity.Company,
                CpuPercent = cpu,
                CpuSampled = sampled,
                PrivateWorkingSet = process.PrivateWorkingSet,
                WorkingSet = process.WorkingSet,
                CommitCharge = process.CommitCharge,
                IoBytesPerSecond = io,
                Threads = process.ThreadCount,
                Handles = process.HandleCount,
                SessionId = process.SessionId,
                WindowTitle = title,
                Services = hosted
            });
        }

        foreach (var stale in previousMarks.Keys.Where(key => !liveKeys.Contains(key)).ToList()) previousMarks.Remove(stale);
        foreach (var stale in identities.Keys.Where(key => !liveKeys.Contains(key)).ToList()) identities.Remove(stale);
        foreach (var stale in networkTotals.Keys.Where(key => !liveKeys.Contains(key)).ToList()) networkTotals.Remove(stale);

        NativeNetworkSnapshot network = NativeNetworkCollector.GetSnapshot();
        var connections = network.Connections.Select(connection => new ConnectionSample
        {
            Pid = connection.OwningPid,
            Protocol = connection.Protocol,
            LocalAddress = connection.LocalAddress,
            LocalPort = connection.LocalPort,
            RemoteAddress = connection.RemoteAddress,
            RemotePort = connection.RemotePort,
            State = connection.State
        }).ToList();
        var connectionCounts = connections.GroupBy(connection => connection.Pid).ToDictionary(group => group.Key, group => group.Count());
        foreach (ProcessSample process in processes)
        {
            if (connectionCounts.TryGetValue(process.Pid, out int count)) process.ConnectionCount = count;
        }
        ApplyThroughput(processes, connections, elapsedSeconds);

        (double cpuPercent, bool cpuSampled) = SampleSystemCpu();
        (double receive, double send, bool networkSampled) = SampleAdapters(elapsedSeconds);

        var system = new SystemSample
        {
            CpuPercent = cpuPercent,
            CpuSampled = cpuSampled,
            Memory = SampleMemory(processes.Sum(process => process.PrivateWorkingSet)),
            NetworkReceiveBytesPerSecond = receive,
            NetworkSendBytesPerSecond = send,
            NetworkSampled = networkSampled,
            PerProcessNetworkAvailable = bandwidth.IsRunning,
            PerProcessNetworkStatus = bandwidth.Status,
            IoBytesPerSecond = totalIo,
            ProcessCount = processes.Count,
            ThreadCount = threads,
            HandleCount = handles,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };

        return new MonitorSnapshot
        {
            Timestamp = DateTime.Now,
            Processes = processes,
            Connections = connections,
            NetworkIssues = network.Issues,
            System = system,
            CollectionTime = stopwatch.Elapsed
        };
    }

    private Identity ResolveIdentity(RawProcess process)
    {
        var key = (process.Pid, process.CreateTime);
        if (identities.TryGetValue(key, out Identity cached)) return cached;

        string path = process.Pid <= 4 ? "" : Win32.QueryImagePath(process.Pid);
        string description = "", company = "";
        if (path.Length > 0)
        {
            if (!fileInfo.TryGetValue(path, out var info))
            {
                try
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                    info = ((version.FileDescription ?? "").Trim(), (version.CompanyName ?? "").Trim());
                }
                catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
                {
                    info = ("", "");
                }
                fileInfo[path] = info;
            }
            (description, company) = info;
        }
        else if (process.Pid == 4)
        {
            description = "System";
        }

        var identity = new Identity(path, description, company);
        identities[key] = identity;
        return identity;
    }

    /// <summary>Turns the bytes counted by the ETW trace since the last snapshot into per-process and per-socket rates.</summary>
    private void ApplyThroughput(List<ProcessSample> processes, List<ConnectionSample> connections, double elapsedSeconds)
    {
        if (!bandwidth.IsRunning) return;
        var (byProcess, bySocket) = bandwidth.Drain();
        if (elapsedSeconds <= 0) return;

        foreach (ProcessSample process in processes)
        {
            var key = (process.Pid, process.CreateTime);
            networkTotals.TryGetValue(key, out ByteCounts total);
            if (byProcess.TryGetValue(process.Pid, out ByteCounts counts))
            {
                process.NetworkReceiveBytesPerSecond = counts.Received / elapsedSeconds;
                process.NetworkSendBytesPerSecond = counts.Sent / elapsedSeconds;
                total.Received += counts.Received;
                total.Sent += counts.Sent;
                networkTotals[key] = total;
            }
            process.NetworkReceivedTotal = total.Received;
            process.NetworkSentTotal = total.Sent;
        }

        // TCP rows match one socket exactly; the UDP table has no remote side, so UDP rows get the sum per local port.
        var tcp = new Dictionary<(int, int, string, int), ByteCounts>();
        var udp = new Dictionary<(int, int), ByteCounts>();
        foreach (var (socket, counts) in bySocket)
        {
            if (socket.Tcp)
            {
                var key = (socket.Pid, socket.LocalPort, socket.RemoteAddress.ToString(), socket.RemotePort);
                tcp.TryGetValue(key, out ByteCounts sum);
                sum.Received += counts.Received;
                sum.Sent += counts.Sent;
                tcp[key] = sum;
            }
            else
            {
                var key = (socket.Pid, socket.LocalPort);
                udp.TryGetValue(key, out ByteCounts sum);
                sum.Received += counts.Received;
                sum.Sent += counts.Sent;
                udp[key] = sum;
            }
        }

        foreach (ConnectionSample connection in connections)
        {
            ByteCounts counts;
            bool found = connection.Protocol == "TCP"
                ? tcp.TryGetValue((connection.Pid, connection.LocalPort, connection.RemoteAddress, connection.RemotePort), out counts)
                : udp.Remove((connection.Pid, connection.LocalPort), out counts); // first socket on the port gets it, so nothing is shown twice
            if (!found) continue;
            connection.ReceiveBytesPerSecond = counts.Received / elapsedSeconds;
            connection.SendBytesPerSecond = counts.Sent / elapsedSeconds;
        }
    }

    private (double Percent, bool Sampled) SampleSystemCpu()
    {
        if (!Win32.GetSystemTimes(out long idle, out long kernel, out long user)) return (0, false);
        bool sampled = previousKernel != 0;
        long idleDelta = idle - previousIdle;
        long totalDelta = (kernel - previousKernel) + (user - previousUser);
        (previousIdle, previousKernel, previousUser) = (idle, kernel, user);
        if (!sampled || totalDelta <= 0) return (0, false);
        return (Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100), true);
    }

    private (double Receive, double Send, bool Sampled) SampleAdapters(double elapsedSeconds)
    {
        // Enumerating adapters is far more expensive than reading their counters, so the list refreshes every 10 s.
        if (adaptersReadAt == 0 || Stopwatch.GetElapsedTime(adaptersReadAt).TotalSeconds >= 10)
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
            adaptersReadAt = Stopwatch.GetTimestamp();
        }

        var current = new Dictionary<string, (long Received, long Sent)>();
        foreach (NetworkInterface adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                current[adapter.Id] = (statistics.BytesReceived, statistics.BytesSent);
            }
            catch (NetworkInformationException)
            {
            }
        }

        long received = 0, sent = 0;
        int matched = 0;
        foreach (var (id, value) in current)
        {
            if (!previousAdapters.TryGetValue(id, out var old) || value.Received < old.Received || value.Sent < old.Sent) continue;
            received += value.Received - old.Received;
            sent += value.Sent - old.Sent;
            matched++;
        }
        previousAdapters = current;
        if (matched == 0 || elapsedSeconds <= 0) return (0, 0, false);
        return (received / elapsedSeconds, sent / elapsedSeconds, true);
    }

    private MemoryBreakdown SampleMemory(long processPrivate)
    {
        var status = new Win32.MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MemoryStatusEx>() };
        Win32.GlobalMemoryStatusEx(ref status);
        Dictionary<string, long> counters = memoryCounters.Read();
        long Get(string name) => counters.TryGetValue(name, out long value) ? value : 0;

        long total = (long)status.TotalPhys;
        long available = (long)status.AvailPhys;
        long modified = Get("Modified Page List Bytes");
        return new MemoryBreakdown
        {
            Total = total,
            Available = available,
            Modified = modified,
            InUse = Math.Max(0, total - available - modified),
            Standby = Get("Standby Cache Core Bytes") + Get("Standby Cache Normal Priority Bytes") + Get("Standby Cache Reserve Bytes"),
            Free = Get("Free & Zero Page List Bytes"),
            ProcessPrivate = processPrivate,
            KernelPools = Get("Pool Paged Resident Bytes") + Get("Pool Nonpaged Bytes"),
            FileCache = Get("System Cache Resident Bytes"),
            Drivers = Get("System Driver Resident Bytes"),
            CommitTotal = (long)(status.TotalPageFile - status.AvailPageFile),
            CommitLimit = (long)status.TotalPageFile,
            Detailed = counters.Count > 0
        };
    }

    public void Dispose()
    {
        shutdown.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        memoryCounters.Dispose();
        bandwidth.Dispose();
        shutdown.Dispose();
        wake.Dispose();
    }
}
