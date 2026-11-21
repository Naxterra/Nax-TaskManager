using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Core.Tests;

internal static class GpuMemoryTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("gpu_memory_uses_adapter_totals_not_sum_of_process_attribution", "A process can use multiple adapters; process attributions can overlap, so system totals come from independent adapter counters.", () =>
        {
            var sample = new GpuMemorySample(new Dictionary<string, long>
            {
                ["pid_42_luid_0x0_0x1_phys_0"] = 4L << 30, ["pid_42_luid_0x0_0x2_phys_0"] = 6L << 30,
                ["pid_99_luid_0x0_0x1_phys_0"] = 5L << 30, ["invalid"] = 900
            }, new Dictionary<string, long> { ["pid_42_luid_0x0_0x1_phys_0"] = 2L << 30 },
                new Dictionary<string, long> { ["adapter1"] = 7L << 30, ["adapter2"] = 1L << 30 },
                new Dictionary<string, long> { ["adapter1"] = 3L << 30 });
            Check.Equal<long?>(10L << 30, sample.ForProcess(42).Shared);
            Check.Equal<long?>(2L << 30, sample.ForProcess(42).Dedicated);
            Check.Equal<long?>(8L << 30, sample.SharedTotal);
            Check.Equal<long?>(3L << 30, sample.DedicatedTotal);
        });
        yield return ("gpu_memory_missing_counter_is_not_zero", "A failed/missing counter stays unknown independently of other counters; a healthy capture with no allocation reports zero.", () =>
        {
            var sample = new GpuMemorySample(null, new Dictionary<string, long>(), null, new Dictionary<string, long>());
            Check.That(sample.ForProcess(42).Shared is null, "missing shared counter became zero");
            Check.Equal<long?>(0, sample.ForProcess(42).Dedicated);
            Check.That(sample.SharedTotal is null, "missing adapter capture became zero");
            Check.Equal<long?>(0, sample.DedicatedTotal);
            Check.That(GpuMemorySample.Unavailable.ForProcess(42).Dedicated is null, "disabled collection became zero");
            var invalid = new GpuMemorySample(new Dictionary<string, long> { ["pid_42_luid_0x0_0x1_phys_0"] = -1 }, null, null, null);
            Check.That(invalid.ForProcess(42).Shared is null, "invalid counter became a measured value");
        });
        yield return ("process_memory_groups_keep_ram_commit_and_gpu_separate", "Large GPU and committed allocations remain visible when CPU working sets are small; unknown GPU data propagates through groups.", () =>
        {
            ProcessSample Process(int pid, long resident, long commit, long? shared) => new()
            {
                Pid = pid, ParentPid = 0, ImageName = "Test.exe", CreateTime = 1, Path = @"C:\Apps\Test.exe", Description = "Test", Company = "Test",
                PrivateWorkingSet = resident, CommitCharge = commit, GpuSharedMemory = shared, GpuDedicatedMemory = 1L << 30
            };
            var first = Process(42, 70L << 20, 32L << 30, 20L << 30);
            var second = Process(99, 30L << 20, 1L << 30, 2L << 30);
            var usage = ProcessMemoryUsage.Sum([first, second]);
            Check.Equal(100L << 20, usage.Resident);
            Check.Equal(33L << 30, usage.Committed);
            Check.Equal<long?>(22L << 30, usage.GpuShared);
            Check.Equal<long?>(2L << 30, usage.GpuDedicated);
            Check.That(ProcessMemoryUsage.Sum([first, Process(100, 0, 0, null)]).GpuShared is null, "partial GPU data looked complete");
        });
        yield return ("new_process_memory_columns_preserve_saved_layout", "New memory measures join an older saved process layout beside private RAM and remain movable and reloadable.", () =>
        {
            string[] defaults = ["Cpu", "Memory", "Commit", "GpuMemory", "Io", "Bandwidth", "Network", "Gpu", "Publisher"];
            var order = new ColumnOrderState(defaults, "Memory,Cpu,Io,Bandwidth,Network,Gpu,Publisher");
            Check.Equal(3, order.PositionOf("Commit"));
            Check.Equal(4, order.PositionOf("GpuMemory"));
            Check.Equal(1, order.PositionOf("Memory"));
            order.Move("GpuMemory", 1);
            var loaded = new ColumnOrderState(defaults, string.Join(",", order.Columns));
            Check.Equal(1, loaded.PositionOf("GpuMemory"));
            Check.Equal(9, loaded.Columns.Distinct().Count());
        });
    }
}
