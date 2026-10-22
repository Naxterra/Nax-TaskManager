#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// Blocked log: grouping of firewall drops and the blocked table in a temporary database. No firewall changes.
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;

int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}

string folder = Path.Combine(Path.GetTempPath(), "nax-blocked-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string database = Path.Combine(folder, "history.db");
try
{
    DateTime now = DateTime.UtcNow;
    DateTime t0 = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(-30);
    const string app = @"C:\Tools\Curl.exe";
    BlockedEvent Drop(int minutes, string reason = PolicyReason.WholeApp, bool ours = true, string remote = "1.2.3.4", bool inbound = false, string path = @"c:\tools\curl.exe") =>
        new(t0.AddMinutes(minutes), path, inbound, 6, remote, 443, 50000 + minutes, ours, reason);

    using (HistoryStore store = HistoryStore.OpenForWriting(database))
    {
        var recorder = new BlockedRecorder(store, address => address == "1.2.3.4" ? "example.com" : null);
        Func<string, string> spelling = path => string.Equals(path, app, StringComparison.OrdinalIgnoreCase) ? app : path;
        recorder.Record([Drop(0), Drop(1), Drop(2)], spelling);
        recorder.Flush(t0.AddMinutes(3));
        recorder.Record([Drop(4)], spelling);
        recorder.Flush(t0.AddMinutes(5));
        recorder.Record([Drop(20)], spelling); // more than 10 minutes after the last one: a new group
        recorder.Record([Drop(21, "Rule:- * TCP/443")], spelling);
        recorder.Record([Drop(22, "Default Outbound", ours: false)], spelling);
        recorder.Record([Drop(23, PolicyReason.BlockInbound, inbound: true, remote: "192.168.1.9")], spelling);
        recorder.Flush(t0.AddMinutes(24));
    }

    using (HistoryStore reader = HistoryStore.OpenReadOnly(database)!)
    {
        List<BlockedRecord> all = reader.ReadBlocked(t0.AddHours(-1), null, 100);
        Check($"five groups recorded (got {all.Count})", all.Count == 5);
        BlockedRecord first = all.Single(r => r.Reason == PolicyReason.WholeApp && r.FirstSeen == t0);
        Check("repeats within 10 minutes count into one group", first.Count == 4 && first.LastSeen == t0.AddMinutes(4));
        Check("the path keeps the running process's spelling", first.AppPath == app);
        Check("the host name is stored", first.RemoteHost == "example.com");
        Check("newest first", all[0].LastSeen >= all[^1].LastSeen);
        Check("ours only leaves out other firewalls", reader.ReadBlocked(t0.AddHours(-1), null, 100, oursOnly: true).All(r => r.Ours) && reader.ReadBlocked(t0.AddHours(-1), null, 100, oursOnly: true).Count == 4);
        Check("app filter ignores case", reader.ReadBlocked(t0.AddHours(-1), @"C:\TOOLS\CURL.EXE", 100).Count == 5);
        Check("rule reason survives", all.Any(r => r.Reason == "Rule:- * TCP/443"));
        BlockedRecord inbound = all.Single(r => r.Inbound);
        Check("inbound drop keeps its local port", inbound.LocalPort == 50023 && inbound.RemoteHost is null);
        Check("since filter", reader.ReadBlocked(t0.AddMinutes(15), null, 100).Count == 4);
    }

    using (HistoryStore store = HistoryStore.OpenForWriting(database))
    {
        store.Prune(TimeSpan.FromMinutes(10), t0.AddMinutes(30));
    }
    using (HistoryStore reader = HistoryStore.OpenReadOnly(database)!)
    {
        Check("prune removes old groups", reader.ReadBlocked(t0.AddHours(-1), null, 100).Count == 4);
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(folder, true); } catch (IOException) { }
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
