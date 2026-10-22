using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// A memory rule for one program, like a cgroup for all of its processes: an upper limit for their combined private
/// RAM and a memory priority. The History service enforces both.
/// </summary>
public sealed class AppMemoryRule
{
    /// <summary>Full path of the executable; the rule covers every process started from it.</summary>
    public string Path { get; set; } = "";
    /// <summary>Combined private working set the program's processes may keep in RAM; null means no limit.</summary>
    public int? LimitMegabytes { get; set; }
    /// <summary>Windows memory priority 1 (very low) to 5 (normal); null leaves it to Windows.</summary>
    public int? Priority { get; set; }

    public const int MinimumMegabytes = 64;

    [JsonIgnore]
    public bool IsEmpty => LimitMegabytes is null && Priority is null;

    [JsonIgnore]
    public bool IsValid => Path.Length > 0 && LimitMegabytes is null or >= MinimumMegabytes && Priority is null or >= 1 and <= 5;
}

/// <summary>The memory rules in the protected data folder (administrators write, everyone reads).</summary>
public static class MemoryRuleStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => System.IO.Path.Combine(DataFolder.Path, "memory-rules.json");

    public static List<AppMemoryRule> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<AppMemoryRule>>(File.ReadAllText(FilePath), Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return [];
    }

    /// <summary>Replaces (or with an empty rule removes) one program's rule. Needs administrator rights.</summary>
    public static void Save(AppMemoryRule rule)
    {
        List<AppMemoryRule> all = Load();
        all.RemoveAll(existing => string.Equals(existing.Path, rule.Path, StringComparison.OrdinalIgnoreCase));
        if (!rule.IsEmpty) all.Add(rule);
        DataFolder.EnsureSecured();
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList(), Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>The memory manager's switches, as Windows' MMAgent cmdlets show them, plus the SysMain service.</summary>
public sealed record MemoryManagerState(bool MemoryCompression, bool PageCombining, bool ApplicationLaunchPrefetching,
    bool ApplicationPreLaunch, bool SysMainRunning, bool SysMainEnabled, DateTime ReadAtUtc);

/// <summary>
/// Reads and changes Windows' memory manager settings: memory compression, page combining, app launch prefetching,
/// app prelaunch (the WMI class behind Get-MMAgent, Enable-MMAgent and Disable-MMAgent) and the SysMain service.
/// Reading needs administrator rights, so the History service stores the last state in the protected data folder
/// for windows without them.
/// </summary>
public static class MemoryManager
{
    public const string MemoryCompression = "MemoryCompression";
    public const string PageCombining = "PageCombining";
    public const string ApplicationLaunchPrefetching = "ApplicationLaunchPrefetching";
    public const string ApplicationPreLaunch = "ApplicationPreLaunch";
    public const string SysMain = "SysMain";
    public static readonly string[] Features = [MemoryCompression, PageCombining, ApplicationLaunchPrefetching, ApplicationPreLaunch, SysMain];

    private const string ClassPath = @"\\.\root\Microsoft\Windows\PS_MMAgent:PS_MMAgent";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string StatePath => System.IO.Path.Combine(DataFolder.Path, "memory-manager.json");

    /// <summary>The current state from Windows; null without administrator rights.</summary>
    public static MemoryManagerState? ReadLive()
    {
        try
        {
            using var agent = new ManagementClass(ClassPath);
            using ManagementBaseObject result = agent.InvokeMethod("Get", null, null);
            if (result["CmdletOutput"] is not ManagementBaseObject output) return null;
            bool Flag(string name) => output[name] is bool value && value;
            (bool running, bool enabled) = SysMainState();
            return new MemoryManagerState(Flag(MemoryCompression), Flag(PageCombining), Flag(ApplicationLaunchPrefetching),
                Flag(ApplicationPreLaunch), running, enabled, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    /// <summary>The state the History service stored last; null when it never ran.</summary>
    public static MemoryManagerState? ReadCached()
    {
        try
        {
            return File.Exists(StatePath) ? JsonSerializer.Deserialize<MemoryManagerState>(File.ReadAllText(StatePath), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Live when allowed, otherwise the stored state.</summary>
    public static MemoryManagerState? Read() => ReadLive() ?? ReadCached();

    /// <summary>Stores the current state for windows without administrator rights. Needs administrator rights.</summary>
    public static void StoreCurrent()
    {
        if (ReadLive() is not { } state) return;
        DataFolder.EnsureSecured();
        string temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        File.Move(temporary, StatePath, overwrite: true);
    }

    /// <summary>Switches one feature on or off and stores the new state. Needs administrator rights.</summary>
    public static void Set(string feature, bool enable)
    {
        if (feature == SysMain)
        {
            CommandResult result = CommandRunner.Run("sc.exe", "config", "SysMain", "start=", enable ? "auto" : "disabled");
            if (!result.Succeeded) throw new InvalidOperationException(result.FailureSummary());
            using var service = new ServiceController("SysMain");
            if (enable && service.Status == ServiceControllerStatus.Stopped)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
            else if (!enable && service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        else
        {
            if (!Features.Contains(feature)) throw new ArgumentException("Unknown memory manager feature: " + feature, nameof(feature));
            using var agent = new ManagementClass(ClassPath);
            using ManagementBaseObject parameters = agent.GetMethodParameters(enable ? "Enable" : "Disable");
            parameters[feature] = true;
            using ManagementBaseObject result = agent.InvokeMethod(enable ? "Enable" : "Disable", parameters, null);
        }
        StoreCurrent();
    }

    private static (bool Running, bool Enabled) SysMainState()
    {
        try
        {
            using var service = new ServiceController("SysMain");
            return (service.Status == ServiceControllerStatus.Running, service.StartType != ServiceStartMode.Disabled);
        }
        catch (InvalidOperationException)
        {
            return (false, false);
        }
    }
}

/// <summary>
/// Enforces the memory rules in the History service, on every snapshot (every 2 seconds): sets each new process's
/// memory priority, and when a program's processes together hold more private RAM than its limit, trims their
/// working sets, largest first, until they are below it. Trimmed pages leave the program's RAM; it reads back what
/// it needs, like a cgroup at its memory.high limit. Also stores the memory manager state every 10 minutes.
/// </summary>
public sealed class MemoryRuleEnforcer
{
    private const int ProcessSetQuota = 0x0100, ProcessSetInformation = 0x0200, ProcessQueryLimited = 0x1000;
    private const int ProcessMemoryPriority = 0;
    private const int NormalPriority = 5;
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StateInterval = TimeSpan.FromMinutes(10);

    private readonly Action<string> log;
    private readonly Func<IReadOnlyList<AppMemoryRule>>? source;
    private readonly Dictionary<(int Pid, long CreateTime), int> priorities = new();
    private readonly HashSet<string> reportedFailures = new(StringComparer.OrdinalIgnoreCase);
    private List<AppMemoryRule> rules = [];
    private DateTime rulesWritten;
    private DateTime summaryDue = DateTime.UtcNow + SummaryInterval;
    private DateTime stateDue = DateTime.UtcNow;
    private readonly Dictionary<string, (int Trims, long Bytes)> trimmed = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="source">Rules to use instead of the file, read on every snapshot (tests); the memory manager state is then not stored.</param>
    public MemoryRuleEnforcer(Action<string> log, Func<IReadOnlyList<AppMemoryRule>>? source = null)
    {
        this.log = log;
        this.source = source;
    }

    public void Apply(MonitorSnapshot snapshot)
    {
        try
        {
            if (source is null && DateTime.UtcNow >= stateDue)
            {
                stateDue = DateTime.UtcNow + StateInterval;
                MemoryManager.StoreCurrent();
            }
            ReloadRules();
            ApplyPriorities(snapshot);
            ApplyLimits(snapshot);
            WriteSummary();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log("Memory rules: " + ex.Message);
        }
    }

    private void ReloadRules()
    {
        if (source is not null)
        {
            rules = source().Where(rule => rule.IsValid).ToList();
            return;
        }
        DateTime written = File.Exists(MemoryRuleStore.FilePath) ? File.GetLastWriteTimeUtc(MemoryRuleStore.FilePath) : DateTime.MinValue;
        if (written == rulesWritten) return;
        rulesWritten = written;
        rules = MemoryRuleStore.Load().Where(rule => rule.IsValid).ToList();
        log(rules.Count == 0 ? "Memory rules: none" : "Memory rules: " + string.Join("; ", rules.Select(rule =>
            $"{System.IO.Path.GetFileName(rule.Path)} limit {(rule.LimitMegabytes is { } mb ? mb + " MB" : "none")}, priority {(rule.Priority?.ToString() ?? "default")}")));
    }

    private AppMemoryRule? RuleFor(string path) =>
        path.Length == 0 ? null : rules.FirstOrDefault(rule => string.Equals(rule.Path, path, StringComparison.OrdinalIgnoreCase));

    private void ApplyPriorities(MonitorSnapshot snapshot)
    {
        var alive = new HashSet<(int, long)>();
        foreach (ProcessSample process in snapshot.Processes)
        {
            if (process.Pid <= 4) continue;
            alive.Add(process.Key);
            int? wanted = RuleFor(process.Path)?.Priority;
            bool known = priorities.TryGetValue(process.Key, out int current);
            if (wanted is { } priority)
            {
                if (known && current == priority) continue;
                if (SetPriority(process, priority)) priorities[process.Key] = priority;
            }
            else if (known)
            {
                // The rule was removed or no longer sets a priority: give the process Windows' normal priority back.
                SetPriority(process, NormalPriority);
                priorities.Remove(process.Key);
            }
        }
        foreach (var key in priorities.Keys.Where(key => !alive.Contains(key)).ToList()) priorities.Remove(key);
    }

    private void ApplyLimits(MonitorSnapshot snapshot)
    {
        foreach (AppMemoryRule rule in rules)
        {
            if (rule.LimitMegabytes is not { } limitMegabytes) continue;
            long limit = limitMegabytes * 1048576L;
            List<ProcessSample> members = snapshot.Processes
                .Where(process => process.Pid > 4 && string.Equals(process.Path, rule.Path, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(process => process.PrivateWorkingSet)
                .ToList();
            long total = members.Sum(process => process.PrivateWorkingSet);
            if (total <= limit) continue;
            foreach (ProcessSample process in members)
            {
                if (total <= limit) break;
                if (!Trim(process)) continue;
                total -= process.PrivateWorkingSet;
                trimmed.TryGetValue(rule.Path, out var sum);
                trimmed[rule.Path] = (sum.Trims + 1, sum.Bytes + process.PrivateWorkingSet);
            }
        }
    }

    private void WriteSummary()
    {
        if (DateTime.UtcNow < summaryDue) return;
        summaryDue = DateTime.UtcNow + SummaryInterval;
        foreach (var (path, sum) in trimmed)
        {
            log($"Memory rules: trimmed {System.IO.Path.GetFileName(path)} {sum.Trims} times in the last 10 minutes, {sum.Bytes / 1048576} MB in total");
        }
        trimmed.Clear();
    }

    private bool SetPriority(ProcessSample process, int priority)
    {
        IntPtr handle = OpenProcess(ProcessSetInformation | ProcessQueryLimited, false, process.Pid);
        if (handle == IntPtr.Zero) return Failed(process, "priority", Marshal.GetLastWin32Error());
        try
        {
            int value = priority;
            return SetProcessInformation(handle, ProcessMemoryPriority, ref value, sizeof(int)) || Failed(process, "priority", Marshal.GetLastWin32Error());
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    private bool Trim(ProcessSample process)
    {
        IntPtr handle = OpenProcess(ProcessSetQuota | ProcessQueryLimited, false, process.Pid);
        if (handle == IntPtr.Zero) return Failed(process, "limit", Marshal.GetLastWin32Error());
        try
        {
            return EmptyWorkingSet(handle) || Failed(process, "limit", Marshal.GetLastWin32Error());
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>Logs once per program and kind (protected processes refuse), so a refusal cannot fill the log.</summary>
    private bool Failed(ProcessSample process, string what, int error)
    {
        if (error != 87 && reportedFailures.Add(what + "|" + process.Path))
        {
            log($"Memory rules: cannot apply the {what} to {process.ImageName} (Windows error {error})");
        }
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref int information, int size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
