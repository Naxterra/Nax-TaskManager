#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// Memory rules: validation, the stored form, and enforcement on a child process of this test (never on other apps).
// Runs without administrator rights: trimming and the memory priority work on the user's own processes.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;

if (args.Length == 2 && args[0] == "--hog")
{
    // The child: touch every page of a buffer so it is really in RAM, then wait to be measured.
    byte[] buffer = new byte[int.Parse(args[1]) * 1048576];
    for (int i = 0; i < buffer.Length; i += 4096) buffer[i] = 1;
    Console.WriteLine("ready");
    Thread.Sleep(60000);
    GC.KeepAlive(buffer);
    return 0;
}

int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}

string self = Environment.ProcessPath!;
Check("empty rule is empty", new AppMemoryRule { Path = self }.IsEmpty);
Check("limit below the minimum is invalid", !new AppMemoryRule { Path = self, LimitMegabytes = 32 }.IsValid);
Check("priority 6 is invalid", !new AppMemoryRule { Path = self, Priority = 6 }.IsValid);
Check("rule without a path is invalid", !new AppMemoryRule { LimitMegabytes = 512 }.IsValid);
Check("limit and priority are valid", new AppMemoryRule { Path = self, LimitMegabytes = 512, Priority = 2 }.IsValid);
var roundTrip = JsonSerializer.Deserialize<AppMemoryRule>(JsonSerializer.Serialize(new AppMemoryRule { Path = self, LimitMegabytes = 1024, Priority = 3 }))!;
Check("rule survives JSON", roundTrip.Path == self && roundTrip.LimitMegabytes == 1024 && roundTrip.Priority == 3);
var state = new MemoryManagerState(true, false, true, true, false, true, new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));
Check("memory manager state survives JSON", JsonSerializer.Deserialize<MemoryManagerState>(JsonSerializer.Serialize(state)) == state);

var start = new ProcessStartInfo(self) { RedirectStandardOutput = true, UseShellExecute = false };
start.ArgumentList.Add("--hog");
start.ArgumentList.Add("400");
using Process child = Process.Start(start)!;
try
{
    Check("child allocated its buffer", child.StandardOutput.ReadLine() == "ready");
    using var engine = new MonitorEngine("NaxMemoryRulesTest") { ReadProcessDetails = false, ReadGpu = false, ReadDisks = false };
    MonitorSnapshot before = engine.Collect();
    ProcessSample? sample = before.Processes.FirstOrDefault(p => p.Pid == child.Id);
    Check("child is in the snapshot with its path", sample is not null && string.Equals(sample.Path, self, StringComparison.OrdinalIgnoreCase));
    Check("child holds the buffer in RAM", sample?.PrivateWorkingSet > 350L * 1048576);

    var rules = new List<AppMemoryRule> { new() { Path = self, LimitMegabytes = 128, Priority = 2 } };
    var log = new List<string>();
    var enforcer = new MemoryRuleEnforcer(log.Add, () => rules);
    enforcer.Apply(before);
    child.Refresh();
    Check($"child was trimmed below the limit (working set {child.WorkingSet64 / 1048576} MB)", child.WorkingSet64 < 128L * 1048576);
    Check("child has memory priority 2", MemoryPriority(child) == 2);

    rules.Clear();
    enforcer.Apply(engine.Collect());
    Check("removing the rule restores memory priority 5", MemoryPriority(child) == 5);
    Check("nothing was logged as refused", !log.Any(line => line.Contains("cannot apply", StringComparison.Ordinal)));
}
finally
{
    child.Kill();
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

static int MemoryPriority(Process process)
{
    int value = 0;
    return GetProcessInformation(process.Handle, 0, ref value, sizeof(int)) ? value : -1;
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool GetProcessInformation(IntPtr process, int informationClass, ref int information, int size);
