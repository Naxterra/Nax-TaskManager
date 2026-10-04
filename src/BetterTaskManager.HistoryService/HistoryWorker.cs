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
    private readonly Action<string> log;
    private readonly object gate = new();
    private DateTime lastError;
    private bool disposed, reportedTraffic;

    /// <param name="publishFeed">Serve live traffic to non-elevated app windows (the real service; off for console tests).</param>
    public HistoryWorker(string databasePath, string sessionPrefix, Action<string> log, bool publishFeed = false)
    {
        if (publishFeed) feed = new NetworkFeedServer(log);
        this.log = log;
        store = HistoryStore.OpenForWriting(databasePath);
        engine = new MonitorEngine(sessionPrefix) { Interval = TimeSpan.FromSeconds(2) };
        recorder = new HistoryRecorder(store, engine.ResolveHost);
        engine.SnapshotReady += OnSnapshot;
        engine.CollectionFailed += OnError;
    }

    public void Start()
    {
        feed?.Start();
        engine.Start();
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
        }
        if (feed is not null)
        {
            feed.Publish(NetworkFeedMessage.From(snapshot));
            // Match the app's refresh while someone is watching; otherwise save work.
            engine.Interval = feed.ReaderCount > 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2);
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
