using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace BetterTaskManager.Core.Network;

public enum HostNameSource
{
    /// <summary>The name an app looked up (DNS cache or live DNS event): what the app actually asked for.</summary>
    Lookup,
    /// <summary>Reverse DNS (PTR). Often a hosting provider's name rather than the service's.</summary>
    Reverse
}

public readonly record struct HostName(string Name, HostNameSource Source);

/// <summary>
/// Maps remote IP addresses to host names, cheapest and most accurate source first:
/// 1. the Windows DNS client cache (read every 10 s, works without elevation),
/// 2. live DNS-Client ETW events when elevated, so short-lived cache entries are not missed,
/// 3. reverse DNS as a fallback for apps with their own resolver (for example browsers using DNS over HTTPS).
/// Lookups never block the caller; unknown addresses are resolved in the background and appear on a later refresh.
/// </summary>
public sealed class HostNameResolver : IDisposable
{
    public const string DefaultSessionName = "BetterTaskManager-Dns";
    private const int DnsQueryCompletedEvent = 3008;
    private static readonly TimeSpan CachePollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FailedLookupRetry = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly Dictionary<IPAddress, HostName> names = new();
    private readonly Dictionary<IPAddress, DateTime> failedReverse = new();
    private readonly HashSet<IPAddress> pendingReverse = new();
    private readonly SemaphoreSlim reverseSlots = new(4);
    private readonly CancellationTokenSource shutdown = new();
    private readonly string sessionName;
    private TraceEventSession? dnsSession;
    private Task? cacheLoop;

    /// <param name="sessionName">Machine-wide ETW session name; tests must pass their own (see <see cref="BandwidthMonitor"/>).</param>
    public HostNameResolver(string sessionName = DefaultSessionName) => this.sessionName = sessionName;

    public bool LiveDnsEvents { get; private set; }

    public void Start()
    {
        TryStartDnsTrace();
        cacheLoop ??= Task.Run(PollCacheAsync);
    }

    /// <summary>Returns a known name, or null and schedules a background reverse lookup.</summary>
    public HostName? Resolve(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? ip) || !IsRemote(ip)) return null;
        ip = Normalize(ip);
        lock (gate)
        {
            if (names.TryGetValue(ip, out HostName known)) return known;
            if (pendingReverse.Contains(ip)) return null;
            if (failedReverse.TryGetValue(ip, out DateTime failedAt) && DateTime.UtcNow - failedAt < FailedLookupRetry) return null;
            pendingReverse.Add(ip);
        }
        _ = ReverseLookupAsync(ip);
        return null;
    }

    private async Task PollCacheAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            // The live DNS trace can be stopped from outside; bring it back on the next poll.
            if (!LiveDnsEvents) TryStartDnsTrace();
            try
            {
                ReadDnsCache();
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // The cache is an optimisation; reverse lookups still work without it.
            }
            try
            {
                await Task.Delay(CachePollInterval, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ReadDnsCache()
    {
        using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Entry, Data, Type FROM MSFT_DNSClientCache");
        using ManagementObjectCollection records = searcher.Get();
        foreach (ManagementBaseObject record in records)
        {
            using (record)
            {
                // Type 1 = A, 28 = AAAA. "Entry" is the name the app asked for, not the CNAME target.
                ushort type = Convert.ToUInt16(record["Type"]);
                if (type is not (1 or 28)) continue;
                if (record["Entry"] is string entry && record["Data"] is string data && IPAddress.TryParse(data, out IPAddress? ip))
                {
                    Remember(ip, entry, HostNameSource.Lookup);
                }
            }
        }
    }

    private void TryStartDnsTrace()
    {
        if (shutdown.IsCancellationRequested || TraceEventSession.IsElevated() != true) return;
        try
        {
            dnsSession?.Dispose();
            dnsSession = new TraceEventSession(sessionName) { StopOnDispose = true };
            var parser = new RegisteredTraceEventParser(dnsSession.Source);
            parser.All += OnDnsEvent;
            dnsSession.EnableProvider("Microsoft-Windows-DNS-Client");
            TraceEventSession started = dnsSession;
            var pump = new Thread(() =>
            {
                try
                {
                    started.Source.Process();
                }
                catch (Exception)
                {
                    // Session stopped or lost; the cache poll keeps names coming.
                }
                LiveDnsEvents = false;
            })
            { IsBackground = true, Name = "DnsTrace" };
            pump.Start();
            LiveDnsEvents = true;
        }
        catch (Exception)
        {
            dnsSession?.Dispose();
            dnsSession = null;
        }
    }

    private void OnDnsEvent(TraceEvent data)
    {
        if ((int)data.ID != DnsQueryCompletedEvent) return;
        if (data.PayloadByName("QueryName") is not string name || name.Length == 0) return;
        if (data.PayloadByName("QueryResults") is not string results) return;

        // QueryResults looks like "type:  5 alias.example.net;::ffff:1.2.3.4;1.2.3.4;"
        foreach (string part in results.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(part, out IPAddress? ip)) Remember(ip, name, HostNameSource.Lookup);
        }
    }

    private async Task ReverseLookupAsync(IPAddress ip)
    {
        await reverseSlots.WaitAsync();
        try
        {
            string? name = await Task.Run(() => QueryPointer(ip)).WaitAsync(TimeSpan.FromSeconds(5), shutdown.Token);
            if (name is not null) Remember(ip, name, HostNameSource.Reverse);
            else lock (gate) failedReverse[ip] = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            lock (gate) failedReverse[ip] = DateTime.UtcNow;
        }
        finally
        {
            lock (gate) pendingReverse.Remove(ip);
            reverseSlots.Release();
        }
    }

    /// <summary>
    /// A PTR query only. Dns.GetHostEntry would also forward-resolve the result, which costs a second query and
    /// leaves that forward record in the Windows cache where it would later look like the app's own lookup.
    /// </summary>
    private static string? QueryPointer(IPAddress ip)
    {
        const ushort DnsTypePtr = 12;
        const int OffsetType = 16;
        const int OffsetPointerName = 32;
        if (DnsQuery(ReverseName(ip), DnsTypePtr, 0, IntPtr.Zero, out IntPtr records, IntPtr.Zero) != 0 || records == IntPtr.Zero) return null;
        try
        {
            for (IntPtr record = records; record != IntPtr.Zero; record = Marshal.ReadIntPtr(record))
            {
                if ((ushort)Marshal.ReadInt16(record, OffsetType) != DnsTypePtr) continue;
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(record, OffsetPointerName));
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return null;
        }
        finally
        {
            DnsRecordListFree(records, 1);
        }
    }

    private static string ReverseName(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
        }
        var nibbles = new System.Text.StringBuilder(72);
        for (int index = bytes.Length - 1; index >= 0; index--)
        {
            nibbles.Append((bytes[index] & 0xF).ToString("x")).Append('.');
            nibbles.Append((bytes[index] >> 4).ToString("x")).Append('.');
        }
        return nibbles.Append("ip6.arpa").ToString();
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode, EntryPoint = "DnsQuery_W")]
    private static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll")]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);

    private void Remember(IPAddress ip, string name, HostNameSource source)
    {
        ip = Normalize(ip);
        lock (gate)
        {
            if (names.TryGetValue(ip, out HostName existing))
            {
                // A name the app looked up always beats a reverse-DNS guess.
                if (source == HostNameSource.Reverse && existing.Source == HostNameSource.Lookup) return;
                // Our own reverse lookup also caches a forward record for the PTR name; keep it labelled as reverse.
                if (existing.Source == HostNameSource.Reverse && string.Equals(existing.Name, name.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)) return;
            }
            names[ip] = new HostName(name.TrimEnd('.'), source);
            failedReverse.Remove(ip);
        }
    }

    private static IPAddress Normalize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) return ip.MapToIPv4();
        // Drop IPv6 zone IDs (fe80::1%12) so the same address always maps to one key. ScopeId throws for IPv4.
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0 ? new IPAddress(ip.GetAddressBytes()) : ip;
    }

    private static bool IsRemote(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            if (bytes[0] >= 224) return false; // multicast and broadcast
        }
        return true;
    }

    public void Dispose()
    {
        shutdown.Cancel();
        dnsSession?.Dispose();
        dnsSession = null;
    }
}
