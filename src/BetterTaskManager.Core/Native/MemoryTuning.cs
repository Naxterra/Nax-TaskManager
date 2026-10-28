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

    public static List<AppMemoryRule> Load(string? filePath = null)
    {
        string path = filePath ?? FilePath;
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<List<AppMemoryRule>>(File.ReadAllText(path), Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return [];
    }

    /// <summary>Replaces (or with an empty rule removes) one program's rule. Needs administrator rights.</summary>
    public static void Save(AppMemoryRule rule)
    {
        DataFolder.EnsureSecured();
        Save(rule, FilePath);
    }

    /// <summary>
    /// Replaces one program's rule in <paramref name="filePath"/>. A file that cannot be read is kept as
    /// "memory-rules.json.damaged-…" instead of being overwritten silently, and the rules start over from this one.
    /// Nothing outside the file depends on it: the service restores normal priorities when rules disappear.
    /// </summary>
    public static void Save(AppMemoryRule rule, string filePath)
    {
        List<AppMemoryRule> all;
        try
        {
            all = File.Exists(filePath) ? JsonSerializer.Deserialize<List<AppMemoryRule>>(File.ReadAllText(filePath), Options) ?? throw new JsonException("No rule list.") : [];
        }
        catch (JsonException)
        {
            File.Move(filePath, filePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), overwrite: true);
            all = [];
        }
        all.RemoveAll(existing => string.Equals(existing.Path, rule.Path, StringComparison.OrdinalIgnoreCase));
        if (!rule.IsEmpty) all.Add(rule);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(all.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList(), Options));
    }
}

/// <summary>The memory manager's switches, as Windows' MMAgent cmdlets show them, plus the SysMain service.</summary>
public sealed record MemoryManagerState(bool MemoryCompression, bool PageCombining, bool ApplicationLaunchPrefetching,
    bool ApplicationPreLaunch, bool SysMainRunning, bool SysMainEnabled, DateTime ReadAtUtc)
{
    /// <summary>SysMain's start type as sc.exe names it (auto, demand or disabled). Records of older versions have none.</summary>
    public string? SysMainStartMode { get; init; }
}

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
                Flag(ApplicationPreLaunch), running, enabled, DateTime.UtcNow) { SysMainStartMode = SysMainStartName() };
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
        AtomicFile.WriteAllText(StatePath, JsonSerializer.Serialize(state, Options));
    }

    /// <summary>The state before this app first changed a switch, so an uninstall can put Windows back as it was.</summary>
    public static string OriginalPath => System.IO.Path.Combine(DataFolder.Path, "memory-manager-original.json");

    /// <summary>Switches one feature on or off and stores the new state. Needs administrator rights.</summary>
    public static void Set(string feature, bool enable)
    {
        if (!File.Exists(OriginalPath) && ReadLive() is { } original)
        {
            DataFolder.EnsureSecured();
            AtomicFile.WriteAllText(OriginalPath, JsonSerializer.Serialize(original, Options));
        }
        Apply(feature, enable);
        StoreCurrent();
    }

    /// <summary>
    /// Puts every switch this app changed back to the state recorded before the first change, then forgets that
    /// state. Does nothing when the app never changed a switch. Returns a note for the user when the record could not
    /// be read: nothing can be restored from it, now or later, so it is deleted and the uninstall goes on. A restore
    /// that fails throws and keeps the record. Needs administrator rights.
    /// </summary>
    public static string? RestoreOriginal()
    {
        if (!File.Exists(OriginalPath)) return null;
        MemoryManagerState original;
        try
        {
            original = JsonSerializer.Deserialize<MemoryManagerState>(File.ReadAllText(OriginalPath), Options)
                ?? throw new JsonException("The file holds no state.");
        }
        catch (JsonException ex)
        {
            File.Delete(OriginalPath);
            return "The recorded memory manager settings could not be read, so they were not restored: " + ex.Message;
        }
        // The record is kept until the restore worked, so a failed attempt can be repeated. The memory manager switches
        // may not be readable or changeable while SysMain is stopped, so it runs while they are restored, and its own
        // recorded start type and running state come last.
        bool hasSysMain = StartSysMainForRestore();
        MemoryManagerState live = ReadLive() ?? throw new InvalidOperationException("The current memory manager settings could not be read, so nothing was restored.");
        if (live.MemoryCompression != original.MemoryCompression) Apply(MemoryCompression, original.MemoryCompression);
        if (live.PageCombining != original.PageCombining) Apply(PageCombining, original.PageCombining);
        if (live.ApplicationLaunchPrefetching != original.ApplicationLaunchPrefetching) Apply(ApplicationLaunchPrefetching, original.ApplicationLaunchPrefetching);
        if (live.ApplicationPreLaunch != original.ApplicationPreLaunch) Apply(ApplicationPreLaunch, original.ApplicationPreLaunch);
        if (hasSysMain) RestoreSysMain(original);
        File.Delete(OriginalPath);
        return null;
    }

    /// <summary>
    /// Makes sure SysMain runs (allowing a manual start if it is disabled) so the memory manager switches can be read and
    /// changed. Returns false when this Windows has no SysMain service.
    /// </summary>
    private static bool StartSysMainForRestore()
    {
        using var service = new ServiceController("SysMain");
        ServiceStartMode mode;
        try
        {
            mode = service.StartType;
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 })
        {
            // ERROR_SERVICE_DOES_NOT_EXIST: some Windows editions have no SysMain service, so there is nothing to put
            // back. Any other failure is thrown, so the record is kept.
            return false;
        }
        if (mode == ServiceStartMode.Disabled)
        {
            CommandResult result = CommandRunner.Run("sc.exe", "config", "SysMain", "start=", "demand");
            if (!result.Succeeded) throw new InvalidOperationException(result.FailureSummary());
        }
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status == ServiceControllerStatus.Stopped) service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
        return true;
    }

    /// <summary>Puts SysMain back to its recorded start type (automatic, manual or disabled) and running state.</summary>
    private static void RestoreSysMain(MemoryManagerState original)
    {
        // Records from before the start type was stored only know enabled or disabled.
        string start = original.SysMainStartMode ?? (original.SysMainEnabled ? "auto" : "disabled");
        using var service = new ServiceController("SysMain");
        string current = ScStartName(service.StartType);
        if (current != start)
        {
            CommandResult result = CommandRunner.Run("sc.exe", "config", "SysMain", "start=", start);
            if (!result.Succeeded) throw new InvalidOperationException(result.FailureSummary());
        }
        service.Refresh();
        if (original.SysMainRunning && start != "disabled" && service.Status == ServiceControllerStatus.Stopped)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
        else if (!original.SysMainRunning && service.Status == ServiceControllerStatus.Running)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    }

    private static string ScStartName(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Disabled => "disabled",
        ServiceStartMode.Manual => "demand",
        _ => "auto"
    };

    private static void Apply(string feature, bool enable)
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
    }

    private static string? SysMainStartName()
    {
        try
        {
            using var service = new ServiceController("SysMain");
            return ScStartName(service.StartType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
            if (!IsSameProcess(handle, process)) return false;
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
            if (!IsSameProcess(handle, process)) return false;
            return EmptyWorkingSet(handle) || Failed(process, "limit", Marshal.GetLastWin32Error());
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>
    /// The snapshot can be up to two seconds old, and Windows reuses process IDs: only the process with the snapshot's
    /// creation time is changed, never a newer one that got the same ID.
    /// </summary>
    private static bool IsSameProcess(IntPtr handle, ProcessSample process) =>
        GetProcessTimes(handle, out long created, out _, out _, out _) && created == process.CreateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

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
