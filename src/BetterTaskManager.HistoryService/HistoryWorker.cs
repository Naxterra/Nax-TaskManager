using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.HistoryService;

/// <summary>Feeds monitor snapshots into the history recorder. Snapshots arrive on the engine's thread, one at a time.</summary>
public sealed class HistoryWorker : IDisposable
{
    private readonly HistoryStore store;
    private readonly MonitorEngine engine;
    private readonly HistoryRecorder recorder;
    private readonly NetworkFeedServer? feed;
    private readonly BootMemoryReport? bootReport;
    private readonly BetterTaskManager.Core.Disk.CacheTrimmer? cacheTrimmer;
    private readonly BetterTaskManager.Core.Native.StandbyLimiter? standbyLimiter;
    private readonly BetterTaskManager.Core.Native.MemoryRuleEnforcer? memoryRules;
    private readonly BetterTaskManager.Core.Firewall.WfpBlocker.DropWatcher? drops;
    private readonly BlockedRecorder? blocked;
    private readonly Action<string> log;
    private readonly object gate = new();
    private DateTime lastError;
    private bool disposed, reportedTraffic;

    /// <param name="publishFeed">Serve live traffic to non-elevated app windows (the real service; off for console tests).</param>
    /// <param name="bootReport">Writes the memory breakdown shortly after startup (the real service at boot; console tests).</param>
    /// <param name="trimCache">Keep big files out of RAM after they were written, enforce the standby limit and the per-app memory rules (the real service; console tests opt in).</param>
    public HistoryWorker(string databasePath, string sessionPrefix, Action<string> log, bool publishFeed = false, BootMemoryReport? bootReport = null,
        bool trimCache = false, bool recordBlocked = false)
    {
        if (trimCache)
        {
            cacheTrimmer = new BetterTaskManager.Core.Disk.CacheTrimmer(log);
            standbyLimiter = new BetterTaskManager.Core.Native.StandbyLimiter(log);
            memoryRules = new BetterTaskManager.Core.Native.MemoryRuleEnforcer(log);
        }
        this.bootReport = bootReport;
        if (publishFeed) feed = new NetworkFeedServer(log);
        this.log = log;
        store = HistoryStore.OpenForWriting(databasePath);
        engine = new MonitorEngine(sessionPrefix) { Interval = TimeSpan.FromSeconds(2), ReadProcessDetails = false, ReadGpu = false, ReadDisks = false };
        recorder = new HistoryRecorder(store, engine.ResolveHost, engine.TryGetTracedProcess);
        if (recordBlocked)
        {
            drops = new BetterTaskManager.Core.Firewall.WfpBlocker.DropWatcher();
            blocked = new BlockedRecorder(store, address => engine.ResolveHost(address)?.Name);
        }
        engine.DnsQueryAnswered += recorder.RecordDnsQuery;
        engine.SnapshotReady += OnSnapshot;
        engine.CollectionFailed += OnError;
    }

    public void Start()
    {
        // Drops first: the other parts take seconds to start, and drops are only seen once subscribed.
        if (drops is not null)
        {
            try
            {
                drops.Start();
                log("Blocked log: recording firewall drops");
            }
            catch (Exception ex) when (ex is BetterTaskManager.Core.Firewall.WfpException or EntryPointNotFoundException or DllNotFoundException)
            {
                log("Blocked log: cannot subscribe to firewall drops: " + ex.Message);
            }
        }
        feed?.Start();
        engine.Start();
        cacheTrimmer?.Start();
        standbyLimiter?.Start();
    }

    private void OnSnapshot(MonitorSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed) return;
            if (!reportedTraffic)
            {
                reportedTraffic = true;
                if (!snapshot.System.PerProcessNetworkAvailable) log("Traffic is not measured: " + snapshot.System.PerProcessNetworkStatus);
            }
            recorder.Record(snapshot);
            RecordBlocked(snapshot);
        }
        bootReport?.Offer(snapshot);
        memoryRules?.Apply(snapshot);
        if (feed is not null)
        {
            feed.Publish(NetworkFeedMessage.From(snapshot));
            // Match the app's refresh while someone is watching; otherwise save work.
            engine.Interval = feed.ReaderCount > 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2);
        }
    }

    /// <summary>Firewall drops since the last snapshot, with the path spelled as the running process spells it.</summary>
    private void RecordBlocked(MonitorSnapshot snapshot)
    {
        if (drops is null || blocked is null) return;
        try
        {
            List<BetterTaskManager.Core.Firewall.BlockedEvent> events = drops.Drain();
            if (drops.Overflow > 0) log($"Blocked log: {drops.Overflow} drops were not recorded (too many at once)");
            if (events.Count > 0)
            {
                var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ProcessSample process in snapshot.Processes) if (process.Path.Length > 0) spelling.TryAdd(process.Path, process.Path);
                blocked.Record(events, path => spelling.GetValueOrDefault(path, path));
            }
            blocked.Flush(DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            OnError(ex);
        }
    }

    private void OnError(Exception ex)
    {
        // At most one entry a minute, so a persistent failure cannot fill the disk.
        if (DateTime.UtcNow - lastError < TimeSpan.FromMinutes(1)) return;
        lastError = DateTime.UtcNow;
        log("Collection failed: " + ex);
    }

    public void Dispose()
    {
        standbyLimiter?.Dispose();
        cacheTrimmer?.Dispose();
        drops?.Dispose();
        feed?.Dispose();
        engine.Dispose();
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try
            {
                recorder.Dispose();
            }
            catch (Exception ex)
            {
                log("Final write failed: " + ex.Message);
            }
            store.Dispose();
        }
    }
}
