using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Tests;

internal static class KernelPoolAlertTests
{
    private const long Gb = 1L << 30;
    private const long Mb = 1L << 20;

    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("normal_pools_on_64gb_machine_give_no_warning",
            "A 64 GB PC with 2.6 GB non-paged and 3 GB paged pool, as measured on a normal day, raises no warning.",
            () =>
            {
                var warning = KernelPoolAlert.Evaluate(2_600 * Mb, 3 * Gb, 64 * Gb, [new("NtFs", 0, 66 * Mb)]);
                Check.That(warning is null, "expected no warning");
            });

        yield return ("ntfs_leak_of_28gb_warns_and_names_ntfc",
            "The 2026-10-26 case: 28 GB non-paged pool, almost all in tag NtFC, warns about the non-paged pool and names NtFC.",
            () =>
            {
                PoolTagUsage[] tags = [new("EtwB", 0, 132 * Mb), new("NtFC", 100 * 1024, 27_500 * Mb), new("NVRM", Mb, 65 * Mb)];
                var warning = KernelPoolAlert.Evaluate(28 * Gb, 800 * Mb, 64 * Gb, tags);
                Check.That(warning is not null, "expected a warning");
                Check.Equal(KernelPoolKind.NonPaged, warning!.Pool);
                Check.Equal("NtFC", warning.TopTag);
                Check.Equal(27_500 * Mb, warning.TopTagBytes);
                Check.Equal(28 * Gb, warning.PoolBytes);
            });

        yield return ("threshold_is_a_tenth_of_ram_on_large_machines",
            "On a 64 GB machine the threshold is 6.4 GB, so 6.3 GB stays quiet and 6.6 GB warns.",
            () =>
            {
                Check.Equal(64 * Gb / 10, KernelPoolAlert.ThresholdFor(64 * Gb));
                Check.That(KernelPoolAlert.Evaluate(6_300 * Mb, 0, 64 * Gb, []) is null, "6.3 GB must not warn");
                Check.That(KernelPoolAlert.Evaluate(6_600 * Mb, 0, 64 * Gb, []) is not null, "6.6 GB must warn");
            });

        yield return ("small_machine_uses_4gb_floor",
            "On an 8 GB machine a tenth would be 0.8 GB, but the warning only starts at 4 GB.",
            () =>
            {
                Check.Equal(4 * Gb, KernelPoolAlert.ThresholdFor(8 * Gb));
                Check.That(KernelPoolAlert.Evaluate(3 * Gb, 0, 8 * Gb, []) is null, "3 GB on 8 GB must not warn");
                Check.That(KernelPoolAlert.Evaluate(4 * Gb, 0, 8 * Gb, []) is not null, "4 GB on 8 GB must warn");
            });

        yield return ("paged_pool_leak_is_reported_as_paged_with_its_own_top_tag",
            "A large paged pool is reported as the paged pool, and the top tag is chosen by paged bytes, not non-paged bytes.",
            () =>
            {
                PoolTagUsage[] tags = [new("MmSt", 9 * Gb, 0), new("NtFC", 0, 2 * Gb)];
                var warning = KernelPoolAlert.Evaluate(2 * Gb, 10 * Gb, 64 * Gb, tags);
                Check.Equal(KernelPoolKind.Paged, warning!.Pool);
                Check.Equal("MmSt", warning.TopTag);
            });

        yield return ("both_pools_high_reports_the_larger_one",
            "When both pools pass the threshold, the warning is about the larger pool.",
            () =>
            {
                var warning = KernelPoolAlert.Evaluate(12 * Gb, 8 * Gb, 64 * Gb, []);
                Check.Equal(KernelPoolKind.NonPaged, warning!.Pool);
                Check.Equal(null, warning.TopTag);
            });

        yield return ("tag_with_nothing_in_the_reported_pool_is_never_named",
            "When the non-paged pool is too large but no tag holds non-paged bytes (only paged ones), the warning names no tag rather than one holding 0 bytes.",
            () =>
            {
                PoolTagUsage[] tags = [new("MmSt", 5 * Gb, 0), new("Ntfx", Gb, 0)];
                var warning = KernelPoolAlert.Evaluate(12 * Gb, 0, 64 * Gb, tags);
                Check.Equal(KernelPoolKind.NonPaged, warning!.Pool);
                Check.Equal(null, warning.TopTag);
                Check.Equal(0L, warning.TopTagBytes);
            });

        yield return ("largest_tags_are_ranked_by_total_and_limited",
            "The tag list is sorted by paged plus non-paged bytes, skips empty tags and returns only the requested number.",
            () =>
            {
                PoolTagUsage[] tags = [new("Aaaa", 1, 1), new("Bbbb", 0, 0), new("Cccc", 50, 50), new("Dddd", 70, 0), new("Eeee", 0, 10)];
                var largest = KernelPoolAlert.Largest(tags, 3);
                Check.Equal("Cccc,Dddd,Eeee", string.Join(",", largest.Select(tag => tag.Tag)));
            });
    }
}
