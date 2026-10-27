using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Tests;

/// <summary>Guards around elevated actions: where results may be written, what may replace Task Manager, what a rule means, which process a memory rule touches.</summary>
internal static class SafetyTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("helper_result_file_in_temp_is_written_once",
            "An elevated helper may create a new nax….txt file in the user's temporary folder, but a second write to the same path is refused.",
            () => StoreTests.InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "nax-helper-test.txt");
                Check.That(HelperResultFile.TryWrite(file, "first"), "the first write must succeed");
                Check.That(!HelperResultFile.TryWrite(file, "second"), "an existing file must never be overwritten");
                Check.Equal("first", File.ReadAllText(file));
            }));

        yield return ("helper_result_file_outside_temp_or_with_another_name_is_refused",
            "A result path in the Windows folder, or with a name that does not start with nax, is never written.",
            () =>
            {
                Check.That(!HelperResultFile.IsAcceptable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "nax-result.txt")), "the Windows folder must be refused");
                Check.That(!HelperResultFile.IsAcceptable(Path.Combine(Path.GetTempPath(), "result.txt")), "a name without nax must be refused");
                Check.That(!HelperResultFile.IsAcceptable(Path.Combine(Path.GetTempPath(), "nax-result.dll")), "a name not ending in .txt must be refused");
                Check.That(!HelperResultFile.IsAcceptable("nax-result.txt"), "a relative path must be refused");
            });

        yield return ("helper_result_file_through_a_junction_is_refused",
            "A result path that leads through a junction inside the temporary folder is refused, so the write cannot land in another folder.",
            () => StoreTests.InTempFolder(folder =>
            {
                string target = Path.Combine(folder, "target");
                string junction = Path.Combine(folder, "link");
                Directory.CreateDirectory(target);
                using (Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                {
                    mklink.WaitForExit();
                }
                Check.That(Directory.Exists(junction), "the test junction could not be created");
                Check.That(!HelperResultFile.IsAcceptable(Path.Combine(junction, "nax-result.txt")), "a path through a junction must be refused");
            }));

        yield return ("system_program_is_admin_only_and_user_temp_program_is_not",
            "A program in the Windows system folder counts as changeable only by administrators, a program in the user's temporary folder does not, and a missing file never does.",
            () => StoreTests.InTempFolder(folder =>
            {
                string system = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                Check.That(ProtectedLocation.IsAdminOnly(system), "System32 must be admin-only");
                string copy = Path.Combine(folder, "app.exe");
                File.WriteAllText(copy, "x");
                Check.That(!ProtectedLocation.IsAdminOnly(copy), "a file in the user's temp folder must not be admin-only");
                Check.That(!ProtectedLocation.IsAdminOnly(Path.Combine(folder, "missing.exe")), "a missing file must not be admin-only");
                Check.That(!TaskManagerReplacement.CanReplaceWith(copy), "a user-writable copy must not replace Task Manager");
            }));

        yield return ("shortened_ipv4_rule_targets_are_rejected",
            "Rules with shortened IPv4 targets such as 10.1/16, 192.168.1 or a bare number are rejected instead of being read as a different network.",
            () =>
            {
                foreach (string line in new[] { "- 10.1/16", "- 192.168.1", "+ 3389", "- 10.1 TCP/80", "+ 0x7f.1" })
                {
                    Check.That(!NetworkRule.TryParse(line, out _, out RuleParseError error, out _) && error == RuleParseError.Target, $"\"{line}\" must be rejected as a target");
                }
            });

        yield return ("full_ipv4_and_ipv6_rule_targets_still_parse",
            "Complete IPv4 addresses and ranges and IPv6 addresses keep working after the stricter check.",
            () =>
            {
                foreach (string line in new[] { "- 10.0.0.0/8", "+ 192.168.1.5 TCP/443", "- 1.1.1.1", "+ ::1", "- 2001:db8::/32", "+ ::ffff:10.0.0.1" })
                {
                    Check.That(NetworkRule.TryParse(line, out _, out RuleParseError error, out _), $"\"{line}\" must parse, got {error}");
                }
            });

        yield return ("memory_rule_skips_a_process_whose_creation_time_differs",
            "A memory rule whose snapshot shows this test process with a different creation time (a reused PID) leaves the process's memory priority alone, and with the right time it applies.",
            () =>
            {
                using Process self = Process.GetCurrentProcess();
                Check.That(GetProcessTimes(self.Handle, out long created, out _, out _, out _), "cannot read the creation time");
                string path = Environment.ProcessPath!;
                var rules = new List<AppMemoryRule> { new() { Path = path, Priority = 2 } };
                var enforcer = new MemoryRuleEnforcer(_ => { }, () => rules);
                using var engine = new MonitorEngine("NaxCoreTests") { ReadProcessDetails = false, ReadGpu = false, ReadDisks = false };
                MonitorSnapshot real = engine.Collect();
                MonitorSnapshot With(long createTime) => new()
                {
                    Timestamp = real.Timestamp,
                    Processes = [new ProcessSample { Pid = self.Id, ParentPid = 0, ImageName = Path.GetFileName(path), CreateTime = createTime, Path = path, Description = "", Company = "" }],
                    Connections = [],
                    NetworkIssues = [],
                    System = real.System
                };
                try
                {
                    enforcer.Apply(With(created + 10_000_000));
                    Check.Equal(5, MemoryPriority(self.Handle));
                    enforcer.Apply(With(created));
                    Check.Equal(2, MemoryPriority(self.Handle));
                }
                finally
                {
                    rules.Clear();
                    enforcer.Apply(With(created));
                }
                Check.Equal(5, MemoryPriority(self.Handle));
            });
    }

    private static int MemoryPriority(IntPtr process)
    {
        int value = 0;
        return GetProcessInformation(process, 0, ref value, sizeof(int)) ? value : -1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(IntPtr process, int informationClass, ref int information, int size);
}
