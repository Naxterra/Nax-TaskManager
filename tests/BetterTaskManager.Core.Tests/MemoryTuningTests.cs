using System.Text.Json;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Tests;

internal static class MemoryTuningTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("application_prefetch_preserves_boot_prefetch", "Both application choices preserve the boot bit in all four Windows modes, and repeating a choice is stable.", () =>
        {
            foreach (int mode in Enumerable.Range(0, 4))
            foreach (bool enabled in new[] { false, true })
            {
                int next = PrefetchSettings.WithApplications(mode, enabled);
                Check.Equal(mode & 2, next & 2);
                Check.Equal(enabled, PrefetchSettings.ApplicationsEnabled(next, !enabled));
                Check.Equal(next, PrefetchSettings.WithApplications(next, enabled));
            }
            foreach (int invalid in new[] { -1, 4, int.MaxValue })
            {
                bool refused = false;
                try { PrefetchSettings.WithApplications(invalid, false); }
                catch (ArgumentOutOfRangeException) { refused = true; }
                Check.That(refused, "unrecognized Prefetch mode accepted");
            }
        });
        yield return ("prefetch_shows_saved_choice_while_runtime_needs_restart", "A saved mode overrides a stale MMAgent flag; when no saved mode exists the MMAgent value is retained.", () =>
        {
            foreach (int mode in Enumerable.Range(0, 4))
            foreach (bool reported in new[] { false, true })
                Check.Equal((mode & 1) != 0, PrefetchSettings.ApplicationsEnabled(mode, reported));
            Check.That(PrefetchSettings.ApplicationsEnabled(null, true), "missing mode hid the native setting");
            Check.That(!PrefetchSettings.ApplicationsEnabled(null, false), "missing mode guessed an enabled setting");
        });
        yield return ("legacy_memory_restore_keeps_original_application_choice", "Upgrading a legacy restore record preserves its original application flag and the current boot bit, and never replaces an already captured mode.", () =>
        {
            var original = JsonSerializer.Deserialize<MemoryManagerState>("""{"MemoryCompression":true,"PageCombining":false,"ApplicationLaunchPrefetching":false,"ApplicationPreLaunch":true,"SysMainRunning":true,"SysMainEnabled":true,"ReadAtUtc":"2026-01-01T00:00:00Z"}""")!;
            var captured = MemoryManager.CapturePrefetchMode(original, 3);
            Check.That(captured.PrefetchModeCaptured, "legacy record not upgraded");
            Check.Equal<int?>(2, captured.PrefetchMode);
            Check.Equal(original.PageCombining, captured.PageCombining);
            Check.Equal(original.ApplicationPreLaunch, captured.ApplicationPreLaunch);
            Check.Equal(captured, MemoryManager.CapturePrefetchMode(captured, 0));
            Check.Equal(captured, JsonSerializer.Deserialize<MemoryManagerState>(JsonSerializer.Serialize(captured)));
            Check.Equal<int?>(1, MemoryManager.CapturePrefetchMode(original with { ApplicationLaunchPrefetching = true }, 0).PrefetchMode);
            Check.That(MemoryManager.CapturePrefetchMode(original, null).PrefetchMode is null, "missing original mode was invented");
        });
    }
}
