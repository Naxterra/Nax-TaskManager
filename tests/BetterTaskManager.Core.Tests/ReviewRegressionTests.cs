using System.Diagnostics;
using BetterTaskManager.Core;
using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Core.Tests;

internal static class ReviewRegressionTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("null_layout_settings_are_repaired", "Null dictionaries and entries keep usable defaults; valid preferences survive.", () =>
        {
            var settings = AppSettings.FromJson("""{"ColumnWidths":null,"ColumnOrder":{"Processes.":null},"Theme":null,"Language":null,"WindowWidth":0,"RefreshIntervalMilliseconds":100000,"ShowDetailsPane":false}""");
            Check.That(settings.ColumnWidths is not null && settings.ColumnOrder.Count == 0, "layout must be usable");
            Check.Equal("System", settings.Theme);
            Check.Equal("System", settings.Language);
            Check.Equal(1024, settings.WindowWidth);
            Check.Equal(10000, settings.RefreshIntervalMilliseconds);
            Check.That(!settings.ShowDetailsPane, "valid preference lost");
            settings = AppSettings.FromJson("""{"ColumnWidths":{"Good":180,"Bad":-5},"ColumnOrder":null}""");
            Check.Equal(180d, settings.ColumnWidths["Good"]);
            Check.Equal(1, settings.ColumnWidths.Count);
            Check.Equal(0, settings.ColumnOrder.Count);
        });
        yield return ("blocked_search_precedes_limit", "An older match survives 600 newer groups, including friendly names, Unicode, exact paths and literal wildcard characters.", () => StoreTests.InTempFolder(folder =>
        {
            using var store = HistoryStore.OpenForWriting(Path.Combine(folder, "history.db"));
            DateTime at = DateTime.UtcNow;
            var groups = Enumerable.Range(0, 601).Select(i => new BlockedRecord(0, at.AddSeconds(i), at.AddSeconds(i),
                i == 0 ? @"C:\Apps\Needle_100%.exe" : @"C:\Apps\Busy.exe", false, 6, "203.0.113.1", null, 443, 50000, i != 1, "test", 1)).ToList();
            store.WriteBlocked(groups);
            var result = store.ReadBlocked(at.AddMinutes(-1), null, 1, search: "Needle_100%");
            Check.Equal(1, result.Count);
            Check.That(result[0].AppPath.Contains("Needle"), "older match lost");
            Check.Equal(1, store.ReadBlocked(at.AddMinutes(-1), null, 1, search: "Über", appName: p => p.Contains("Needle") ? "Über App" : "Busy").Count);
            Check.That(store.ReadBlocked(at.AddMinutes(-1), null, 700, oursOnly: true).All(x => x.Ours), "other firewall leaked into filter");
            Check.Equal(1, store.ReadBlocked(at.AddMinutes(-1), @"c:\apps\needle_100%.EXE", 1).Count);
            Check.Equal(0, store.ReadBlocked(at.AddMinutes(-1), null, 1, search: "absent").Count);
        }));
        yield return ("clock_rollback_expires_old_feed_rates", "After a one-hour rollback, old rates expire after three elapsed seconds; totals use the newest frame.", () =>
        {
            using var client = new NetworkFeedClient();
            long ticks = Stopwatch.GetTimestamp();
            DateTime wall = DateTime.UtcNow;
            NetworkFeedMessage Frame(DateTime date, double rate, long total) => new(date, [new ProcessTraffic(42, 1, rate, 0, total, 0)], []);
            client.Accept(Frame(wall, 6000, 6000), ticks);
            client.Accept(Frame(wall.AddHours(-1), 0, 7000), ticks + Stopwatch.Frequency);
            Check.That(client.Current!.Processes[0].ReceivePerSecond > 0, "normal smoothing lost");
            client.Accept(Frame(wall.AddHours(-1).AddSeconds(5), 0, 7000), ticks + 5 * Stopwatch.Frequency);
            Check.Equal(0d, client.Current!.Processes.Single().ReceivePerSecond);
            Check.Equal(7000L, client.Current.Processes.Single().ReceivedTotal);
        });
        yield return ("trace_stops_when_disabled_during_startup", "Leaving a page during blocked trace startup stops it afterwards, and disposal prevents queued restarts.", () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int starts = 0, stops = 0;
            var errors = new List<Exception>();
            using var activity = new BackgroundActivity(() => { Interlocked.Increment(ref starts); entered.Set(); Check.That(release.Wait(5000), "startup gate timed out"); return true; },
                () => Interlocked.Increment(ref stops), errors.Add);
            try
            {
                activity.SetEnabled(true);
                Check.That(entered.Wait(5000), "trace never started");
                activity.SetEnabled(false);
            }
            finally { release.Set(); }
            Check.That(activity.Completion.Wait(5000), "trace did not settle");
            Check.Equal(1, starts);
            Check.Equal(1, stops);
            Check.Equal(0, errors.Count);
            activity.Dispose();
            activity.SetEnabled(true);
            Check.Equal(1, starts);
        });
        yield return ("disposing_during_startup_stops_the_trace", "Closing the app while startup is pending waits for startup and shuts down the trace exactly once.", () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int stops = 0;
            using var activity = new BackgroundActivity(() => { entered.Set(); return release.Wait(5000); }, () => Interlocked.Increment(ref stops), _ => { });
            activity.SetEnabled(true);
            Check.That(entered.Wait(5000), "startup never entered");
            Task disposal = Task.Run(activity.Dispose);
            release.Set();
            Check.That(disposal.Wait(5000), "shutdown hung");
            Check.Equal(1, stops);
        });
        yield return ("connections_sort_across_apps", "App A has connections 6 and 5, app B has 10: the combined table orders 10, 6, 5 and sorts remote hosts globally.", () =>
        {
            ConnectionSample Socket(int pid, string host, double speed) => new() { Pid = pid, Protocol = "TCP", LocalAddress = "127.0.0.1", LocalPort = pid,
                RemoteAddress = host, RemotePort = 443, State = "Established", ReceiveBytesPerSecond = speed };
            var rows = new[] { Socket(1, "a", 6), Socket(1, "z", 5), Socket(2, "m", 10) };
            Check.That(ConnectionOrdering.Sort(rows, "Speed", true).Select(c => c.ReceiveBytesPerSecond).SequenceEqual(new double[] { 10, 6, 5 }), "speed not global");
            Check.That(ConnectionOrdering.Sort(rows, "Remote", false).Select(c => c.RemoteAddress).SequenceEqual(new[] { "a", "m", "z" }), "hosts not global");
            Check.That(ConnectionOrdering.Sort(rows, "Speed", false).Select(c => c.ReceiveBytesPerSecond).SequenceEqual(new double[] { 5, 6, 10 }), "ascending speed broken");
        });
        yield return ("chart_window_uses_elapsed_time", "A 60-second chart retains a full minute at 250 ms or 10 s refresh, spaces mixed intervals accurately and ignores repeated snapshots.", () =>
        {
            foreach (double step in new[] { .25, 1d, 10d })
            {
                var history = new HistoryBuffer(60);
                for (double seconds = 0; seconds <= 120; seconds += step) history.Add(seconds, (long)(seconds * Stopwatch.Frequency));
                Check.Equal(60d, history.AgeSeconds(0));
                Check.Equal((int)(60 / step) + 1, history.Count);
            }
            var mixed = new HistoryBuffer(60);
            mixed.Add(1, 0); mixed.Add(2, Stopwatch.Frequency); mixed.Add(3, 11 * Stopwatch.Frequency);
            mixed.Add(100, 11 * Stopwatch.Frequency);
            Check.Equal(3, mixed.Count);
            Check.Equal(10d, mixed.AgeSeconds(1));
            mixed.Add(4, 72 * Stopwatch.Frequency);
            Check.Equal(1, mixed.Count);
        });
        yield return ("csv_preserves_text_and_safe_spreadsheet_values", "CSV quotes commas, quotes and newlines, uses invariant numbers, neutralizes formula text and preserves the destination on failure.", () => StoreTests.InTempFolder(folder =>
        {
            string path = Path.Combine(folder, "out.csv");
            DateTime now = DateTime.UtcNow;
            var record = new BlockedRecord(1, now, now, @"C:\Apps\Über.exe", false, 6, "203.0.113.1", "=SUM(1,2)", 443, 99, true, "a,\"b\"\r\nc", 123);
            CsvExport.Save(path, writer => CsvExport.Blocked(writer, [record]));
            string text = File.ReadAllText(path);
            Check.That(text.Contains("\"'=SUM(1,2)\""), "formula not neutralized");
            Check.That(text.Contains("\"a,\"\"b\"\"\r\nc\""), "quoting corrupted");
            Check.That(text.Contains("Über.exe") && text.Contains("\"123\""), "data lost");
            try { CsvExport.Save(path, writer => { writer.Write("partial"); throw new IOException("injected"); }); }
            catch (IOException) { }
            Check.Equal(text, File.ReadAllText(path));
            Check.Equal(1, Directory.GetFiles(folder).Length);
        }));
    }
}
