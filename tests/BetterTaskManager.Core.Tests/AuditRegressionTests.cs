using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Core.Tests;

/// <summary>Regression tests for safety, native parsing, shared file reads and settings persistence.</summary>
internal static class AuditRegressionTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("helper_result_file_rejects_alternate_data_streams",
            "A helper result path with an NTFS alternate data stream colon after the drive prefix is rejected even when it ends with .txt.",
            () =>
            {
                string ads = Path.Combine(Path.GetTempPath(), "nax-result.txt:hidden.txt");
                Check.That(!HelperResultFile.IsAcceptable(ads), "alternate data stream path must be refused");
            });

        yield return ("helper_result_file_rejects_junction_temp_root_itself",
            "A helper result path placed directly inside a temporary folder that is itself an NTFS junction is refused.",
            () => StoreTests.InTempFolder(folder =>
            {
                string target = Path.Combine(folder, "target");
                string junction = Path.Combine(folder, "temp-link");
                Directory.CreateDirectory(target);
                using (Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                {
                    mklink.WaitForExit();
                }
                Check.That(Directory.Exists(junction), "the test junction could not be created");
                Check.That(!HelperResultFile.IsAcceptable(Path.Combine(junction, "nax-result.txt"), junction),
                    "a temp root that is itself a junction must be refused");
            }));

        yield return ("disk_file_activity_maps_etw_io_priority_enum_values_without_shift",
            "ETW IOPriority values map Normal (3) to Normal, High (4) to High, Verylow (1) to VeryLow and Notset (0) to empty.",
            () =>
            {
                Check.Equal("", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.Notset));
                Check.Equal("VeryLow", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.Verylow));
                Check.Equal("Low", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.Low));
                Check.Equal("Normal", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.Normal));
                Check.Equal("High", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.High));
                Check.Equal("Critical", BetterTaskManager.Core.Disk.DiskFileActivity.FormatPriority(Microsoft.Diagnostics.Tracing.Parsers.Kernel.IOPriority.Critical));
            });

        yield return ("task_manager_replacement_unquote_extracts_executable_with_arguments",
            "Quoted and unquoted IFEO Debugger values with trailing command-line arguments resolve to the executable path alone.",
            () =>
            {
                Check.Equal(@"C:\Program Files\Nax-TaskManager\Nax-TaskManager.exe",
                    TaskManagerReplacement.Unquote("\"C:\\Program Files\\Nax-TaskManager\\Nax-TaskManager.exe\" --page Processes"));
                Check.Equal(@"C:\Tools\TaskMgr\app.exe",
                    TaskManagerReplacement.Unquote(@"C:\Tools\TaskMgr\app.exe /elevated"));
                Check.Equal(@"C:\Program Files\My App\app.exe",
                    TaskManagerReplacement.Unquote(@"C:\Program Files\My App\app.exe --arg"));
                Check.Equal("", TaskManagerReplacement.Unquote("   "));
            });

        yield return ("standby_limiter_rejects_overflowing_and_too_small_megabytes",
            "Standby limit parsing rejects values below the minimum or above the 16 TB overflow cap and converts valid megabytes to bytes.",
            () =>
            {
                Check.Equal<long?>(null, StandbyLimiter.ParseLimitBytes("128"));
                Check.Equal<long?>(null, StandbyLimiter.ParseLimitBytes("9000000000000"));
                Check.Equal<long?>(null, StandbyLimiter.ParseLimitBytes("-512"));
                Check.Equal<long?>(2048L * 1048576L, StandbyLimiter.ParseLimitBytes(" 2048 \r\n"));
                Check.Equal<long?>(StandbyLimiter.MaximumMegabytes * 1048576L,
                    StandbyLimiter.ParseLimitBytes(StandbyLimiter.MaximumMegabytes.ToString()));
            });

        yield return ("gpu_counters_separate_multi_gpu_luids",
            "Two GPUs whose PDH engine names both say phys_0 but carry different WDDM LUIDs are numbered separately as GPU 0 and GPU 1.",
            () =>
            {
                var luids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int Resolve(string luid)
                {
                    if (!luids.TryGetValue(luid, out int idx)) luids[luid] = idx = luids.Count;
                    return idx;
                }
                Check.That(GpuCounters.TryParse("pid_100_luid_0x0_0xA100_phys_0_eng_0_engtype_3D", out int pid1, out string key1, out string eng1, Resolve), "first GPU must parse");
                Check.That(GpuCounters.TryParse("pid_200_luid_0x0_0xB200_phys_0_eng_0_engtype_3D", out int pid2, out string key2, out string eng2, Resolve), "second GPU must parse");
                Check.Equal(100, pid1);
                Check.Equal(200, pid2);
                Check.Equal("GPU 0 - 3D", eng1);
                Check.Equal("GPU 1 - 3D", eng2);
                Check.That(key1 != key2, "distinct LUIDs must produce distinct engine keys");
            });

        yield return ("atomic_file_read_shared_reads_under_concurrent_writer_handle",
            "AtomicFile.ReadAllTextShared reads a file even when another handle keeps it open for shared read/write/delete access.",
            () => StoreTests.InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "shared.txt");
                using var writer = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes("shared-content");
                writer.Write(bytes, 0, bytes.Length);
                writer.Flush();
                Check.Equal("shared-content", AtomicFile.ReadAllTextShared(file));
            }));

        yield return ("process_control_rejects_mismatched_create_time_without_affecting_process",
            "ProcessControl.TryRead and ProcessControl.Terminate refuse to touch a process when the supplied creation time differs from the running process.",
            () =>
            {
                using Process self = Process.GetCurrentProcess();
                Check.That(GetProcessTimes(self.Handle, out long created, out _, out _, out _), "cannot read creation time");
                Check.That(!ProcessControl.TryRead(self.Id, created + 10_000_000, out _), "TryRead must fail on mismatched creation time");
                Check.That(ProcessControl.TryRead(self.Id, created, out _), "TryRead must succeed on matching creation time");
                ControlResult term = ProcessControl.Terminate(self.Id, created + 10_000_000);
                Check.That(!term.Succeeded && term.Gone, "Terminate with wrong creation time must report Gone without ending the process");
            });

        yield return ("native_process_info_marks_idle_and_system_as_critical",
            "PID 0 (System Idle) and PID 4 (System) are both marked critical while this test process is not.",
            () =>
            {
                Check.That(NativeProcessInfo.IsCritical(0), "PID 0 must be critical");
                Check.That(NativeProcessInfo.IsCritical(4), "PID 4 must be critical");
                Check.That(!NativeProcessInfo.IsCritical(Environment.ProcessId), "the test process must not be critical");
            });

        yield return ("virtual_machine_owner_recognizes_vmmem_variants",
            "VirtualMachineOwners.IsMemoryProcess recognizes vmmem, vmmemWSL and other vmmem* process names, and ignores unrelated executables.",
            () =>
            {
                Check.That(VirtualMachineOwners.IsMemoryProcess("vmmem"), "bare vmmem must match");
                Check.That(VirtualMachineOwners.IsMemoryProcess("vmmemWSL"), "vmmemWSL must match");
                Check.That(VirtualMachineOwners.IsMemoryProcess("vmmemCmZygote"), "vmmemCmZygote must match");
                Check.That(VirtualMachineOwners.IsMemoryProcess("Vmmem.exe"), "Vmmem.exe must match");
                Check.That(!VirtualMachineOwners.IsMemoryProcess("notepad.exe"), "unrelated process must not match");
                Check.That(!VirtualMachineOwners.IsMemoryProcess("myvmmem.exe"), "prefix-only match required");
            });

        yield return ("app_settings_round_trips_disk_and_startup_sort_preferences",
            "Disk process sort, disk file sort and startup sort preferences survive JSON serialization and null repair.",
            () =>
            {
                var settings = new AppSettings
                {
                    DiskSortColumn = "Write",
                    DiskSortDescending = false,
                    DiskFilesSortColumn = "Response",
                    DiskFilesSortDescending = false,
                    StartupSortColumn = "Status",
                    StartupSortDescending = true
                };
                string json = System.Text.Json.JsonSerializer.Serialize(settings);
                AppSettings reloaded = AppSettings.FromJson(json);
                Check.Equal("Write", reloaded.DiskSortColumn);
                Check.Equal(false, reloaded.DiskSortDescending);
                Check.Equal("Response", reloaded.DiskFilesSortColumn);
                Check.Equal(false, reloaded.DiskFilesSortDescending);
                Check.Equal("Status", reloaded.StartupSortColumn);
                Check.Equal(true, reloaded.StartupSortDescending);

                AppSettings repaired = AppSettings.FromJson("""{"DiskSortColumn":null,"DiskFilesSortColumn":null,"StartupSortColumn":null}""");
                Check.Equal("Total", repaired.DiskSortColumn);
                Check.Equal("Total", repaired.DiskFilesSortColumn);
                Check.Equal("Name", repaired.StartupSortColumn);
            });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
}
