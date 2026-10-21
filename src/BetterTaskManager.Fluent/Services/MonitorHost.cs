using System.Diagnostics;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Monitoring;
using Microsoft.UI.Dispatching;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Owns the background <see cref="MonitorEngine"/> and hands snapshots to the UI thread. Snapshots are
/// coalesced: if the UI is still busy with the previous one, only the newest is delivered.
/// </summary>
public sealed class MonitorHost : IDisposable
{
    public const int HistoryLength = 60;

    private readonly MonitorEngine engine = new();
    private readonly AppSettings settings;
    private DispatcherQueue? dispatcher;
    private MonitorSnapshot? pending;
    private int deliveryQueued;
    private string searchText = "";
    private bool viewSuspended;

    public MonitorHost(AppSettings settings)
    {
        this.settings = settings;
        engine.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.RefreshIntervalMilliseconds, 250, 10000));
        engine.SnapshotReady += OnSnapshotReady;
        engine.CollectionFailed += ex => dispatcher?.TryEnqueue(DispatcherQueuePriority.Normal, () => CollectionFailed?.Invoke(ex.Message));
        IsElevated = FirewallRules.IsElevated;
        if (!IsElevated) engine.UseServiceFeed();
        // Elevated with the history service running: one set of kernel traces (the service's) is enough.
        else if (Core.History.HistoryServiceControl.QueryState() == Core.History.HistoryServiceState.Running) engine.PreferServiceFeed();
        // A copy that was killed while its Disk page showed files left its trace running; stop it.
        if (IsElevated) Core.Disk.DiskFileActivity.StopLeftover();
    }

    /// <summary>Raised on the UI thread with the newest snapshot.</summary>
    public event Action<MonitorSnapshot>? Updated;
    /// <summary>A collection failed; the numbers on screen are from the last good snapshot until the next one arrives.</summary>
    public event Action<string>? CollectionFailed;
    public event Action? SearchChanged;
    public event Action? FirewallChanged;

    public MonitorSnapshot? Latest { get; private set; }
    public bool IsElevated { get; }
    public HistoryBuffer CpuHistory { get; } = new(HistoryLength);
    public HistoryBuffer MemoryHistory { get; } = new(HistoryLength);
    public HistoryBuffer ReceiveHistory { get; } = new(HistoryLength);
    public HistoryBuffer SendHistory { get; } = new(HistoryLength);
    public HashSet<string> BlockRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-app network rules by executable path, as stored by the last change.</summary>
    public Dictionary<string, AppNetworkPolicy> Policies { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FirewallStateKnown { get; private set; }

    public string SearchText
    {
        get => searchText;
        set
        {
            value ??= "";
            if (value == searchText) return;
            searchText = value;
            SearchChanged?.Invoke();
        }
    }

    public bool Paused
    {
        get => engine.Paused;
        set => engine.Paused = value;
    }

    /// <summary>
    /// While the window is minimized, snapshots keep feeding the chart history but pages are not updated.
    /// Resuming delivers the newest snapshot immediately.
    /// </summary>
    public bool ViewSuspended
    {
        get => viewSuspended;
        set
        {
            if (viewSuspended == value) return;
            viewSuspended = value;
            if (!value && Latest is { } latest) Updated?.Invoke(latest);
        }
    }

    public TimeSpan Interval
    {
        get => engine.Interval;
        set
        {
            engine.Interval = value;
            settings.RefreshIntervalMilliseconds = (int)value.TotalMilliseconds;
            engine.RequestRefresh();
        }
    }

    public void Start(DispatcherQueue uiDispatcher)
    {
        dispatcher = uiDispatcher;
        engine.Start();
        _ = RefreshFirewallAsync();
    }

    public void RequestRefresh() => engine.RequestRefresh();

    /// <summary>After changing priority or efficiency mode: read them again in the next refresh.</summary>
    public void InvalidateProcessDetails() => engine.InvalidateProcessDetails();

    private void OnSnapshotReady(MonitorSnapshot snapshot)
    {
        Volatile.Write(ref pending, snapshot);
        if (Interlocked.Exchange(ref deliveryQueued, 1) == 1) return;
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Normal, Deliver);
    }

    private void Deliver()
    {
        Interlocked.Exchange(ref deliveryQueued, 0);
        MonitorSnapshot? snapshot = Interlocked.Exchange(ref pending, null);
        if (snapshot is null) return;

        Latest = snapshot;
        SystemSample system = snapshot.System;
        if (system.CpuSampled) CpuHistory.Add(system.CpuPercent);
        MemoryHistory.Add(system.Memory.LoadPercent);
        if (system.NetworkSampled)
        {
            ReceiveHistory.Add(system.NetworkReceiveBytesPerSecond);
            SendHistory.Add(system.NetworkSendBytesPerSecond);
        }

        if (viewSuspended) return;
        try
        {
            Updated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    public bool IsBlocked(string path) =>
        !string.IsNullOrWhiteSpace(path) && BlockRules.Contains(FirewallRules.RuleNameForPath(path));

    public bool HasRules(string path) => !string.IsNullOrWhiteSpace(path) && Policies.ContainsKey(path);

    public NetworkAccess AccessOf(string path) => IsBlocked(path) ? NetworkAccess.Blocked : HasRules(path) ? NetworkAccess.Rules : NetworkAccess.Open;

    /// <summary>A copy of the app's rules to edit, or empty rules for an app that has none.</summary>
    public AppNetworkPolicy PolicyFor(string path)
    {
        if (!Policies.TryGetValue(path, out AppNetworkPolicy? policy)) return new AppNetworkPolicy { Path = path };
        return new AppNetworkPolicy
        {
            Path = path,
            BlockInternet = policy.BlockInternet,
            BlockLan = policy.BlockLan,
            BlockLocalhost = policy.BlockLocalhost,
            BlockInbound = policy.BlockInbound,
            Outgoing = [.. policy.Outgoing],
            Incoming = [.. policy.Incoming]
        };
    }

    public async Task RefreshFirewallAsync()
    {
        try
        {
            BlockRules = await Task.Run(FirewallRules.ReadBlockRuleNames);
            FirewallStateKnown = true;
            List<AppNetworkPolicy> policies = await Task.Run(NetworkPolicyStore.Load);
            Policies = policies.Where(policy => policy.Path.Length > 0).GroupBy(policy => policy.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            FirewallStateKnown = false;
        }
        FirewallChanged?.Invoke();
    }

    /// <summary>
    /// Adds or removes the outbound block rule for an executable. Without elevation this starts a short-lived
    /// elevated copy of the app, so Windows shows one UAC prompt per change.
    /// </summary>
    public async Task<string?> SetBlockedAsync(string path, bool block)
    {
        int exitCode;
        if (IsElevated)
        {
            CommandResult result = await Task.Run(() => FirewallRules.Apply(path, block));
            exitCode = result.Succeeded ? 0 : Math.Max(1, result.ExitCode);
            if (exitCode != 0) return result.FailureSummary();
        }
        else
        {
            var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(block ? Program.FirewallBlockArgument : Program.FirewallUnblockArgument);
            startInfo.ArgumentList.Add(path);
            try
            {
                using Process helper = Process.Start(startInfo)!;
                await helper.WaitForExitAsync();
                exitCode = helper.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Loc.Get("Uac_CancelledFirewall");
            }
            if (exitCode != 0) return Loc.F("Firewall_Failed", exitCode);
        }

        await RefreshFirewallAsync();
        return null;
    }

    /// <summary>
    /// Saves one app's network rules (empty rules remove them). Without elevation this starts a short-lived elevated
    /// copy of the app with the rules as its argument, so Windows shows one UAC prompt per change.
    /// </summary>
    public async Task<string?> SetPolicyAsync(AppNetworkPolicy policy)
    {
        if (IsElevated)
        {
            CommandResult result = await Task.Run(() => FirewallRules.ApplyPolicy(policy));
            if (!result.Succeeded) return result.FailureSummary();
        }
        else
        {
            var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(Program.PolicyApplyArgument);
            startInfo.ArgumentList.Add(Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(policy)));
            int exitCode;
            try
            {
                using Process helper = Process.Start(startInfo)!;
                await helper.WaitForExitAsync();
                exitCode = helper.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Loc.Get("Uac_CancelledRules");
            }
            if (exitCode != 0) return Loc.F("Rules_Failed", exitCode);
        }

        await RefreshFirewallAsync();
        return null;
    }

    /// <summary>Per-file disk trace for the Disk page; runs only while its file list is shown (administrator only).</summary>
    public BetterTaskManager.Core.Disk.DiskFileActivity DiskFiles { get; } = new();

    public void Dispose()
    {
        DiskFiles.Dispose();
        engine.Dispose();
    }
}

/// <summary>What limits an app's network access: nothing, its own rules, or a block of everything.</summary>
public enum NetworkAccess
{
    Open,
    Rules,
    Blocked
}

/// <summary>Fixed-size ring of recent values for the trend charts.</summary>
public sealed class HistoryBuffer
{
    private readonly double[] values;
    private int start;

    public HistoryBuffer(int capacity) => values = new double[capacity];

    public int Count { get; private set; }
    public int Capacity => values.Length;

    public void Add(double value)
    {
        if (Count < values.Length)
        {
            values[(start + Count) % values.Length] = value;
            Count++;
        }
        else
        {
            values[start] = value;
            start = (start + 1) % values.Length;
        }
    }

    public double this[int index] => values[(start + index) % values.Length];

    public double Max()
    {
        double max = 0;
        for (int index = 0; index < Count; index++) max = Math.Max(max, this[index]);
        return max;
    }
}
