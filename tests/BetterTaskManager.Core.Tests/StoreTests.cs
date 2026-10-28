using System.Text.Json;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Tests;

/// <summary>Stored files survive damage and crashes: rules, memory rules, the history database and atomic writes.</summary>
internal static class StoreTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("network_rules_save_over_damaged_file_is_refused_and_file_kept",
            "Saving a rule while app-rules.json is filled with zero bytes fails and leaves the damaged file as it was, so no other app's entry is silently dropped.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "app-rules.json");
                File.WriteAllBytes(file, new byte[64]);
                bool refused = false;
                try
                {
                    NetworkPolicyStore.Save(new AppNetworkPolicy { Path = @"C:\Apps\C.exe", BlockInternet = true }, file);
                }
                catch (InvalidDataException)
                {
                    refused = true;
                }
                Check.That(refused, "saving over a damaged file must be refused");
                Check.That(File.ReadAllBytes(file).All(b => b == 0), "the damaged file must stay untouched");
            }));

        yield return ("network_rules_save_keeps_the_other_apps",
            "Saving rules for app C after A and B keeps A and B in the file, and removing C's rules removes only C.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "app-rules.json");
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = @"C:\Apps\A.exe", BlockLan = true }, file);
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = @"C:\Apps\B.exe", Outgoing = ["- *"] }, file);
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = @"C:\Apps\C.exe", BlockInternet = true }, file);
                Check.Equal(3, NetworkPolicyStore.Read(file).Count);
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = @"C:\Apps\C.exe" }, file);
                List<AppNetworkPolicy> left = NetworkPolicyStore.Read(file);
                Check.That(left.Select(p => p.Path).SequenceEqual([@"C:\Apps\A.exe", @"C:\Apps\B.exe"]), "A and B must stay, C must go");
            }));

        yield return ("memory_rules_damaged_file_is_set_aside_and_new_rule_saved",
            "A damaged memory-rules.json is kept as a .damaged copy, and the new rule is saved in a fresh file.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "memory-rules.json");
                File.WriteAllText(file, "{ not json");
                MemoryRuleStore.Save(new AppMemoryRule { Path = @"C:\Apps\A.exe", LimitMegabytes = 512 }, file);
                Check.Equal(1, Directory.GetFiles(folder, "memory-rules.json.damaged-*").Length);
                List<AppMemoryRule> rules = MemoryRuleStore.Load(file);
                Check.That(rules.Count == 1 && rules[0].LimitMegabytes == 512, "the new rule must be the only one");
            }));

        yield return ("atomic_write_replaces_content_and_leaves_no_temporary_file",
            "Writing a file twice leaves only the second content and no .tmp file next to it.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "settings.json");
                AtomicFile.WriteAllText(file, "first");
                AtomicFile.WriteAllText(file, "second");
                Check.Equal("second", File.ReadAllText(file));
                Check.That(!File.Exists(file + ".tmp"), "no temporary file may be left behind");
            }));

        yield return ("damaged_history_database_is_set_aside_and_a_new_one_opens",
            "A history.db that is not a database is kept as a .damaged copy, and the service gets a new, working database instead of failing at every start.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "history.db");
                File.WriteAllText(file, "this is not a SQLite database, just text that fills the first page of the file");
                using (HistoryStore store = HistoryStore.OpenForWriting(file))
                {
                    store.Prune(TimeSpan.FromDays(30), DateTime.UtcNow);
                }
                Check.Equal(1, Directory.GetFiles(folder, "history.db.damaged-*").Length);
                using HistoryStore? reader = HistoryStore.OpenReadOnly(file);
                Check.That(reader is not null && reader.ReadBlocked(DateTime.UtcNow.AddDays(-1), null, 10).Count == 0, "the new database must be readable and empty");
            }));

        yield return ("history_prune_after_clock_jumps_a_year_ahead_keeps_recent_history",
            "The clock jumps a year ahead within ten seconds of real time. The jump is stored as skew, pruning with it keeps a blocked entry from two days ago, and setting the clock back again removes the skew.",
            () => InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "history.db");
                DateTime now = DateTime.UtcNow;
                using HistoryStore store = HistoryStore.OpenForWriting(file);
                var recorder = new BlockedRecorder(store, _ => null);
                recorder.Record([new BlockedEvent(now.AddDays(-2), @"c:\tools\curl.exe", false, 6, "1.2.3.4", 443, 50000, true, PolicyReason.WholeApp)], path => path);
                recorder.Flush(now.AddDays(-2).AddMinutes(1));
                TimeSpan skew = ClockSkew.Update(store.ReadClockSkew(), TimeSpan.FromDays(365), TimeSpan.FromSeconds(10));
                store.WriteClockSkew(skew);
                Check.Equal(TimeSpan.FromDays(365) - TimeSpan.FromSeconds(10), store.ReadClockSkew());
                store.Prune(TimeSpan.FromDays(30), now.AddDays(365) - store.ReadClockSkew());
                Check.Equal(1, store.ReadBlocked(now.AddDays(-3), null, 10).Count);
                Check.Equal(TimeSpan.Zero, ClockSkew.Update(skew, -TimeSpan.FromDays(365), TimeSpan.FromSeconds(10)));
            }));

        yield return ("history_prune_ignores_sleep_and_small_clock_corrections",
            "A laptop asleep for eight hours (the tick count moves with the wall clock) and a time-server correction of two minutes leave the skew at zero, so pruning keeps its normal 30-day cutoff.",
            () =>
            {
                Check.Equal(TimeSpan.Zero, ClockSkew.Update(TimeSpan.Zero, TimeSpan.FromHours(8), TimeSpan.FromHours(8)));
                Check.Equal(TimeSpan.Zero, ClockSkew.Update(TimeSpan.Zero, TimeSpan.FromSeconds(130), TimeSpan.FromSeconds(10)));
            });
    }

    internal static void InTempFolder(Action<string> body)
    {
        string folder = Path.Combine(Path.GetTempPath(), "nax-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            body(folder);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
