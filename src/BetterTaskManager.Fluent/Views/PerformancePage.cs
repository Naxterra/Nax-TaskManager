using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Controls;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// CPU and memory on one page, in three sections: what the CPU does and which apps use it; where the RAM goes, with
/// the cleanup actions; and the memory settings (standby limit, written files, Windows' memory manager and the
/// per-app memory rules).
/// </summary>
public sealed partial class PerformancePage : Page
{
    public const string CpuSection = "Cpu", MemorySection = "Memory", TuningSection = "Tuning";

    private readonly SelectorBar sections = new();
    private readonly Dictionary<string, FrameworkElement> panels = new();
    private readonly TextBlock stamp = Text("", 12, secondary: true);
    private readonly ScrollViewer scroll;
    private int layoutMode = -1;

    public PerformancePage()
    {
        InitializeComponent();
        var root = new StackPanel { Spacing = 18, Padding = new Thickness(28, 20, 28, 24) };
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 5 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        titleRow.Children.Add(Ui.IconBadge("\uE9D9", "Memory", 38));
        titleRow.Children.Add(Text(Loc.Get("Perf_Title"), 30, true));
        titles.Children.Add(titleRow);
        stamp.Margin = new Thickness(50, 0, 0, 0);
        titles.Children.Add(stamp);
        header.Children.Add(titles);
        var cleanup = BuildCleanupButton();
        Grid.SetColumn(cleanup, 1);
        header.Children.Add(cleanup);
        root.Children.Add(header);

        foreach ((string tag, string key, string glyph) in new[] { (CpuSection, "Perf_SectionCpu", ""), (MemorySection, "Perf_SectionMemory", ""), (TuningSection, "Perf_SectionTuning", "") })
        {
            sections.Items.Add(new SelectorBarItem { Text = Loc.Get(key), Tag = tag, Icon = new FontIcon { Glyph = glyph } });
        }
        sections.SelectionChanged += (_, _) => ShowSelected();
        root.Children.Add(sections);
        root.Children.Add(cleanupBar);

        panels[CpuSection] = BuildCpu();
        panels[MemorySection] = BuildMemory();
        panels[TuningSection] = BuildTuning();
        foreach (FrameworkElement panel in panels.Values) root.Children.Add(panel);

        scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;
        string start = App.Settings.PerformanceSection is CpuSection or MemorySection or TuningSection ? App.Settings.PerformanceSection : MemorySection;
        ShowSection(start);
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Loaded += (_, _) =>
        {
            App.Monitor.Updated -= Update;
            App.Monitor.Updated += Update;
            App.Monitor.FirewallChanged -= ShowMemoryRules;
            App.Monitor.FirewallChanged += ShowMemoryRules;
            if (App.Monitor.Latest is { } snapshot) Update(snapshot);
            ShowTuningState();
        };
        Unloaded += (_, _) =>
        {
            App.Monitor.Updated -= Update;
            App.Monitor.FirewallChanged -= ShowMemoryRules;
        };
    }

    /// <summary>Opens one section: CPU, memory or the memory settings.</summary>
    public void ShowSection(string section)
    {
        sections.SelectedItem = sections.Items.FirstOrDefault(item => Equals(item.Tag, section)) ?? sections.Items[1];
        ShowSelected();
        scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ShowSelected()
    {
        string section = sections.SelectedItem?.Tag as string ?? MemorySection;
        foreach (var (tag, panel) in panels) panel.Visibility = tag == section ? Visibility.Visible : Visibility.Collapsed;
        App.Settings.PerformanceSection = section;
        if (section == TuningSection && IsLoaded) ShowTuningState();
    }

    private void Arrange(double width)
    {
        int mode = width < 950 ? 0 : 1;
        if (layoutMode == mode) return;
        layoutMode = mode;
        int columns = mode == 0 ? 2 : 4;
        foreach (Grid grid in new[] { cpuMetrics, memoryMetrics })
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4 / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((FrameworkElement)grid.Children[i], i / columns); }
        }
        foreach ((Grid grid, FrameworkElement second) in new[] { (cpuLower, (FrameworkElement)cpuAppsCard), (memoryLower, memoryAppsCard) })
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (mode == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            else grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(second, mode == 0 ? 0 : 1);
            Grid.SetRow(second, mode == 0 ? 1 : 0);
        }
    }

    private void Update(MonitorSnapshot snapshot)
    {
        stamp.Text = Loc.F("Perf_Updated", snapshot.Timestamp.ToString("T"), snapshot.CollectionTime.TotalMilliseconds.ToString("0"));
        UpdateCpu(snapshot);
        UpdateMemory(snapshot);
    }

    // ===== CPU =====

    private readonly Grid cpuMetrics = new() { ColumnSpacing = 12, RowSpacing = 12 }, cpuLower = new() { ColumnSpacing = 18, RowSpacing = 18 };
    private readonly StatTile cpuTile = new(), processTile = new(), threadTile = new(), handleTile = new();
    private readonly HistoryBuffer processHistory = new(60), threadHistory = new(60), handleHistory = new(60);
    private readonly TrendChart cpuChart = new() { Height = 150, Filled = true };
    private readonly TextBlock cpuDetail = Text("", 12, secondary: true);
    private readonly StackPanel cpuApps = new();
    private Border cpuAppsCard = null!;
    private DateTime cpuSampledAt;

    private FrameworkElement BuildCpu()
    {
        var panel = new StackPanel { Spacing = 18 };
        StatTile[] tiles = [cpuTile, processTile, threadTile, handleTile];
        string[] labels = ["Tile_Cpu", "Perf_Processes", "Perf_Threads", "Perf_Handles"];
        string[] brushes = ["ChartCpuBrush", "BrandBrush", "ChartReceiveBrush", "ChartMemoryBrush"];
        string[] tones = ["Cpu", "Brand", "Receive", "Memory"];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Caption = Loc.Get(labels[i]);
            tiles[i].Tone = tones[i];
            tiles[i].SetResourceReference(StatTile.StrokeProperty, brushes[i]);
            cpuMetrics.Children.Add(tiles[i]);
        }
        panel.Children.Add(cpuMetrics);

        var chart = new StackPanel { Spacing = 10 };
        chart.Children.Add(Titled("\uE9D9", "Cpu", Loc.Get("Perf_CpuHistory")));
        cpuChart.SetResourceReference(TrendChart.StrokeProperty, "ChartCpuBrush");
        chart.Children.Add(cpuChart);
        chart.Children.Add(cpuDetail);
        cpuLower.Children.Add(Card(chart));

        var leaders = new StackPanel { Spacing = 10 };
        var all = new HyperlinkButton { Content = Loc.Get("Perf_AllByCpu"), Padding = new Thickness(0) };
        all.Click += (_, _) => App.Window.ShowProcessesSorted(ProcessTree.SortCpu);
        leaders.Children.Add(Heading("Perf_CpuApps", all, "\uE9F5", "Cpu"));
        leaders.Children.Add(Text(Loc.Get("Perf_CpuAppsNote"), 12, secondary: true));
        leaders.Children.Add(cpuApps);
        cpuAppsCard = Card(leaders);
        cpuLower.Children.Add(cpuAppsCard);
        panel.Children.Add(cpuLower);
        return panel;
    }

    private void UpdateCpu(MonitorSnapshot snapshot)
    {
        SystemSample system = snapshot.System;
        if (cpuSampledAt != snapshot.Timestamp)
        {
            cpuSampledAt = snapshot.Timestamp;
            processHistory.Add(system.ProcessCount); threadHistory.Add(system.ThreadCount); handleHistory.Add(system.HandleCount);
        }
        cpuTile.Update(system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "–", Loc.F("Design_LogicalCores", Environment.ProcessorCount), App.Monitor.CpuHistory, 100);
        processTile.Update(Format.Count(system.ProcessCount), Loc.F("Perf_Uptime", Format.Duration(system.Uptime)), processHistory, Math.Max(1, processHistory.Max() * 1.2));
        threadTile.Update(Format.Count(system.ThreadCount), "", threadHistory, Math.Max(1, threadHistory.Max() * 1.2));
        handleTile.Update(Format.Count(system.HandleCount), "", handleHistory, Math.Max(1, handleHistory.Max() * 1.2));
        cpuChart.Update(App.Monitor.CpuHistory, 100);
        cpuDetail.Text = Loc.F("Perf_CpuDetail", Format.Count(system.ProcessCount), Format.Count(system.ThreadCount),
            Format.Count(system.HandleCount), Environment.ProcessorCount, Format.Duration(system.Uptime));
        var groups = snapshot.Processes.Where(p => p.Pid > 0).GroupBy(ProcessTree.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Process: g.FirstOrDefault(p => !AppIdentityRules.IsEmbeddedHost(p)) ?? g.First(), Value: g.Sum(p => p.CpuPercent), Count: g.Count()))
            .OrderByDescending(g => g.Value).ThenBy(g => g.Process.DisplayName).Take(8);
        FillApps(cpuApps, groups.Select(g => (g.Process, g.Count, Format.Percent(g.Value))));
    }

    // ===== Memory =====

    private readonly Grid memoryMetrics = new() { ColumnSpacing = 12, RowSpacing = 12 }, memoryLower = new() { ColumnSpacing = 18, RowSpacing = 18 };
    private readonly Grid capacity = new() { Height = 18, ColumnSpacing = 3, CornerRadius = new CornerRadius(5) };
    private readonly StatTile used = new(), available = new(), cached = new(), commit = new();
    private readonly TrendChart memoryChart = new() { Height = 100 };
    private readonly TextBlock total = Text("", 14, true);
    private readonly TextBlock reconciliation = Text("", 12, secondary: true), availableNote = Text("", 12, secondary: true);
    private readonly TextBlock usedHeader = Text("", 14, true), readyHeader = Text("", 14, true);
    private readonly StackPanel memoryApps = new();
    private readonly List<TextBlock> values = new();
    private readonly HistoryBuffer availableHistory = new(60), cachedHistory = new(60), commitHistory = new(60);
    private Border memoryAppsCard = null!;
    private DateTime memorySampledAt;

    private FrameworkElement BuildMemory()
    {
        var panel = new StackPanel { Spacing = 18 };
        StatTile[] tiles = [used, available, cached, commit];
        string[] labels = ["Mem_InUse", "Design_Available", "Memory_Cached", "Design_Committed"];
        string[] brushes = ["ChartMemoryBrush", "BrandBrush", "ChartReceiveBrush", "ChartCpuBrush"];
        string[] tones = ["Memory", "Brand", "Receive", "Cpu"];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Caption = Loc.Get(labels[i]);
            tiles[i].Tone = tones[i];
            tiles[i].SetResourceReference(StatTile.StrokeProperty, brushes[i]);
            memoryMetrics.Children.Add(tiles[i]);
        }
        panel.Children.Add(memoryMetrics);

        var composition = new StackPanel { Spacing = 12 };
        composition.Children.Add(Heading("Memory_Physical", total, "\uE964", "Memory"));
        composition.Children.Add(Text(Loc.Get("Memory_PhysicalNote"), 12, secondary: true));
        string[] capacityBrushes = ["ChartMemoryBrush", "MemoryModifiedBrush", "BrandBrush"];
        string[] capacityLabels = ["Mem_InUse", "Mem_Modified", "Design_Available"];
        for (int i = 0; i < 3; i++)
        {
            capacity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var block = new Border(); block.SetResourceReference(Border.BackgroundProperty, capacityBrushes[i]);
            ToolTipService.SetToolTip(block, Loc.Get(capacityLabels[i]));
            Grid.SetColumn(block, i); capacity.Children.Add(block);
        }
        composition.Children.Add(capacity);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        for (int i = 0; i < 3; i++)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4) };
            dot.SetResourceReference(Border.BackgroundProperty, capacityBrushes[i]);
            item.Children.Add(dot); item.Children.Add(Text(Loc.Get(capacityLabels[i]), 12, secondary: true)); legend.Children.Add(item);
        }
        composition.Children.Add(legend);
        memoryChart.SetResourceReference(TrendChart.StrokeProperty, "ChartMemoryBrush");
        composition.Children.Add(memoryChart);
        panel.Children.Add(Card(composition));

        var accounting = new StackPanel { Spacing = 12 };
        accounting.Children.Add(Titled("\uE9F9", "Memory", Loc.Get("Memory_Accounting")));
        string[] names = ["Mem_Apps", "Memory_PagedPool", "Memory_NonpagedPool", "Mem_Cache", "Mem_Drivers", "Memory_Residual", "Mem_Modified", "Mem_Standby", "Mem_Free"];
        foreach (string name in names)
        {
            // Two groups: what is really occupied, and what Windows hands to programs the moment they ask.
            if (name == "Mem_Apps") accounting.Children.Add(usedHeader);
            if (name == "Mem_Modified")
            {
                usedHeader.Margin = new Thickness(0, 4, 0, 0);
                readyHeader.Margin = new Thickness(0, 10, 0, 0);
                accounting.Children.Add(readyHeader);
            }
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(Loc.Get(name), 13, secondary: true));
            var value = Text("", 13, true); Grid.SetColumn(value, 1); row.Children.Add(value); values.Add(value);
            accounting.Children.Add(row);
        }
        accounting.Children.Add(Text(Loc.Get("Memory_FreeNote"), 12, secondary: true));
        accounting.Children.Add(reconciliation); accounting.Children.Add(availableNote);
        memoryLower.Children.Add(Card(accounting));

        var leaders = new StackPanel { Spacing = 10 };
        var showProcesses = new HyperlinkButton { Content = Loc.Get("Memory_AllProcesses"), Padding = new Thickness(0) };
        showProcesses.Click += (_, _) => App.Window.ShowProcessesSorted(ProcessTree.SortMemory);
        leaders.Children.Add(Heading("Memory_AppsTitle", showProcesses, "\uE9F5", "Brand"));
        leaders.Children.Add(Text(Loc.Get("Memory_AppsNote"), 12, secondary: true));
        leaders.Children.Add(memoryApps);
        memoryAppsCard = Card(leaders);
        memoryLower.Children.Add(memoryAppsCard);
        panel.Children.Add(memoryLower);

        var explanation = new StackPanel { Spacing = 6 };
        explanation.Children.Add(Titled("\uE946", "Warn", Loc.Get("Memory_Why")));
        explanation.Children.Add(Text(Loc.Get("Memory_WhyText"), 12, secondary: true));
        panel.Children.Add(Card(explanation));
        return panel;
    }

    private void UpdateMemory(MonitorSnapshot snapshot)
    {
        MemoryBreakdown m = snapshot.System.Memory;
        if (memorySampledAt != snapshot.Timestamp)
        {
            memorySampledAt = snapshot.Timestamp;
            availableHistory.Add(m.Available); cachedHistory.Add(m.Standby + m.Modified); commitHistory.Add(m.CommitTotal);
        }
        used.Update(Format.Gigabytes(m.InUse), Format.WholePercent(m.LoadPercent), App.Monitor.MemoryHistory, 100);
        available.Update(Format.Gigabytes(m.Available), Loc.Get("Memory_Ready"), availableHistory, Math.Max(1, m.Total));
        cached.Update(m.PageListsAvailable && m.ModifiedAvailable ? Format.Gigabytes(m.Standby + m.Modified) : Loc.Get("Memory_Unavailable"), Loc.Get("Memory_CachedNote"), cachedHistory, Math.Max(1, m.Total));
        commit.Update(Format.Gigabytes(m.CommitTotal), Loc.F("Memory_CommitLimit", Format.Gigabytes(m.CommitLimit)), commitHistory, Math.Max(1, m.CommitLimit));
        total.Text = Loc.F("Memory_Usable", Format.Gigabytes(m.Total));
        capacity.ColumnDefinitions[0].Width = new GridLength(m.InUse, GridUnitType.Star);
        capacity.ColumnDefinitions[1].Width = new GridLength(m.Modified, GridUnitType.Star);
        capacity.ColumnDefinitions[2].Width = new GridLength(m.Available, GridUnitType.Star);
        memoryChart.Update(App.Monitor.MemoryHistory, 100);
        usedHeader.Text = Loc.F("Memory_UsedGroup", Format.Gigabytes(m.InUse));
        readyHeader.Text = Loc.F("Memory_ReadyGroup", Format.Gigabytes(m.Available + m.Modified));
        long[] bytes = [m.ProcessPrivate, m.PagedPoolResident, m.NonpagedPool, m.FileCache, m.Drivers, m.Unattributed, m.Modified, m.Standby, m.Free];
        for (int i = 0; i < bytes.Length; i++)
        {
            bool known = i == 0 || (i < 6 ? m.Detailed : i == 6 ? m.ModifiedAvailable : m.PageListsAvailable);
            values[i].Text = known ? Format.Memory(bytes[i]) : Loc.Get("Memory_Unavailable");
        }
        reconciliation.Text = !m.Detailed ? Loc.Get("Memory_Partial")
            : m.AttributionDifference < 0 ? Loc.F("Memory_Excess", Format.Memory(-m.AttributionDifference))
            : Loc.F("Memory_ResidualNote", Format.Memory(m.Unattributed));
        availableNote.Text = m.PageListsAvailable ? Loc.F("Memory_AvailableDifference", Format.Memory(Math.Abs(m.AvailableDifference))) : Loc.Get("Memory_PageListsMissing");
        if (!m.ModifiedAvailable) reconciliation.Text += " " + Loc.Get("Memory_ModifiedMissing");
        var groups = snapshot.Processes.Where(p => p.Pid > 0).GroupBy(ProcessTree.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Process: g.FirstOrDefault(p => !AppIdentityRules.IsEmbeddedHost(p)) ?? g.First(), Bytes: g.Sum(p => p.PrivateWorkingSet), Count: g.Count()))
            .OrderByDescending(g => g.Bytes).ThenBy(g => g.Process.DisplayName).Take(8);
        FillApps(memoryApps, groups.Select(g => (g.Process, g.Count, Format.Memory(g.Bytes))));
    }

    // ===== Freeing memory =====

    private readonly InfoBar cleanupBar = new() { IsOpen = false, IsClosable = true };
    private SplitButton cleanupButton = null!;

    private sealed record CleanupAction(string Title, string Explanation, string Button);

    private static readonly Dictionary<string, CleanupAction> CleanupActions = new()
    {
        ["Trim"] = new(Loc.Get("Cleanup_TrimTitle"), Loc.Get("Cleanup_TrimText"), Loc.Get("Cleanup_TrimButton")),
        ["Standby"] = new(Loc.Get("Cleanup_StandbyTitle"), Loc.Get("Cleanup_StandbyText"), Loc.Get("Cleanup_StandbyButton")),
        ["System"] = new(Loc.Get("Cleanup_SystemTitle"), Loc.Get("Cleanup_SystemText"), Loc.Get("Cleanup_SystemButton"))
    };

    private SplitButton BuildCleanupButton()
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach ((string kind, string key, string glyph) in new[] { ("Trim", "Perf_Trim", ""), ("Standby", "Perf_ClearStandby", ""), ("System", "Perf_EmptyAll", "") })
        {
            var item = new MenuFlyoutItem { Text = Loc.Get(key), Icon = new FontIcon { Glyph = glyph } };
            if (kind != "Trim" && !App.Monitor.IsElevated)
            {
                item.IsEnabled = false;
                item.Text += Loc.Get("Cleanup_NeedsAdmin");
            }
            item.Click += async (_, _) =>
            {
                CleanupAction action = CleanupActions[kind];
                await RunCleanupsAsync([kind], action.Title, action.Explanation + "\n\n" + Loc.Get("Cleanup_Note"), action.Button);
            };
            menu.Items.Add(item);
        }
        cleanupButton = new SplitButton
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = Loc.Get("Perf_FreeUp") } } },
            Flyout = menu,
            VerticalAlignment = VerticalAlignment.Top
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cleanupButton, Loc.Get("Perf_FreeUp"));
        cleanupButton.Click += async (_, _) =>
        {
            // The primary button runs every cleanup available at this rights level in one click.
            string[] kinds = App.Monitor.IsElevated ? ["Trim", "Standby", "System"] : ["Trim"];
            string body = (App.Monitor.IsElevated ? Loc.Get("Cleanup_AllTextElevated") : Loc.Get("Cleanup_AllTextStandard")) + "\n\n" + Loc.Get("Cleanup_Note");
            await RunCleanupsAsync(kinds, Loc.Get("Cleanup_AllTitle"), body, Loc.Get("Cleanup_AllButton"));
        };
        return cleanupButton;
    }

    private async Task RunCleanupsAsync(IReadOnlyList<string> kinds, string title, string body, string button)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = button,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        cleanupButton.IsEnabled = false;
        MemoryBreakdown? before = App.Monitor.Latest?.System.Memory;
        var messages = new List<string>();
        bool allOk = true;
        foreach (string kind in kinds)
        {
            (bool ok, string message) = await RunOneAsync(kind);
            allOk &= ok;
            messages.Add(message);
        }

        // A fixed delay can reuse the pre-cleanup sample when paused or collecting slowly.
        MemoryBreakdown? after = await SampleAfterAsync(DateTime.Now);
        string combined = string.Join("\n", messages);
        if (allOk && before is { PageListsAvailable: true, ModifiedAvailable: true } && after is { PageListsAvailable: true, ModifiedAvailable: true })
        {
            combined += Loc.F("Cleanup_Delta", Format.Gigabytes(before.InUse), Format.Gigabytes(after.InUse),
                Format.Gigabytes(before.Standby), Format.Gigabytes(after.Standby),
                Format.Gigabytes(before.Free), Format.Gigabytes(after.Free));
        }
        else if (allOk) combined += "\n" + Loc.Get("Cleanup_NoMeasurement");
        cleanupBar.Severity = allOk ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        cleanupBar.Title = allOk ? Loc.Get("Cleanup_Done") : Loc.Get("Cleanup_NotDone");
        cleanupBar.Message = combined;
        cleanupBar.IsOpen = true;
        cleanupButton.IsEnabled = true;
    }

    private static async Task<MemoryBreakdown?> SampleAfterAsync(DateTime finished)
    {
        if (App.Monitor.Paused) return null;
        var next = new TaskCompletionSource<MemoryBreakdown>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(MonitorSnapshot snapshot)
        {
            if (snapshot.Timestamp - snapshot.CollectionTime >= finished) next.TrySetResult(snapshot.System.Memory);
        }
        App.Monitor.Updated += Observe;
        try
        {
            App.Monitor.RequestRefresh();
            return await Task.WhenAny(next.Task, Task.Delay(3000)) == next.Task ? await next.Task : null;
        }
        finally { App.Monitor.Updated -= Observe; }
    }

    private static async Task<(bool Succeeded, string Message)> RunOneAsync(string kind)
    {
        switch (kind)
        {
            case "Trim":
                return await Task.Run(() =>
                {
                    TrimResult trim = MemoryCleanup.TrimAllWorkingSets(Environment.ProcessId);
                    string refused = trim.Denied == 0 ? "" : App.Monitor.IsElevated
                        ? Loc.F("Cleanup_ProtectedRefused", trim.Denied)
                        : Loc.F("Cleanup_NeedAdminCount", trim.Denied);
                    return (true, Loc.F("Cleanup_Trimmed", Format.Count(trim.Trimmed)) + refused);
                });
            case "Standby":
                return ToTuple(await Task.Run(MemoryCleanup.PurgeStandbyList), "Cleanup_StandbyDone");
            default:
                return ToTuple(await Task.Run(MemoryCleanup.EmptySystemWorkingSets), "Cleanup_SystemDone");
        }
    }

    private static (bool, string) ToTuple(CleanupResult result, string doneKey) =>
        (result.Succeeded, result.Succeeded ? Loc.Get(doneKey) : Loc.CleanupFailure(result));

    // ===== Memory settings =====

    private static readonly (string Feature, string Title, string Note)[] Switches =
    [
        (MemoryManager.MemoryCompression, "Tuning_Compression", "Tuning_CompressionNote"),
        (MemoryManager.PageCombining, "Tuning_PageCombining", "Tuning_PageCombiningNote"),
        (MemoryManager.ApplicationLaunchPrefetching, "Tuning_Prefetch", "Tuning_PrefetchNote"),
        (MemoryManager.ApplicationPreLaunch, "Tuning_PreLaunch", "Tuning_PreLaunchNote"),
        (MemoryManager.SysMain, "Tuning_SysMain", "Tuning_SysMainNote")
    ];

    private readonly ComboBox standbyBox = new() { MinWidth = 150 };
    private readonly ToggleSwitch cacheTrimToggle = Toggle(Loc.Get("Tuning_CacheTrim"));
    private readonly TextBlock standbyStatus = Note(""), cacheTrimStatus = Note(""), managerState = Note(""), tuningError = Note("");
    private readonly Dictionary<string, ToggleSwitch> featureToggles = new();
    private readonly StackPanel memoryRules = new() { Spacing = 4 };
    private bool loadingTuning;

    private FrameworkElement BuildTuning()
    {
        var panel = new StackPanel { Spacing = 18 };
        panel.Children.Add(Note(Loc.Get("Tuning_Intro")));
        tuningError.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        tuningError.Visibility = Visibility.Collapsed;
        panel.Children.Add(tuningError);

        // The two cache settings the History service applies.
        var cache = new StackPanel { Spacing = 16 };
        cache.Children.Add(Titled("\uE81E", "Receive", Loc.Get("Tuning_CacheTitle")));
        foreach ((string tag, string label) in new[] { ("off", Loc.Get("Tuning_Off")), ("512", "512 MB"), ("1024", "1 GB"), ("2048", "2 GB"), ("4096", "4 GB"), ("8192", "8 GB") })
        {
            standbyBox.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(standbyBox, Loc.Get("Tuning_Standby"));
        standbyBox.SelectionChanged += StandbyBox_SelectionChanged;
        cache.Children.Add(SettingRow(Loc.Get("Tuning_Standby"), Loc.Get("Tuning_StandbyNote"), standbyBox, standbyStatus));
        cacheTrimToggle.Toggled += CacheTrimToggle_Toggled;
        cache.Children.Add(SettingRow(Loc.Get("Tuning_CacheTrim"), Loc.Get("Tuning_CacheTrimNote"), cacheTrimToggle, cacheTrimStatus));
        panel.Children.Add(Card(cache));

        // Windows' own memory manager switches.
        var manager = new StackPanel { Spacing = 16 };
        manager.Children.Add(Titled("\uE713", "Gpu", Loc.Get("Tuning_ManagerTitle")));
        manager.Children.Add(managerState);
        foreach ((string feature, string title, string note) in Switches)
        {
            ToggleSwitch toggle = Toggle(Loc.Get(title));
            toggle.Tag = feature;
            toggle.IsEnabled = false;
            toggle.Toggled += FeatureToggle_Toggled;
            featureToggles[feature] = toggle;
            manager.Children.Add(SettingRow(Loc.Get(title), Loc.Get(note), toggle, null));
        }
        manager.Children.Add(Note(Loc.Get("Tuning_RestartNote")));
        panel.Children.Add(Card(manager));

        // Per-app memory rules live with the other rules; this card lists them and links there.
        var rules = new StackPanel { Spacing = 12 };
        var open = new Button { Content = Loc.Get("Tuning_OpenRules") };
        open.Click += (_, _) => App.Window.NavigatePage("Rules");
        rules.Children.Add(Heading("Tuning_RulesTitle", open, "\uE8D7", "Rose"));
        rules.Children.Add(Note(Loc.Get("Tuning_RulesNote")));
        rules.Children.Add(memoryRules);
        panel.Children.Add(Card(rules));
        return panel;
    }

    private void ShowTuningState()
    {
        loadingTuning = true;
        long? limit = StandbyLimiter.LimitBytes;
        string tag = limit is { } bytes ? (bytes / 1048576).ToString(System.Globalization.CultureInfo.InvariantCulture) : "off";
        standbyBox.SelectedItem = standbyBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == tag) ?? standbyBox.Items[0];
        bool service = HistoryServiceControl.QueryState() == HistoryServiceState.Running;
        standbyStatus.Text = !service ? Loc.Get("Tuning_NoService") : limit is { } on ? Loc.F("Standby_On", Format.Memory(on)) : Loc.Get("Standby_Off");
        cacheTrimToggle.IsOn = Core.Disk.CacheTrimmer.Enabled;
        cacheTrimStatus.Text = !service ? Loc.Get("Tuning_NoService") : cacheTrimToggle.IsOn ? Loc.Get("CacheTrim_On") : Loc.Get("CacheTrim_Off");
        loadingTuning = false;
        ShowMemoryRules();
        _ = ShowManagerStateAsync();
    }

    private async Task ShowManagerStateAsync()
    {
        MemoryManagerState? state = await Task.Run(MemoryManager.Read);
        loadingTuning = true;
        foreach ((string feature, ToggleSwitch toggle) in featureToggles)
        {
            toggle.IsEnabled = state is not null;
            toggle.IsOn = state is not null && feature switch
            {
                MemoryManager.MemoryCompression => state.MemoryCompression,
                MemoryManager.PageCombining => state.PageCombining,
                MemoryManager.ApplicationLaunchPrefetching => state.ApplicationLaunchPrefetching,
                MemoryManager.ApplicationPreLaunch => state.ApplicationPreLaunch,
                _ => state.SysMainEnabled
            };
        }
        loadingTuning = false;
        managerState.Text = state is null ? Loc.Get("Tuning_StateUnknown")
            : Loc.F("Tuning_StateAsOf", state.ReadAtUtc.ToLocalTime().ToString("g"), Loc.Get(state.SysMainRunning ? "Tuning_SysMainRunning" : "Tuning_SysMainStopped"));
    }

    private void ShowMemoryRules()
    {
        memoryRules.Children.Clear();
        if (App.Monitor.MemoryRules.Count == 0)
        {
            memoryRules.Children.Add(Note(Loc.Get("Tuning_NoRules")));
            return;
        }
        foreach (AppMemoryRule rule in App.Monitor.MemoryRules.Values.OrderBy(rule => RulesPage.DisplayName(rule.Path), StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Image { Source = IconCache.Get(rule.Path), Width = 20, Height = 20 });
            var name = Text(RulesPage.DisplayName(rule.Path), 13);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var parts = new List<string>();
            if (rule.LimitMegabytes is { } mb) parts.Add(Loc.F("RulesPage_LimitShort", Format.Memory(mb * 1048576L)));
            if (rule.Priority is { } priority) parts.Add(Loc.F("RulesPage_PriorityShort", Loc.Get("RulesPage_Priority" + priority)));
            var detail = Text(string.Join(" · ", parts), 12, secondary: true);
            Grid.SetColumn(detail, 2);
            row.Children.Add(detail);
            string path = rule.Path;
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            ToolTipService.SetToolTip(button, rule.Path);
            button.Click += (_, _) => App.Window.ShowRules(path);
            memoryRules.Children.Add(button);
        }
    }

    private async void StandbyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingTuning || standbyBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        standbyBox.IsEnabled = false;
        // The limit lives in the protected data folder, read by the History service.
        ShowTuningError(await ElevatedHelper.RunAsync(Program.StandbyLimitArgument, tag));
        standbyBox.IsEnabled = true;
        ShowTuningState();
    }

    private async void CacheTrimToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loadingTuning) return;
        cacheTrimToggle.IsEnabled = false;
        ShowTuningError(await ElevatedHelper.RunAsync(cacheTrimToggle.IsOn ? Program.CacheTrimOnArgument : Program.CacheTrimOffArgument));
        cacheTrimToggle.IsEnabled = true;
        ShowTuningState();
    }

    private async void FeatureToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loadingTuning || sender is not ToggleSwitch { Tag: string feature } toggle) return;
        toggle.IsEnabled = false;
        ShowTuningError(await ElevatedHelper.RunAsync(Program.MemoryFeatureArgument, feature, toggle.IsOn ? "on" : "off"));
        await ShowManagerStateAsync();
    }

    private void ShowTuningError(string? error)
    {
        tuningError.Text = error ?? "";
        tuningError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===== Helpers =====

    /// <summary>Fills a card's app list with clickable rows: icon, name with process count, and one value.</summary>
    private static void FillApps(StackPanel list, IEnumerable<(ProcessSample Process, int Count, string Value)> apps)
    {
        int index = 0;
        foreach (var app in apps)
        {
            if (list.Children.Count == index) list.Children.Add(AppRow());
            var button = (Button)list.Children[index++];
            button.Tag = app.Process;
            var row = (Grid)button.Content;
            ((Image)row.Children[0]).Source = IconCache.Get(app.Process.Path);
            ((TextBlock)row.Children[1]).Text = Loc.AppName(app.Process.DisplayName) + (app.Count > 1 ? $" ({app.Count})" : "");
            ((TextBlock)row.Children[2]).Text = app.Value;
            ToolTipService.SetToolTip(button, app.Process.Path);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Loc.Get("Design_InspectApp") + " " + app.Process.DisplayName);
        }
        while (list.Children.Count > index) list.Children.RemoveAt(list.Children.Count - 1);
    }

    private static Button AppRow()
    {
        var row = new Grid { Height = 38, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Image { Width = 20, Height = 20 });
        var name = Text("", 13); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1); row.Children.Add(name);
        var value = Text("", 13, true); Grid.SetColumn(value, 2); row.Children.Add(value);
        var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        button.Click += (_, _) => { if (button.Tag is ProcessSample process) App.Window.ShowProcess(process); };
        return button;
    }

    private static TextBlock Text(string value, double size, bool strong = false, bool secondary = false)
    {
        var block = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"); return block;
    }

    private static TextBlock Note(string value) => Text(value, 12, secondary: true);

    private static ToggleSwitch Toggle(string name)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    /// <summary>A title with its explanation (and an optional live status) on the left and the control on the right.</summary>
    private static Grid SettingRow(string title, string description, FrameworkElement control, TextBlock? status)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(Text(title, 14));
        if (status is not null) texts.Children.Add(status);
        texts.Children.Add(Note(description));
        row.Children.Add(texts);
        control.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Grid Heading(string label, FrameworkElement right, string glyph, string tone)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Titled(glyph, tone, Loc.Get(label))); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }

    /// <summary>A card title with a small colored badge in front.</summary>
    private static StackPanel Titled(string glyph, string tone, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(Ui.IconBadge(glyph, tone, 28));
        row.Children.Add(Text(title, 16, true));
        return row;
    }

    private static Border Card(UIElement content)
    {
        var card = new Border { Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeBrush"); return card;
    }
}
