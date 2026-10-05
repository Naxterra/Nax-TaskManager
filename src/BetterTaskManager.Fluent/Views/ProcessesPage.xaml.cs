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

    public ProcessesPage()
    {
        ViewModel = new ProcessesViewModel(App.Monitor, App.Settings);
        FlatViewModel = new DetailsViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        groupedReorder = new ColumnReorder(GroupedHeader, ViewModel.Layout);
        flatReorder = new ColumnReorder(FlatHeader, FlatViewModel.Layout);
        flatMode = App.Settings.ProcessFlatView;
        ModeSelector.SelectedItem = flatMode ? FlatMode : ByAppMode;
        ApplyMode();
        UpdateSortIndicators();
        UpdateFlatSortIndicators();
    }

    public ProcessesViewModel ViewModel { get; }
    public DetailsViewModel FlatViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.Updated += OnUpdated;
        App.Monitor.SearchChanged += Refresh;
        App.Monitor.FirewallChanged += Refresh;
        PauseButton.IsChecked = App.Monitor.Paused;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
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
        UpdateCommands();
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
    }

    // ===== Selection =====

    private ProcessSlot? Selected => RowList.SelectedItem as ProcessSlot is { Data.Kind: not RowKind.Section } slot ? slot : null;

    private void RowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
    }

    private void FlatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection) return;
        flatSelectedKey = (FlatList.SelectedItem as DetailSlot)?.Key;
        UpdateCommands();
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

    private void ShowContextMenu(FrameworkElement source, ContextRequestedEventArgs e)
    {
        if (CurrentTarget() is not { } target) return;
        bool hasPath = target.Path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(target.Path);

        var menu = new MenuFlyout();
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
        if (e.TryGetPosition(source, out Windows.Foundation.Point point)) menu.ShowAt(source, point);
        else menu.ShowAt(source);
        e.Handled = true;
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
