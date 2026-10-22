using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class ProcessesPage : Page
{
    private const string FirewallGlyph = "";

    private string? selectedKey;
    private (int Pid, long CreateTime)? flatSelectedKey;
    private bool restoringSelection;
    private bool flatMode;
    private readonly ColumnReorder groupedReorder, flatReorder;
    private readonly Button detailOpen, detailFirewall, detailRules, detailEnd, detailNetwork;
    private string? detailKey;
    private HistoryBuffer detailCpu = new(MonitorHost.HistoryLength), detailMemory = new(MonitorHost.HistoryLength);

    public ProcessesPage()
    {
        ViewModel = new ProcessesViewModel(App.Monitor, App.Settings);
        FlatViewModel = new DetailsViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        groupedReorder = new ColumnReorder(GroupedHeader, ViewModel.Layout);
        flatReorder = new ColumnReorder(FlatHeader, FlatViewModel.Layout);
        flatMode = App.Settings.ProcessFlatView;
        ModeSelector.SelectedItem = flatMode ? FlatMode : ByAppMode;
        CpuTile.Caption = Loc.Get("Tile_Cpu");
        MemoryTile.Caption = Loc.Get("Tile_Memory");
        DiskTile.Caption = Loc.Get("Tile_Disk");
        NetworkTile.Caption = Loc.Get("Tile_Network");
        GpuTile.Caption = Loc.Get("Tile_Gpu");
        Details.Tile1.Caption = "CPU";
        Details.Tile1.Stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChartCpuBrush"];
        Details.Tile2.Caption = Loc.Get("Tile_Memory");
        Details.Tile2.Stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChartMemoryBrush"];
        detailOpen = Controls.DetailPane.ActionButton("\uE838", Loc.Get("Menu_OpenLocation"), OpenLocation_Click);
        detailFirewall = Controls.DetailPane.ActionButton("\uE785", Loc.Get("Firewall_Block"), Firewall_Click);
        detailRules = Controls.DetailPane.ActionButton("\uE8D7", Loc.Get("Rules_Menu"), (_, _) => { if (CurrentTarget() is { Path.Length: > 0 } target) _ = AppRulesDialog.ShowAsync(XamlRoot, target.Path, target.Name); });
        detailEnd = Controls.DetailPane.ActionButton("\uE711", Loc.Get("Proc_EndTask"), EndTask_Click);
        Controls.DetailPane.MakeDanger(detailEnd);
        detailNetwork = Controls.DetailPane.ActionButton("\uE968", Loc.Get("Workflow_Connections"), (_, _) => ShowNetwork());
        Details.SetActions([detailNetwork, detailOpen, detailFirewall, detailRules, detailEnd]);
        DetailsToggle.IsChecked = App.Settings.ShowDetailsPane;
        Details.Visibility = App.Settings.ShowDetailsPane ? Visibility.Visible : Visibility.Collapsed;
        ApplyMode();
        UpdateSortIndicators();
        UpdateFlatSortIndicators();
        SizeChanged += (_, _) => { PageCommands.MaxWidth = Math.Max(180, ActualWidth - 440); UpdateDetails(); };
        RowList.Loaded += (_, _) => { if (RowList.SelectedItem is { } item) RowList.ScrollIntoView(item); };
    }

    public ProcessesViewModel ViewModel { get; }
    public DetailsViewModel FlatViewModel { get; }

    public void ShowProcess(ProcessSample process)
    {
        flatMode = false;
        App.Settings.ProcessFlatView = false;
        ModeSelector.SelectedItem = ByAppMode;
        selectedKey = ProcessTree.GroupKey(process);
        ApplyMode();
        Refresh();
        if (RowList.SelectedItem is { } item) RowList.ScrollIntoView(item);
    }

    private void ShowNetwork()
    {
        if (CurrentTarget() is not { } target || App.Monitor.Latest is not { } snapshot) return;
        var keys = target.Processes.ToHashSet();
        if (snapshot.Processes.FirstOrDefault(p => keys.Contains(p.Key)) is { } process) App.Window.ShowAppNetwork(process);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded can arrive twice without Unloaded in between; never subscribe twice.
        App.Monitor.Updated -= OnUpdated;
        App.Monitor.SearchChanged -= Refresh;
        App.Monitor.FirewallChanged -= Refresh;
        App.Monitor.Updated += OnUpdated;
        App.Monitor.SearchChanged += Refresh;
        App.Monitor.FirewallChanged += Refresh;
        PauseButton.IsChecked = App.Monitor.Paused;
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Monitor.Updated -= OnUpdated;
        App.Monitor.SearchChanged -= Refresh;
        App.Monitor.FirewallChanged -= Refresh;
    }

    private void OnUpdated(MonitorSnapshot snapshot) => Refresh();

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        flatMode = ReferenceEquals(ModeSelector.SelectedItem, FlatMode);
        App.Settings.ProcessFlatView = flatMode;
        ApplyMode();
        Refresh();
    }

    private void ApplyMode()
    {
        GroupedHeader.Visibility = RowList.Visibility = flatMode ? Visibility.Collapsed : Visibility.Visible;
        FlatHeader.Visibility = FlatList.Visibility = flatMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        if (flatMode)
        {
            FlatViewModel.Refresh();
            RestoreFlatSelection();
        }
        else
        {
            ViewModel.Refresh();
            RestoreSelection();
        }
        UpdateSummary();
        UpdateTiles();
        UpdateCommands();
        UpdateDetails();
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowDetailsPane = DetailsToggle.IsChecked == true;
        Details.Visibility = App.Settings.ShowDetailsPane ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The card on the right: the selected app or process with its own CPU and memory history.</summary>
    private void UpdateDetails()
    {
        Target? target = CurrentTarget();
        Details.Visibility = App.Settings.ShowDetailsPane && target is not null && ActualWidth >= 1100 ? Visibility.Visible : Visibility.Collapsed;
        if (Details.Visibility == Visibility.Collapsed) return;
        if (target is null)
        {
            detailKey = null;
            Details.ShowEmpty(Loc.Get("Details_EmptyProcess"));
            return;
        }
        string key = flatMode ? "pid:" + (FlatList.SelectedItem as DetailSlot)?.Key.Pid : "group:" + selectedKey;
        if (key != detailKey)
        {
            detailKey = key;
            detailCpu = new HistoryBuffer(MonitorHost.HistoryLength);
            detailMemory = new HistoryBuffer(MonitorHost.HistoryLength);
        }
        var members = new List<ProcessSample>();
        if (App.Monitor.Latest is { } snapshot)
        {
            var wanted = target.Processes.ToHashSet();
            members = snapshot.Processes.Where(process => wanted.Contains(process.Key)).ToList();
        }
        double cpu = members.Sum(process => process.CpuPercent);
        long memory = members.Sum(process => process.PrivateWorkingSet);
        detailCpu.Add(cpu);
        detailMemory.Add(memory);
        string publisher = members.Select(process => process.Company).FirstOrDefault(company => company.Length > 0) ?? "";
        string count = target.Processes.Count == 1 ? "PID " + target.Processes[0].Pid : Loc.F("Tile_Processes", target.Processes.Count);
        Details.SetHeader(IconCache.Get(target.Path), target.Name, publisher, count);
        Details.Tile1.Update(Format.Percent(cpu), "", detailCpu, 100);
        Details.Tile2.Update(Format.Memory(memory), "", detailMemory, Math.Max(detailMemory.Max(), 1));
        Details.BeginFacts();
        if (target.Processes.Count > 1) Details.Fact("PID", string.Join(", ", target.Processes.Take(6).Select(process => process.Pid)) + (target.Processes.Count > 6 ? ", …" : ""));
        else Details.Fact("PID", target.Processes[0].Pid.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (publisher.Length > 0) Details.Fact(Loc.Get("Details_Publisher"), publisher);
        Details.Fact(Loc.Get("Details_Status"), members.Any(process => process.Suspended) ? Loc.Get("ProcState_Suspended") : target.Efficiency ? Loc.Get("Proc_Efficiency") : Loc.Get("ProcState_Running"));
        Details.Fact(Loc.Get("Details_Threads"), Format.Count(members.Sum(process => process.Threads)));
        Details.Fact(Loc.Get("Details_Handles"), Format.Count(members.Sum(process => process.Handles)));
        Details.Fact(Loc.Get("Details_Connections"), Format.Count(members.Sum(process => process.ConnectionCount)));
        string user = members.Select(process => process.UserName).FirstOrDefault(name => name.Length > 0) ?? "";
        if (user.Length > 0) Details.Fact(Loc.Get("Details_User"), user);
        if (target.Path.Length > 0) Details.Fact(Loc.Get("Details_Path"), target.Path, copyable: true);
        Details.EndFacts();
        bool hasPath = target.Path.Length > 0;
        detailOpen.IsEnabled = detailRules.IsEnabled = detailFirewall.IsEnabled = hasPath;
        detailNetwork.IsEnabled = members.Count > 0;
        Controls.DetailPane.SetActionText(detailFirewall, hasPath && App.Monitor.IsBlocked(target.Path) ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block"));
    }

    /// <summary>The strip under the title: totals with the last minute as sparklines.</summary>
    private void UpdateTiles()
    {
        if (App.Monitor.Latest is not { } snapshot) return;
        SystemSample system = snapshot.System;
        MonitorHost monitor = App.Monitor;
        CpuTile.Update(Format.WholePercent(system.CpuPercent), Loc.F("Tile_Processes", Format.Count(system.ProcessCount)), monitor.CpuHistory, 100);
        MemoryTile.Update(Format.WholePercent(system.Memory.LoadPercent), Loc.F("Tile_MemoryDetail", Format.Gigabytes(system.Memory.InUse), Format.Gigabytes(system.Memory.Total)), monitor.MemoryHistory, 100);
        double read = system.Disks.Sum(disk => disk.ReadPerSecond), write = system.Disks.Sum(disk => disk.WritePerSecond);
        double diskMax = Math.Max(Math.Max(monitor.DiskReadHistory.Max(), monitor.DiskWriteHistory.Max()), 1048576);
        DiskTile.Update(Format.ByteRate(read + write), Loc.F("Tile_DiskDetail", Format.ByteRate(read), Format.ByteRate(write)), monitor.DiskReadHistory, diskMax, monitor.DiskWriteHistory);
        double networkMax = Math.Max(Math.Max(monitor.ReceiveHistory.Max(), monitor.SendHistory.Max()), 131072);
        NetworkTile.Update("↓ " + Format.NetworkRate(system.NetworkReceiveBytesPerSecond), "↑ " + Format.NetworkRate(system.NetworkSendBytesPerSecond), monitor.ReceiveHistory, networkMax, monitor.SendHistory);
        bool gpu = system.GpuAvailable;
        GpuTile.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
        GpuTileColumn.Width = gpu ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (gpu) GpuTile.Update(Format.WholePercent(system.GpuPercent), Loc.F("Tile_PeakDetail", Format.WholePercent(monitor.GpuHistory.Max())), monitor.GpuHistory, 100);
    }

    private void UpdateSummary()
    {
        if (flatMode)
        {
            SummaryText.Text = FlatViewModel.Summary;
            return;
        }
        if (App.Monitor.Latest is not { } snapshot) return;
        SystemSample system = snapshot.System;
        SummaryText.Text = Loc.F("Proc_Summary", Format.Count(system.ProcessCount), Format.Count(system.ThreadCount),
            Format.Count(system.HandleCount), Format.Duration(system.Uptime));
        int apps = ViewModel.Rows.Count(slot => slot.Data?.Kind == RowKind.Group && slot.Data.IsApp);
        ByAppMode.Text = Loc.F("Proc_ByAppCount", apps);
        FlatMode.Text = Loc.F("Proc_AllCount", Format.Count(system.ProcessCount));
    }

    // ===== Selection =====

    private ProcessSlot? Selected => RowList.SelectedItem as ProcessSlot is { Data.Kind: not RowKind.Section } slot ? slot : null;

    private void RowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RowMenu.TrackSelection(e);
        if (restoringSelection) return;
        if (RowList.SelectedItem is ProcessSlot { Data.Kind: RowKind.Section })
        {
            restoringSelection = true;
            RowList.SelectedIndex = -1;
            restoringSelection = false;
            selectedKey = null;
        }
        else
        {
            selectedKey = (RowList.SelectedItem as ProcessSlot)?.Key;
        }
        UpdateCommands();
        UpdateDetails();
    }

    private void FlatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RowMenu.TrackSelection(e);
        if (restoringSelection) return;
        flatSelectedKey = (FlatList.SelectedItem as DetailSlot)?.Key;
        UpdateCommands();
        UpdateDetails();
    }

    private void RestoreSelection()
    {
        restoringSelection = true;
        try
        {
            int index = -1;
            if (selectedKey is not null)
            {
                for (int i = 0; i < ViewModel.Rows.Count; i++)
                {
                    if (ViewModel.Rows[i].Key == selectedKey) { index = i; break; }
                }
            }
            if (RowList.SelectedIndex != index) RowList.SelectedIndex = index;
            if (index < 0) selectedKey = null;
        }
        finally
        {
            restoringSelection = false;
        }
    }

    private void RestoreFlatSelection()
    {
        restoringSelection = true;
        try
        {
            int index = -1;
            if (flatSelectedKey is { } key)
            {
                for (int i = 0; i < FlatViewModel.Rows.Count; i++)
                {
                    if (FlatViewModel.Rows[i].Key == key) { index = i; break; }
                }
            }
            if (FlatList.SelectedIndex != index) FlatList.SelectedIndex = index;
            if (index < 0) flatSelectedKey = null;
        }
        finally
        {
            restoringSelection = false;
        }
    }

    // ===== The process(es) the command bar and menu act on, in either mode =====

    private sealed record Target(string Name, IReadOnlyList<(int Pid, long CreateTime)> Processes, string Path, bool Efficiency, bool Critical);

    private Target? CurrentTarget()
    {
        if (flatMode)
        {
            return FlatList.SelectedItem is DetailSlot { Process: { } process }
                ? new Target(process.ImageName, new[] { process.Key }, process.Path, process.Efficiency, NativeProcessInfo.IsCritical(process.Pid))
                : null;
        }
        return Selected is { Data: { } data }
            ? new Target(data.Name, data.Processes, data.Path, data.Efficiency, ProcessActions.AnyCritical(data.Processes))
            : null;
    }

    private void UpdateCommands()
    {
        Target? target = CurrentTarget();
        bool any = target is not null;
        bool hasPath = target is { Path.Length: > 0 };
        EndTaskButton.IsEnabled = EndTreeButton.IsEnabled = any;
        OpenLocationButton.IsEnabled = PropertiesButton.IsEnabled = CopyPathButton.IsEnabled = hasPath;
        FirewallButton.IsEnabled = hasPath;
        EfficiencyButton.IsEnabled = any && !target!.Critical;
        EfficiencyButton.IsChecked = target?.Efficiency == true;
        bool blocked = hasPath && App.Monitor.IsBlocked(target!.Path);
        FirewallButton.Label = blocked ? Loc.Get("Firewall_AllowShort") : Loc.Get("Firewall_BlockShort");
        FirewallIcon.Glyph = FirewallGlyph;
    }

    // ===== Sorting =====

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (groupedReorder.SuppressClick) return;
        if (sender is FrameworkElement { Tag: string column })
        {
            ViewModel.Sort(column);
            UpdateSortIndicators();
            RestoreSelection();
        }
    }

    private void FlatHeader_Click(object sender, RoutedEventArgs e)
    {
        if (flatReorder.SuppressClick) return;
        if (sender is FrameworkElement { Tag: string column })
        {
            FlatViewModel.Sort(column);
            UpdateFlatSortIndicators();
            RestoreFlatSelection();
        }
    }

    private void UpdateSortIndicators()
    {
        var indicators = new Dictionary<string, FontIcon>
        {
            [ProcessTree.SortName] = SortName, [ProcessTree.SortCpu] = SortCpu, [ProcessTree.SortMemory] = SortMemory,
            [ProcessTree.SortIo] = SortIo, [ProcessTree.SortBandwidth] = SortBandwidth, [ProcessTree.SortNetwork] = SortNetwork, [ProcessTree.SortGpu] = SortGpu,
            [ProcessTree.SortPublisher] = SortPublisher, [ProcessTree.SortPath] = SortPath
        };
        foreach (var (column, icon) in indicators)
        {
            icon.Visibility = column == ViewModel.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.SortDescending ? "" : "";
        }
    }

    private void UpdateFlatSortIndicators()
    {
        var indicators = new Dictionary<string, FontIcon>
        {
            [DetailsViewModel.SortName] = FlatSortName, [DetailsViewModel.SortPid] = FlatSortPid, [DetailsViewModel.SortStatus] = FlatSortStatus,
            [DetailsViewModel.SortUser] = FlatSortUser, [DetailsViewModel.SortCpu] = FlatSortCpu, [DetailsViewModel.SortMemory] = FlatSortMemory, [DetailsViewModel.SortGpu] = FlatSortGpu,
            [DetailsViewModel.SortDescription] = FlatSortDescription
        };
        foreach (var (column, icon) in indicators)
        {
            icon.Visibility = column == FlatViewModel.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = FlatViewModel.SortDescending ? "" : "";
        }
    }

    private void GroupedSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    private void FlatSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) FlatViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    // ===== Grouped-only interactions =====

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessSlot slot }) ViewModel.ToggleExpanded(slot.Key);
        RestoreSelection();
    }

    private void RowList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ProcessSlot { Data: { Expandable: true } } slot })
        {
            ViewModel.ToggleExpanded(slot.Key);
            RestoreSelection();
        }
    }

    // ===== Context menus =====

    private void RowList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var source = e.OriginalSource as FrameworkElement;
        ProcessSlot? slot = source?.DataContext as ProcessSlot ?? (source as ListViewItem)?.Content as ProcessSlot ?? Selected;
        if (source is null || slot is null || slot.Data?.Kind == RowKind.Section) return;
        RowList.SelectedItem = slot;
        ShowContextMenu(source, e);
    }

    private void FlatList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var source = e.OriginalSource as FrameworkElement;
        DetailSlot? slot = source?.DataContext as DetailSlot ?? (source as ListViewItem)?.Content as DetailSlot ?? FlatList.SelectedItem as DetailSlot;
        if (source is null || slot?.Process is null) return;
        FlatList.SelectedItem = slot;
        ShowContextMenu(source, e);
    }

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, true);

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, false);

    /// <summary>The row's "…" button: selects its row and opens the same menu as a right-click.</summary>
    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: var context }) return;
        if (context is ProcessSlot slot) RowList.SelectedItem = slot;
        else if (context is DetailSlot detail) FlatList.SelectedItem = detail;
        if (BuildMenu() is { } menu) RowMenu.Show(menu, sender);
    }

    private void ShowContextMenu(FrameworkElement source, ContextRequestedEventArgs e)
    {
        if (BuildMenu() is not { } menu) return;
        if (e.TryGetPosition(source, out Windows.Foundation.Point point)) menu.ShowAt(source, point);
        else menu.ShowAt(source);
        e.Handled = true;
    }

    private MenuFlyout? BuildMenu()
    {
        if (CurrentTarget() is not { } target) return null;
        bool hasPath = target.Path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(target.Path);

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(Loc.Get("Workflow_Connections"), "\uE968", (_, _) => ShowNetwork()));
        menu.Items.Add(MenuItem(Loc.Get("Proc_EndTask"), "", (_, _) => _ = EndSelectedAsync(entireTree: false)));
        menu.Items.Add(MenuItem(Loc.Get("Proc_EndTree"), "", (_, _) => _ = EndSelectedAsync(entireTree: true)));
        var efficiency = new ToggleMenuFlyoutItem { Text = Loc.Get("Proc_Efficiency"), IsChecked = target.Efficiency, IsEnabled = !target.Critical };
        efficiency.Click += (_, _) => _ = ToggleEfficiencyAsync();
        menu.Items.Add(efficiency);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Menu_OpenLocation"), "", (_, _) => ProcessActions.OpenFileLocation(target.Path), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Proc_Properties"), "", (_, _) => ProcessActions.ShowProperties(target.Path), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Proc_CopyPath"), "", (_, _) => ProcessActions.CopyText(target.Path), hasPath));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(blocked ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block"), FirewallGlyph,
            (_, _) => _ = ToggleFirewallAsync(target.Path), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Rules_Menu"), "", (_, _) => _ = AppRulesDialog.ShowAsync(XamlRoot, target.Path, target.Name), hasPath));
        return menu;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, RoutedEventHandler click, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
        item.Click += click;
        return item;
    }

    // ===== Command bar =====

    private void EndTask_Click(object sender, RoutedEventArgs e) => _ = EndSelectedAsync(entireTree: false);

    private void EndTree_Click(object sender, RoutedEventArgs e) => _ = EndSelectedAsync(entireTree: true);

    private void Efficiency_Click(object sender, RoutedEventArgs e) => _ = ToggleEfficiencyAsync();

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTarget() is { } target) ProcessActions.ShowProperties(target.Path);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTarget() is { } target) ProcessActions.CopyText(target.Path);
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTarget() is { } target) ProcessActions.OpenFileLocation(target.Path);
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTarget() is { } target) _ = ToggleFirewallAsync(target.Path);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => App.Monitor.Paused = PauseButton.IsChecked == true;

    private async Task EndSelectedAsync(bool entireTree)
    {
        if (CurrentTarget() is not { } target) return;
        string name = target.Name;

        if (target.Critical)
        {
            if (!await ConfirmAsync(Loc.Get("Proc_CriticalTitle"), Loc.F("Proc_CriticalText", name), Loc.Get("Proc_EndAnyway"))) return;
        }
        else if (entireTree)
        {
            if (!await ConfirmAsync(Loc.F("Proc_TreeTitle", name), Loc.Get("Proc_TreeText"), Loc.Get("Proc_EndTree"))) return;
        }

        ProcessActions.EndResult result = await ProcessActions.EndAsync(target.Processes, entireTree);
        App.Monitor.RequestRefresh();
        if (result.Failures.Count > 0)
        {
            string hint = App.Monitor.IsElevated ? "" : "\n\n" + Loc.Get("Proc_RestartHint");
            await ShowMessageAsync(Loc.F("Proc_CouldNotEnd", name), string.Join("\n", result.Failures.Take(5)) + hint);
        }
    }

    private async Task ToggleEfficiencyAsync()
    {
        if (CurrentTarget() is not { } target) return;
        bool on = !target.Efficiency;
        ProcessActions.ChangeResult result = await ProcessActions.ChangeAsync(target.Processes,
            (pid, createTime) => ProcessControl.SetEfficiency(pid, createTime, on));
        App.Monitor.InvalidateProcessDetails();
        if (result.Failures.Count > 0)
        {
            string hint = result.AccessDenied && !App.Monitor.IsElevated ? "\n\n" + Loc.Get("Proc_RestartHintChange") : "";
            await ShowMessageAsync(Loc.F("Proc_EfficiencyFailed", target.Name), string.Join("\n", result.Failures.Take(5)) + hint);
        }
    }

    private async Task ToggleFirewallAsync(string path)
    {
        bool block = !App.Monitor.IsBlocked(path);
        if (block && !await ConfirmAsync(Loc.Get("Firewall_ConfirmTitle"), Loc.F("Firewall_ConfirmText", path), Loc.Get("Firewall_BlockButton"))) return;

        string? error = await App.Monitor.SetBlockedAsync(path, block);
        if (error is not null) await ShowMessageAsync(Loc.Get("Firewall_NotChanged"), error);
        UpdateCommands();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Loc.Get("Common_OK")
        };
        await dialog.ShowAsync();
    }
}
