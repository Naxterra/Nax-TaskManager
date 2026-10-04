using System.Net;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;

namespace BetterTaskManager.Core.History;

/// <summary>How history groups a process; the rules live in <see cref="AppIdentityRules"/>.</summary>
internal sealed record AppIdentity(string Key, string Name, string Path, long CreateTime, bool Tunnel = false)
{
    public static AppIdentity From(ProcessSample process) =>
        new(AppIdentityRules.AppKey(process), AppIdentityRules.AppName(process), process.Path, process.CreateTime, VpnTunnels.IsTunnel(process));

    public static AppIdentity Unknown(int pid) => new("pid:" + pid, $"Exited process (PID {pid})", "", 0);

    /// <summary>A process that exited before a snapshot listed it, named from the kernel trace.</summary>
    public static AppIdentity FromTraced(BandwidthMonitor.TracedProcess traced)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(traced.Name);
        try
        {
            if (traced.Path.Length > 0 && File.Exists(traced.Path) &&
                System.Diagnostics.FileVersionInfo.GetVersionInfo(traced.Path).FileDescription is { Length: > 0 } description)
            {
                name = description.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        string key = AppIdentityRules.PathKey(traced.Path) ?? traced.Name.ToLowerInvariant();
        return new AppIdentity(key, name, traced.Path, -1);
    }
}

internal sealed class TrackedConnection
{
    public long Id;
    public required AppIdentity App;
    public required int Pid;
    public required string Protocol;
    public required int LocalPort;
    public required string RemoteAddress;
    public required int RemotePort;
    public required DateTime FirstSeen;
    public DateTime LastSeen;
    public string LocalAddress = "";
    public string? RemoteHost;
    public bool RemoteHostIsReverse;
    public long BytesIn, BytesOut;
    public string State = "";
    public string Scope = "";
    public bool Inbound;
    public bool Changed;
    public bool Ended;
}

internal sealed class UsageDelta
{
    public required AppIdentity App;
    public long BytesIn, BytesOut;
}

/// <summary>
/// Turns monitor snapshots into connection history: one row per TCP connection (from the connection table, so idle
/// connections count too) and per UDP flow (from the kernel trace, which knows the remote side), plus traffic per app
/// and local day. Changes are batched and written every <see cref="FlushInterval"/>. Not thread-safe: call
/// <see cref="Record"/> from one thread.
/// </summary>
public sealed class HistoryRecorder : IDisposable
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UdpIdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ExitedProcessMemory = TimeSpan.FromMinutes(5);

    private readonly record struct FlowKey(int Pid, long CreateTime, string Protocol, int LocalPort, string RemoteAddress, int RemotePort);

    private readonly HistoryStore store;
    private readonly Func<string, HostName?> resolveHost;
    private readonly TracedProcessLookup? tracedProcess;
    private readonly Dictionary<FlowKey, TrackedConnection> live = new();
    private readonly Dictionary<(string Day, string AppKey), UsageDelta> usage = new();
    // Traffic is counted by PID; a process can exit before the next snapshot names it.
    private readonly Dictionary<int, (AppIdentity App, DateTime SeenUtc)> identities = new();
    private DateTime lastFlush = DateTime.UtcNow, lastPrune;

    public delegate bool TracedProcessLookup(int pid, out BandwidthMonitor.TracedProcess process);

    public HistoryRecorder(HistoryStore store, Func<string, HostName?> resolveHost, TracedProcessLookup? tracedProcess = null)
    {
        this.store = store;
        this.resolveHost = resolveHost;
        this.tracedProcess = tracedProcess;
    }

    public void Record(MonitorSnapshot snapshot)
    {
        DateTime now = snapshot.Timestamp.ToUniversalTime();
        string today = HistoryStore.Day(snapshot.Timestamp);

        foreach (ProcessSample process in snapshot.Processes)
        {
            if (!identities.TryGetValue(process.Pid, out var known) || known.App.CreateTime != process.CreateTime)
            {
                identities[process.Pid] = (AppIdentity.From(process), now);
            }
            else
            {
                identities[process.Pid] = (known.App, now);
            }
        }

        var seenTcp = new HashSet<FlowKey>();
        foreach (ConnectionSample connection in snapshot.Connections)
        {
            if (connection.Protocol != "TCP" || connection.Pid <= 0 || connection.State == "Listen" || !IsRemote(connection.RemoteAddress)) continue;
            AppIdentity app = Identify(connection.Pid);
            var key = new FlowKey(connection.Pid, app.CreateTime, "TCP", connection.LocalPort, connection.RemoteAddress, connection.RemotePort);
            TrackedConnection tracked = Track(key, app, now);
            seenTcp.Add(key);
            tracked.LocalAddress = connection.LocalAddress;
            tracked.Scope = IpScopes.Label(connection.Scope);
            tracked.Inbound = connection.Inbound;
            if (tracked.State != connection.State)
            {
                tracked.State = connection.State;
                tracked.Changed = true;
            }
            if (connection.RemoteHost is { } host) SetHost(tracked, host, connection.RemoteHostIsReverse);
        }

        var active = new HashSet<FlowKey>();
        foreach (FlowSample flow in snapshot.Flows)
        {
            if (flow.Pid <= 0 || !IsRemote(flow.RemoteAddress) || flow.Received + flow.Sent == 0) continue;
            AppIdentity app = Identify(flow.Pid);
            // UDP flows are grouped per app and remote endpoint: a client that opens a new local port per request
            // (DNS, QUIC) would otherwise produce one row per packet exchange.
            int localPort = flow.Protocol == "UDP" ? 0 : flow.LocalPort;
            var key = new FlowKey(flow.Pid, app.CreateTime, flow.Protocol, localPort, flow.RemoteAddress, flow.RemotePort);
            TrackedConnection tracked = Track(key, app, now);
            active.Add(key);
            if (tracked.Scope.Length == 0) tracked.Scope = IpScopes.Label(IpScopes.Classify(flow.RemoteAddress));
            tracked.BytesIn += flow.Received;
            tracked.BytesOut += flow.Sent;
            tracked.Changed = true;
            if (tracked.State.Length == 0) tracked.State = flow.Protocol == "UDP" ? "UDP" : "Closed";
            if (tracked.RemoteHost is null && resolveHost(flow.RemoteAddress) is { } host) SetHost(tracked, host.Name, host.Source == HostNameSource.Reverse);

            var usageKey = (today, app.Key);
            if (!usage.TryGetValue(usageKey, out UsageDelta? delta)) usage[usageKey] = delta = new UsageDelta { App = app };
            delta.BytesIn += flow.Received;
            delta.BytesOut += flow.Sent;
        }

        foreach (var (key, tracked) in live)
        {
            bool gone = key.Protocol == "TCP"
                ? !seenTcp.Contains(key) && !active.Contains(key)
                : now - tracked.LastSeen >= UdpIdleTimeout;
            if (gone && !tracked.Ended)
            {
                tracked.Ended = true;
                if (key.Protocol == "TCP" && tracked.State != "Closed")
                {
                    tracked.State = "Closed";
                    tracked.Changed = true;
                }
            }
        }

        if (now - lastFlush >= FlushInterval) Flush(now);
    }

    private TrackedConnection Track(FlowKey key, AppIdentity app, DateTime now)
    {
        if (!live.TryGetValue(key, out TrackedConnection? tracked) || tracked.Ended)
        {
            // An ended UDP flow that starts again becomes a new row, like a new TCP connection would.
            if (tracked is { Ended: true }) live.Remove(key);
            tracked = new TrackedConnection
            {
                App = app,
                Pid = key.Pid,
                Protocol = key.Protocol,
                LocalPort = key.LocalPort,
                RemoteAddress = key.RemoteAddress,
                RemotePort = key.RemotePort,
                FirstSeen = now,
                Changed = true
            };
            live[key] = tracked;
        }
        if (tracked.LastSeen != now)
        {
            tracked.LastSeen = now;
            tracked.Changed = true;
        }
        return tracked;
    }

    private static void SetHost(TrackedConnection tracked, string host, bool reverse)
    {
        if (tracked.RemoteHost == host && tracked.RemoteHostIsReverse == reverse) return;
        tracked.RemoteHost = host;
        tracked.RemoteHostIsReverse = reverse;
        tracked.Changed = true;
    }

    private AppIdentity Identify(int pid)
    {
        if (identities.TryGetValue(pid, out var known)) return known.App;
        if (tracedProcess is not null && tracedProcess(pid, out BandwidthMonitor.TracedProcess traced))
        {
            AppIdentity app = AppIdentity.FromTraced(traced);
            identities[pid] = (app, DateTime.UtcNow);
            return app;
        }
        return AppIdentity.Unknown(pid);
    }

    /// <summary>Writes pending changes now. Called automatically every few seconds and on dispose.</summary>
    public void Flush() => Flush(DateTime.UtcNow);

    private void Flush(DateTime now)
    {
        List<TrackedConnection> changed = live.Values.Where(tracked => tracked.Changed).ToList();
        if (changed.Count > 0 || usage.Count > 0) store.Write(changed, usage, now);
        foreach (TrackedConnection tracked in changed) tracked.Changed = false;
        usage.Clear();
        foreach (var key in live.Where(pair => pair.Value.Ended).Select(pair => pair.Key).ToList()) live.Remove(key);
        foreach (int pid in identities.Where(pair => now - pair.Value.SeenUtc > ExitedProcessMemory).Select(pair => pair.Key).ToList()) identities.Remove(pid);
        lastFlush = now;

        if (now - lastPrune >= PruneInterval)
        {
            store.Prune(Retention, now);
            lastPrune = now;
        }
    }

    /// <summary>Only traffic that leaves the machine; loopback is local IPC.</summary>
    private static bool IsRemote(string address) =>
        IPAddress.TryParse(address, out IPAddress? ip) && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any);

    public void Dispose()
    {
        foreach (TrackedConnection tracked in live.Values)
        {
            if (tracked.Protocol == "TCP" && tracked.State != "Closed") continue; // still open; leave its state as last seen
            tracked.Ended = true;
        }
        Flush(DateTime.UtcNow);
    }
}
