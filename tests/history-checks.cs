#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// Regression checks for history recording and DNS attribution (findings from the 2026-10-17 review).
// Run: dotnet run --file tests/history-checks.cs   (no administrator rights needed; uses temporary databases)
using System.Net;
using System.Reflection;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;
using Microsoft.Data.Sqlite;

int failures = 0;
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
    if (!ok) failures++;
}

DateTime start = DateTime.Now;
ProcessSample Process(int pid, string name, DateTime createdUtc) => new()
{
    Pid = pid, ParentPid = 0, ImageName = name + ".exe", CreateTime = createdUtc.ToFileTimeUtc(),
    Path = $@"C:\Test\{name}.exe", Description = name, Company = "Test"
};
MonitorSnapshot Snapshot(int second, IEnumerable<ProcessSample> processes, IEnumerable<ConnectionSample> connections, IEnumerable<FlowSample> flows) => new()
{
    Timestamp = start.AddSeconds(second),
    Processes = processes.ToList(),
    Connections = connections.ToList(),
    Flows = flows.ToList(),
    NetworkIssues = [],
    System = new SystemSample { Memory = new MemoryBreakdown() }
};
ConnectionSample Tcp(int pid) => new()
{
    Pid = pid, Protocol = "TCP", LocalAddress = "192.168.1.10", LocalPort = 50000, RemoteAddress = "203.0.113.7", RemotePort = 443, State = "Established"
};
FlowSample Flow(int pid, long received) => new()
{
    Pid = pid, Protocol = "TCP", LocalPort = 50000, RemoteAddress = "203.0.113.7", RemotePort = 443, Received = received
};
string NewDatabase() => Path.Combine(Path.GetTempPath(), $"nax-history-check-{Guid.NewGuid():N}.db");
List<ConnectionRecord> Rows(string db)
{
    using var reader = HistoryStore.OpenReadOnly(db)!;
    return reader.ReadConnections(start.AddDays(-1).ToUniversalTime(), null, null, 100, includeDns: false);
}

// 5: a connection that ended and came back on the same key before the flush keeps its own row and bytes.
{
    string db = NewDatabase();
    var a = Process(1000, "AppA", DateTime.UtcNow.AddMinutes(-10));
    using (var store = HistoryStore.OpenForWriting(db))
    {
        var recorder = new HistoryRecorder(store, (_, _) => null);
        recorder.Record(Snapshot(0, [a], [Tcp(1000)], [Flow(1000, 100)]));
        recorder.Record(Snapshot(1, [a], [], []));
        recorder.Record(Snapshot(2, [a], [Tcp(1000)], [Flow(1000, 200)]));
        recorder.Flush();
    }
    var rows = Rows(db);
    Check("5 reused key before flush", rows.Count == 2 && rows.Sum(r => r.BytesIn) == 300, $"{rows.Count} rows, {rows.Sum(r => r.BytesIn)} bytes (want 2 rows, 300 bytes)");
}

// 6: a failed write keeps no row id, so the retry inserts the connection instead of updating a missing row.
{
    string db = NewDatabase();
    var a = Process(1001, "AppA", DateTime.UtcNow.AddMinutes(-10));
    using (var store = HistoryStore.OpenForWriting(db))
    {
        var recorder = new HistoryRecorder(store, (_, _) => null);
        using (var sabotage = new SqliteConnection($"Data Source={db}"))
        {
            sabotage.Open();
            using var command = sabotage.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_usage BEFORE INSERT ON app_usage BEGIN SELECT RAISE(ABORT, 'injected'); END;";
            command.ExecuteNonQuery();
        }
        recorder.Record(Snapshot(0, [a], [Tcp(1001)], [Flow(1001, 100)]));
        bool failed = false;
        try { recorder.Flush(); } catch (SqliteException) { failed = true; }
        using (var repair = new SqliteConnection($"Data Source={db}"))
        {
            repair.Open();
            using var command = repair.CreateCommand();
            command.CommandText = "DROP TRIGGER fail_usage;";
            command.ExecuteNonQuery();
        }
        recorder.Flush();
        var rows = Rows(db);
        Check("6 retry after failed write", failed && rows.Count == 1 && rows[0].BytesIn == 100, $"first write failed: {failed}; {rows.Count} rows, {rows.Sum(r => r.BytesIn)} bytes (want 1 row, 100 bytes)");
    }
}

// 7: a PID reused by a short-lived process that only the kernel trace saw is credited to the new process.
{
    string db = NewDatabase();
    var a = Process(1002, "AppA", DateTime.UtcNow.AddMinutes(-10));
    var traced = new BandwidthMonitor.TracedProcess("AppB.exe", @"C:\Test\AppB.exe", DateTime.UtcNow, DateTime.UtcNow.AddSeconds(-1));
    using (var store = HistoryStore.OpenForWriting(db))
    {
        HistoryRecorder.TracedProcessLookup lookup = (int pid, out BandwidthMonitor.TracedProcess process) =>
        {
            process = traced;
            return pid == 1002;
        };
        var recorder = new HistoryRecorder(store, (_, _) => null, lookup);
        recorder.Record(Snapshot(0, [a], [], []));
        recorder.Record(Snapshot(1, [], [], [new FlowSample { Pid = 1002, Protocol = "UDP", LocalPort = 50001, RemoteAddress = "203.0.113.8", RemotePort = 53, Received = 50 }]));
        recorder.Flush();
    }
    var rows = Rows(db);
    string app = rows.FirstOrDefault()?.AppPath ?? "(none)";
    Check("7 PID reuse between snapshots", app.EndsWith("AppB.exe", StringComparison.OrdinalIgnoreCase), $"traffic credited to {app} (want AppB)");
}

// 8: DNS lookups of an earlier process are not credited to a new process that got the same PID.
{
    var resolver = new HostNameResolver("NaxTest-HistoryChecks");
    var byProcess = (System.Collections.IDictionary)typeof(HostNameResolver).GetField("byProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resolver)!;
    var ip = IPAddress.Parse("203.0.113.9");
    var own = new Dictionary<IPAddress, (string Name, DateTime At)> { [ip] = ("old-process.example", DateTime.UtcNow.AddHours(-1)) };
    byProcess[1003] = own;
    bool before = resolver.Resolve("203.0.113.9", 1003) is { Name: "old-process.example", Source: HostNameSource.Lookup };
    resolver.ProcessStarted(1003, DateTime.UtcNow.AddMinutes(-1));
    bool after = resolver.Resolve("203.0.113.9", 1003) is { Name: "old-process.example", Source: HostNameSource.Lookup };
    Check("8 DNS lookups scoped to the process", before && !after, $"own lookup before new process: {before}, after: {after} (want true, false)");
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
