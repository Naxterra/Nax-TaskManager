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
        IsElevated = FirewallRules.IsElevated;
        if (!IsElevated) engine.UseServiceFeed();
    }

    /// <summary>Raised on the UI thread with the newest snapshot.</summary>
    public event Action<MonitorSnapshot>? Updated;
    public event Action? SearchChanged;
    public event Action? FirewallChanged;

    public MonitorSnapshot? Latest { get; private set; }
    public bool IsElevated { get; }
    public HistoryBuffer CpuHistory { get; } = new(HistoryLength);
    public HistoryBuffer MemoryHistory { get; } = new(HistoryLength);
    public HistoryBuffer ReceiveHistory { get; } = new(HistoryLength);
    public HistoryBuffer SendHistory { get; } = new(HistoryLength);
    public HashSet<string> BlockRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
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

    public async Task RefreshFirewallAsync()
    {
        try
        {
            BlockRules = await Task.Run(FirewallRules.ReadBlockRuleNames);
            FirewallStateKnown = true;
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

    public void Dispose() => engine.Dispose();
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
