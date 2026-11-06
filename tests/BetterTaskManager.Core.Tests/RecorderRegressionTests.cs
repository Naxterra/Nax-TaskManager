using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;
using BetterTaskManager.Fluent.Services;
using Microsoft.Data.Sqlite;

namespace BetterTaskManager.Core.Tests;

internal static class RecorderRegressionTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("blocked_failed_insert_keeps_multiple_replacements", "A partially executed transaction rolls back; three generations of one group survive and repeated flushes never double counts.", () => StoreTests.InTempFolder(folder =>
        {
            string file = Path.Combine(folder, "history.db");
            using var store = HistoryStore.OpenForWriting(file);
            var recorder = new BlockedRecorder(store, _ => null);
            DateTime now = DateTime.UtcNow;
            recorder.Record([Drop(now), Drop(now, "203.0.113.8")], path => path);
            Sql(file, "CREATE TRIGGER fail_write BEFORE INSERT ON blocked WHEN NEW.remote_address = '203.0.113.8' BEGIN SELECT RAISE(FAIL, 'injected'); END");
            ExpectWriteFailure(() => recorder.Flush(now));
            Check.Equal(0, store.ReadBlocked(now.AddDays(-1), null, 100).Count);
            recorder.Record([Drop(now.AddMinutes(11)), Drop(now.AddMinutes(22))], path => path);
            ExpectWriteFailure(() => recorder.Flush(now.AddMinutes(22)));
            Sql(file, "DROP TRIGGER fail_write");
            recorder.Flush(now.AddMinutes(22));
            var rows = store.ReadBlocked(now.AddDays(-1), null, 100);
            Check.Equal(4, rows.Count);
            Check.Equal(4, rows.Sum(row => row.Count));
            Check.Equal(3, rows.Count(row => row.RemoteAddress == "203.0.113.7"));
            recorder.Flush(now.AddMinutes(22));
            recorder.Record([Drop(now.AddMinutes(23))], path => path);
            recorder.Flush(now.AddMinutes(23));
            rows = store.ReadBlocked(now.AddDays(-1), null, 100);
            Check.Equal(4, rows.Count);
            Check.Equal(5, rows.Sum(row => row.Count));
        }));

        yield return ("blocked_failed_update_keeps_persisted_group", "A failed update followed by two replacements retries the original ID and its grown count without duplicate inserts.", () => StoreTests.InTempFolder(folder =>
        {
            string file = Path.Combine(folder, "history.db");
            using var store = HistoryStore.OpenForWriting(file);
            var recorder = new BlockedRecorder(store, _ => null);
            DateTime now = DateTime.UtcNow;
            recorder.Record([Drop(now)], path => path);
            recorder.Flush(now);
            long id = store.ReadBlocked(now.AddDays(-1), null, 100).Single().Id;
            recorder.Record([Drop(now.AddMinutes(1))], path => path);
            Sql(file, "CREATE TRIGGER fail_write BEFORE UPDATE ON blocked BEGIN SELECT RAISE(ABORT, 'injected'); END");
            ExpectWriteFailure(() => recorder.Flush(now.AddMinutes(1)));
            recorder.Record([Drop(now.AddMinutes(12)), Drop(now.AddMinutes(23))], path => path);
            // The newest insert executes before the rejected update. Its provisional ID must also be forgotten.
            ExpectWriteFailure(() => recorder.Flush(now.AddMinutes(23)));
            Check.Equal(1, store.ReadBlocked(now.AddDays(-1), null, 100).Single().Count);
            Sql(file, "DROP TRIGGER fail_write");
            recorder.Flush(now.AddMinutes(23));
            recorder.Flush(now.AddMinutes(24));
            var rows = store.ReadBlocked(now.AddDays(-1), null, 100);
            Check.Equal(3, rows.Count);
            Check.Equal(4, rows.Sum(row => row.Count));
            Check.Equal(2, rows.Single(row => row.Id == id).Count);
        }));

        yield return ("tcp_addresses_keep_separate_rates_flows_and_history", "Two local addresses share the other endpoints. Trace counts, table rates, feed smoothing and stored bytes remain separate; per-app totals count each byte once.", () => StoreTests.InTempFolder(folder =>
        {
            using var monitor = new BandwidthMonitor("NaxTests-NoTrace");
            using var engine = new MonitorEngine("NaxTests-NoTrace");
            var process = Process();
            List<ConnectionSample> connections = [Tcp("192.168.1.10"), Tcp("192.168.2.10"), Tcp("192.168.1.10")];
            foreach (var (local, received, sent) in new[] { ("192.168.1.10", 100, 40), ("192.168.2.10", 300, 60) })
            {
                monitor.Add(1234, true, true, received, IPAddress.Parse(local), 50000, IPAddress.Parse("203.0.113.7"), 443);
                monitor.Add(1234, true, false, sent, IPAddress.Parse(local), 50000, IPAddress.Parse("203.0.113.7"), 443);
            }
            var counts = monitor.Drain();
            Check.Equal(2, counts.Sockets.Count);
            Check.Equal(400L, counts.Processes[1234].Received);
            var flows = engine.ApplyTraffic([process], connections, 2, counts.Processes, counts.Sockets);
            Check.Equal(2, flows.Count);
            Check.Equal(50d, connections[0].ReceiveBytesPerSecond);
            Check.Equal(150d, connections[1].ReceiveBytesPerSecond);
            Check.Equal(0d, connections[2].ReceiveBytesPerSecond);
            Check.Equal(20d, connections[0].SendBytesPerSecond);
            Check.Equal(30d, connections[1].SendBytesPerSecond);
            Check.Equal(400L, process.NetworkReceivedTotal);
            Check.Equal(100L, process.NetworkSentTotal);
            Check.Equal(200d, process.NetworkReceiveBytesPerSecond);
            DateTime now = DateTime.UtcNow;
            var snapshot = Snapshot(now, [process], connections, flows);
            NetworkFeedMessage message = NetworkFeedMessage.From(snapshot);
            string json = JsonSerializer.Serialize(message, NetworkFeedJson.Default.NetworkFeedMessage);
            message = JsonSerializer.Deserialize(json, NetworkFeedJson.Default.NetworkFeedMessage)!;
            Check.Equal(2, message.Sockets.Count);
            Check.Equal(2, message.Sockets.Select(socket => socket.LocalAddress).Distinct().Count());
            using var client = new NetworkFeedClient();
            long ticks = Stopwatch.GetTimestamp();
            client.Accept(message, ticks);
            client.Accept(message with { Sockets = message.Sockets.Select(socket => socket with { ReceivePerSecond = socket.ReceivePerSecond * 3 }).ToList() }, ticks + Stopwatch.Frequency);
            var uiRows = new List<ConnectionSample> { Tcp("192.168.2.10"), Tcp("192.168.1.10") };
            MonitorEngine.ApplyFeed([Process()], uiRows, client.Current!);
            Check.Equal(300d, uiRows[0].ReceiveBytesPerSecond);
            Check.Equal(100d, uiRows[1].ReceiveBytesPerSecond);
            using var store = HistoryStore.OpenForWriting(Path.Combine(folder, "history.db"));
            using var recorder = new HistoryRecorder(store, (_, _) => null);
            recorder.Record(snapshot);
            recorder.Flush();
            var rows = store.ReadConnections(now.AddDays(-1), null, null, 100);
            Check.Equal(2, rows.Count);
            Check.Equal(100L, rows.Single(row => row.LocalAddress == "192.168.1.10").BytesIn);
            Check.Equal(300L, rows.Single(row => row.LocalAddress == "192.168.2.10").BytesIn);
            var usage = store.ReadAppUsage(now.ToLocalTime().Date.AddDays(-1));
            Check.Equal(400L, usage.Sum(row => row.BytesIn));
            Check.Equal(100L, usage.Sum(row => row.BytesOut));
            Check.Equal(0, monitor.Drain().Sockets.Count);
        }));

        yield return ("tcp_local_addresses_close_and_reopen_independently", "Closing one address keeps its sibling open; reopening creates one new row even before a flush, and PID reuse creates another identity.", () => StoreTests.InTempFolder(folder =>
        {
            using var store = HistoryStore.OpenForWriting(Path.Combine(folder, "history.db"));
            using var recorder = new HistoryRecorder(store, (_, _) => null);
            DateTime now = DateTime.UtcNow;
            ProcessSample process = Process();
            recorder.Record(Snapshot(now, [process], [Tcp("192.168.1.10"), Tcp("192.168.2.10")], [Flow("192.168.1.10", 100), Flow("192.168.2.10", 200)]));
            recorder.Record(Snapshot(now.AddSeconds(1), [process], [Tcp("192.168.2.10")], []));
            recorder.Record(Snapshot(now.AddSeconds(2), [process], [Tcp("192.168.1.10"), Tcp("192.168.2.10")], [Flow("192.168.1.10", 300)]));
            recorder.Flush();
            var rows = store.ReadConnections(now.AddDays(-1), null, null, 100);
            Check.Equal(3, rows.Count);
            Check.Equal(200L, rows.Single(row => row.LocalAddress == "192.168.2.10").BytesIn);
            var first = rows.Single(row => row.LocalAddress == "192.168.1.10" && row.State == "Closed");
            Check.Equal(100L, first.BytesIn);
            Check.Equal(300L, rows.Single(row => row.LocalAddress == "192.168.1.10" && row.State == "Established").BytesIn);
            recorder.Record(Snapshot(now.AddSeconds(3), [Process(2)], [Tcp("192.168.1.10")], [Flow("192.168.1.10", 400)]));
            recorder.Flush();
            rows = store.ReadConnections(now.AddDays(-1), null, null, 100);
            Check.Equal(4, rows.Count);
            Check.Equal(1000L, rows.Sum(row => row.BytesIn));
        }));

        yield return ("legacy_feed_attribution_is_unambiguous_and_pid_safe", "Old JSON without local addresses still loads; unique sockets get rates, ambiguous sockets get none, and stale process identities never feed reused PIDs.", () =>
        {
            const string json = """{"TimestampUtc":"2026-11-06T00:00:00Z","Processes":[{"Pid":1234,"CreateTime":1,"ReceivePerSecond":100,"SendPerSecond":20,"ReceivedTotal":400,"SentTotal":80}],"Sockets":[{"Pid":1234,"Protocol":"TCP","LocalPort":50000,"RemoteAddress":"203.0.113.7","RemotePort":443,"ReceivePerSecond":100,"SendPerSecond":20}]}""";
            var message = JsonSerializer.Deserialize(json, NetworkFeedJson.Default.NetworkFeedMessage)!;
            var single = Tcp("192.168.1.10");
            MonitorEngine.ApplyFeed([Process()], [single], message);
            Check.Equal(100d, single.ReceiveBytesPerSecond);
            List<ConnectionSample> ambiguous = [Tcp("192.168.1.10"), Tcp("192.168.2.10")];
            ProcessSample process = Process();
            MonitorEngine.ApplyFeed([process], ambiguous, message);
            Check.Equal(0d, ambiguous.Sum(row => row.ReceiveBytesPerSecond));
            Check.Equal(400L, process.NetworkReceivedTotal);
            using var client = new NetworkFeedClient();
            long ticks = Stopwatch.GetTimestamp();
            client.Accept(message, ticks);
            client.Accept(message with { Processes = [new ProcessTraffic(1234, 2, 0, 0, 0, 0)], Sockets = [] }, ticks + Stopwatch.Frequency);
            var reused = Tcp("192.168.1.10");
            MonitorEngine.ApplyFeed([Process(2)], [reused], client.Current!);
            Check.Equal(0d, reused.ReceiveBytesPerSecond);
            var exactOld = message with { Sockets = [message.Sockets[0] with { LocalAddress = "192.168.1.10", CreateTime = 1 }] };
            MonitorEngine.ApplyFeed([Process(2)], [reused], exactOld);
            Check.Equal(0d, reused.ReceiveBytesPerSecond);
        });

        yield return ("udp_grouping_dns_and_ipv6_survive_socket_identity_change", "IPv6 TCP rates stay separate. UDP ports and local addresses still form one history flow per app and remote endpoint; DNS answers retain their rows.", () => StoreTests.InTempFolder(folder =>
        {
            using var monitor = new BandwidthMonitor("NaxTests-NoTrace");
            using var engine = new MonitorEngine("NaxTests-NoTrace");
            IPAddress remote = IPAddress.Parse("2001:db8::7");
            monitor.Add(1234, true, true, 80, IPAddress.Parse("2001:db8:1::10"), 50000, remote, 443);
            monitor.Add(1234, true, false, 120, IPAddress.Parse("2001:db8:2::10"), 50000, remote, 443);
            monitor.Add(1234, false, true, 20, IPAddress.Parse("192.168.1.10"), 53000, IPAddress.Parse("203.0.113.7"), 53);
            monitor.Add(1234, false, true, 30, IPAddress.Parse("192.168.2.10"), 53000, IPAddress.Parse("203.0.113.7"), 53);
            ConnectionSample V6(string local) => new() { Pid = 1234, Protocol = "TCP", LocalAddress = local, LocalPort = 50000, RemoteAddress = remote.ToString(), RemotePort = 443, State = "Established" };
            ConnectionSample Udp(string local) => new() { Pid = 1234, Protocol = "UDP", LocalAddress = local, LocalPort = 53000, RemoteAddress = "", RemotePort = 0, State = "" };
            List<ConnectionSample> connections = [V6("2001:db8:1::10"), V6("2001:db8:2::10"), Udp("0.0.0.0"), Udp("192.168.2.10")];
            var counts = monitor.Drain();
            var flows = engine.ApplyTraffic([Process()], connections, 2, counts.Processes, counts.Sockets);
            Check.Equal(40d, connections[0].ReceiveBytesPerSecond);
            Check.Equal(60d, connections[1].SendBytesPerSecond);
            Check.Equal(25d, connections[2].ReceiveBytesPerSecond);
            Check.Equal(0d, connections[3].ReceiveBytesPerSecond);
            var feedRows = new List<ConnectionSample> { V6("2001:db8:1::10"), V6("2001:db8:2::10"), Udp("0.0.0.0"), Udp("192.168.2.10") };
            MonitorEngine.ApplyFeed([Process()], feedRows, NetworkFeedMessage.From(Snapshot(DateTime.UtcNow, [Process()], connections, flows)));
            Check.Equal(25d, feedRows[2].ReceiveBytesPerSecond);
            Check.Equal(0d, feedRows[3].ReceiveBytesPerSecond);
            using var store = HistoryStore.OpenForWriting(Path.Combine(folder, "history.db"));
            using var recorder = new HistoryRecorder(store, (_, _) => null);
            DateTime now = DateTime.UtcNow;
            recorder.RecordDnsQuery(1234, "example.test", [IPAddress.Parse("203.0.113.7")]);
            recorder.Record(Snapshot(now, [Process()], connections, flows));
            recorder.Flush();
            var rows = store.ReadConnections(now.AddDays(-1), null, null, 100);
            Check.Equal(4, rows.Count);
            Check.Equal(50L, rows.Single(row => row.Protocol == "UDP").BytesIn);
            Check.Equal(0, rows.Single(row => row.Protocol == "UDP").LocalPort);
            Check.Equal("203.0.113.7", rows.Single(row => row.Protocol == "DNS").LocalAddress);
        }));

        yield return ("drain_sweeps_expired_processes_without_lookups", "A normal drain removes 10000 old exits, retains live/recent identities, and sweeps again after one elapsed minute without sleeping.", () =>
        {
            var clock = new TestClock();
            using var monitor = new BandwidthMonitor("NaxTests-NoTrace", clock);
            for (int pid = 100; pid < 10100; pid++) monitor.ProcessCache.Start(pid, new("old.exe", "old", clock.Now.AddHours(-1)));
            monitor.ProcessCache.Start(1, new("live.exe", "live", null));
            monitor.ProcessCache.Start(2, new("recent.exe", "recent", clock.Now.AddMinutes(-9)));
            monitor.Drain();
            Check.Equal(2, monitor.ProcessCache.Count);
            Check.That(monitor.TryGetProcess(1, out _) && monitor.TryGetProcess(2, out _), "live and recent identities lost");
            monitor.ProcessCache.Start(3, new("old.exe", "old", clock.Now.AddHours(-1)));
            clock.Advance(TimeSpan.FromSeconds(59));
            monitor.Drain();
            Check.Equal(3, monitor.ProcessCache.Count);
            clock.Advance(TimeSpan.FromSeconds(2));
            monitor.Drain();
            Check.Equal(1, monitor.ProcessCache.Count);
            Check.That(monitor.TryGetProcess(1, out _), "live identity expired");
        });

        yield return ("cache_expiration_cannot_remove_reused_pid", "A stale sweep candidate cannot delete a replacement identity; lookups also expire old entries, and stops retain recent names.", () =>
        {
            var clock = new TestClock();
            var cache = new TracedProcessCache(clock);
            var old = new BandwidthMonitor.TracedProcess("old.exe", "old", clock.Now.AddHours(-1), clock.Now.AddHours(-2));
            cache.Start(42, old);
            var replacement = new BandwidthMonitor.TracedProcess("new.exe", "new", null, clock.Now);
            cache.Start(42, replacement);
            Check.That(!cache.RemoveExpired(42, old, clock.Now), "stale candidate removed the new identity");
            Check.That(cache.TryGet(42, out var actual) && actual == replacement, "replacement was lost");
            cache.Stop(42);
            Check.That(cache.TryGet(42, out actual) && actual.ExitedUtc == clock.Now, "recent exit missing");
            cache.Start(43, old);
            Check.That(!cache.TryGet(43, out _), "lookup retained expired process");
            clock.Advance(TimeSpan.FromMinutes(11));
            cache.Sweep();
            Check.Equal(0, cache.Count);
        });

        yield return ("blocked_rows_reuse_content_and_apply_diffs", "Unchanged records allocate no row content. Count/time changes update in place; order, removals, filters, empty/error results and the 300-row cap reconcile correctly.", Rows);
    }

    private static void Rows()
    {
        var cache = new BlockedLogRows<FakeRow>();
        var rows = new List<FakeRow>();
        int created = 0, updated = 0, rebuilt = 0;
        string time = "now";
        BlockedRecord Record(long id) => new(id, DateTime.UnixEpoch, DateTime.UnixEpoch, "app" + id, false, 6, "203.0.113.7", null, 443, 50000, true, "rule", 1);
        List<BlockedRecord> Apply(IEnumerable<BlockedRecord> records) => cache.Apply(rows, records, _ => time,
            model => { created++; return new FakeRow(model); }, (row, model) =>
            {
                updated++;
                if (!model.HasSameContentAs(row.Model.Record)) rebuilt++;
                row.Model = model;
            });
        var one = Record(1); var two = Record(2); var three = Record(3);
        Apply([one, two, three]);
        FakeRow a = rows[0], b = rows[1], c = rows[2];
        for (int refresh = 0; refresh < 20; refresh++) Apply([one with { }, two with { }, three with { }]);
        Check.Equal(3, created);
        Check.Equal(0, updated);
        one = one with { Count = 20, LastSeen = DateTime.UnixEpoch.AddMinutes(2) };
        Apply([three, one, two]);
        Check.That(ReferenceEquals(c, rows[0]) && ReferenceEquals(a, rows[1]) && ReferenceEquals(b, rows[2]), "reorder recreated rows");
        Check.Equal(20, a.Model.Record.Count);
        Check.Equal(1, updated);
        time = "one minute ago";
        Apply([three, one, two]);
        Check.Equal(4, updated);
        Check.Equal(time, a.Model.Time);
        Check.Equal(0, rebuilt);
        Apply([two with { RemoteHost = "changed.test", Reason = "changed" }, one]);
        Check.That(ReferenceEquals(b, rows[0]) && ReferenceEquals(a, rows[1]), "removal or content update lost identity");
        Check.Equal("changed.test", b.Model.Record.RemoteHost);
        Check.Equal(1, rebuilt);
        Apply([one]);
        Check.Equal(1, rows.Count);
        Check.That(ReferenceEquals(a, rows[0]), "filtered survivor rebuilt");
        Apply([]);
        Check.Equal(0, rows.Count);
        Check.Equal(300, Apply(Enumerable.Range(1, 301).Select(id => Record(id))).Count);
        int before = created;
        Apply(Enumerable.Range(1, 301).Select(id => Record(id)));
        Check.Equal(before, created);
        Apply(Enumerable.Range(2, 301).Select(id => Record(id)));
        Check.Equal(before + 1, created);
        Check.Equal(301L, rows[^1].Model.Record.Id);
        Check.Equal(300, rows.Count);
    }

    private sealed class FakeRow(BlockedRowModel model) { internal BlockedRowModel Model = model; }

    private sealed class TestClock : TimeProvider
    {
        internal DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        private long ticks;
        public override DateTimeOffset GetUtcNow() => new(Now);
        public override long GetTimestamp() => ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan elapsed) { Now += elapsed; ticks += elapsed.Ticks; }
    }

    private static BlockedEvent Drop(DateTime at, string remote = "203.0.113.7") => new(at, @"C:\Test\App.exe", false, 6, remote, 443, 50000, true, "WholeApp");
    private static void Sql(string file, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static void ExpectWriteFailure(Action write)
    {
        try { write(); }
        catch (SqliteException) { return; }
        throw new Exception("injected write should fail");
    }
    private static ProcessSample Process(long created = 1) => new() { Pid = 1234, ParentPid = 0, CreateTime = created, ImageName = "App.exe", Path = @"C:\Test\App.exe", Description = "App", Company = "Test" };
    private static ConnectionSample Tcp(string local) => new() { Pid = 1234, Protocol = "TCP", LocalAddress = local, LocalPort = 50000, RemoteAddress = "203.0.113.7", RemotePort = 443, State = "Established" };
    private static FlowSample Flow(string local, long received) => new() { Pid = 1234, Protocol = "TCP", LocalAddress = local, LocalPort = 50000, RemoteAddress = "203.0.113.7", RemotePort = 443, Received = received };
    private static MonitorSnapshot Snapshot(DateTime at, List<ProcessSample> processes, List<ConnectionSample> connections, List<FlowSample> flows) => new()
    {
        Timestamp = at, Processes = processes, Connections = connections, Flows = flows, NetworkIssues = [], System = new SystemSample { Memory = new() }
    };
}
