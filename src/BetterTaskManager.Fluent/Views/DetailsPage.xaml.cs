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

public sealed partial class DetailsPage : Page
{
    private static readonly PriorityClass[] Priorities =
        [PriorityClass.Realtime, PriorityClass.High, PriorityClass.AboveNormal, PriorityClass.Normal, PriorityClass.BelowNormal, PriorityClass.Idle];

    private (int Pid, long CreateTime)? selectedKey;
    private bool restoringSelection;

    public DetailsPage()
    {
        ViewModel = new DetailsViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        UpdateSortIndicators();
    }

    public DetailsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.Updated += OnUpdated;
        App.Monitor.SearchChanged += Refresh;
        PauseButton.IsChecked = App.Monitor.Paused;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Monitor.Updated -= OnUpdated;
        App.Monitor.SearchChanged -= Refresh;
    }

    private void OnUpdated(MonitorSnapshot snapshot) => Refresh();

    private void Refresh()
    {
        ViewModel.Refresh();
        RestoreSelection();
        UpdateCommands();
    }

    /// <summary>Rows are reused slots, so selection follows the process (PID and start time), not the list position.</summary>
    private void RestoreSelection()
    {
        restoringSelection = true;
        try
        {
            int index = -1;
            if (selectedKey is { } key)
            {
                for (int i = 0; i < ViewModel.Rows.Count; i++)
                {
                    if (ViewModel.Rows[i].Key == key)
                    {
                        index = i;
                        break;
                    }
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

    private ProcessSample? Selected => (RowList.SelectedItem as DetailSlot)?.Process;

    private void RowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection) return;
        selectedKey = (RowList.SelectedItem as DetailSlot)?.Key;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        ProcessSample? process = Selected;
        bool any = process is not null;
        bool critical = any && NativeProcessInfo.IsCritical(process!.Pid);
        bool hasPath = any && !string.IsNullOrWhiteSpace(process!.Path);
        EndTaskButton.IsEnabled = EndTreeButton.IsEnabled = any;
        PriorityButton.IsEnabled = any && !critical;
        EfficiencyButton.IsEnabled = any && !critical;
        EfficiencyButton.IsChecked = process?.Efficiency == true;
        OpenLocationButton.IsEnabled = PropertiesButton.IsEnabled = CopyPathButton.IsEnabled = hasPath;
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
        {
            ViewModel.Sort(column);
            UpdateSortIndicators();
            RestoreSelection();
        }
    }

    private void UpdateSortIndicators()
    {
        var indicators = new Dictionary<string, FontIcon>
        {
            [DetailsViewModel.SortName] = SortName, [DetailsViewModel.SortPid] = SortPid, [DetailsViewModel.SortStatus] = SortStatus,
            [DetailsViewModel.SortUser] = SortUser, [DetailsViewModel.SortCpu] = SortCpu, [DetailsViewModel.SortMemory] = SortMemory,
            [DetailsViewModel.SortPriority] = SortPriority, [DetailsViewModel.SortDescription] = SortDescription
        };
        foreach (var (column, icon) in indicators)
        {
            icon.Visibility = column == ViewModel.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.SortDescending ? "" : "";
        }
    }

    private void Splitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    private void PriorityMenu_Opening(object sender, object e) => FillPriorityItems(PriorityMenu.Items, Selected);

    /// <summary>Radio items for the six priority classes, the current one checked.</summary>
    private void FillPriorityItems(IList<MenuFlyoutItemBase> items, ProcessSample? process)
    {
        items.Clear();
        if (process is null) return;
        foreach (PriorityClass priority in Priorities)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = DetailSlot.PriorityName(priority),
                GroupName = "Priority",
                IsChecked = process.Priority == priority
            };
            item.Click += (_, _) => _ = SetPriorityAsync(process, priority);
            items.Add(item);
        }
    }

    private void RowList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var source = e.OriginalSource as FrameworkElement;
        DetailSlot? slot = source?.DataContext as DetailSlot ?? (source as ListViewItem)?.Content as DetailSlot ?? RowList.SelectedItem as DetailSlot;
        if (source is null || slot?.Process is not { } process) return;
        RowList.SelectedItem = slot;

        bool critical = NativeProcessInfo.IsCritical(process.Pid);
        bool hasPath = !string.IsNullOrWhiteSpace(process.Path);
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(Loc.Get("Proc_EndTask"), "", (_, _) => _ = EndAsync(process, entireTree: false)));
        menu.Items.Add(MenuItem(Loc.Get("Proc_EndTree"), "", (_, _) => _ = EndAsync(process, entireTree: true)));
        menu.Items.Add(new MenuFlyoutSeparator());
        var priority = new MenuFlyoutSubItem { Text = Loc.Get("Details_SetPriority"), IsEnabled = !critical };
        FillPriorityItems(priority.Items, process);
        menu.Items.Add(priority);
        var efficiency = new ToggleMenuFlyoutItem { Text = Loc.Get("Proc_Efficiency"), IsChecked = process.Efficiency, IsEnabled = !critical };
        efficiency.Click += (_, _) => _ = ToggleEfficiencyAsync(process);
        menu.Items.Add(efficiency);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Menu_OpenLocation"), "", (_, _) => ProcessActions.OpenFileLocation(process.Path), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Proc_Properties"), "", (_, _) => ProcessActions.ShowProperties(process.Path), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Proc_CopyPath"), "", (_, _) => ProcessActions.CopyText(process.Path), hasPath));
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

    private void EndTask_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) _ = EndAsync(process, entireTree: false);
    }

    private void EndTree_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) _ = EndAsync(process, entireTree: true);
    }

    private void Efficiency_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) _ = ToggleEfficiencyAsync(process);
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) ProcessActions.OpenFileLocation(process.Path);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) ProcessActions.ShowProperties(process.Path);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } process) ProcessActions.CopyText(process.Path);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => App.Monitor.Paused = PauseButton.IsChecked == true;

    /// <summary>End task here ends only this process, like Task Manager's Details tab.</summary>
    private async Task EndAsync(ProcessSample process, bool entireTree)
    {
        string name = process.ImageName;
        if (NativeProcessInfo.IsCritical(process.Pid))
        {
            if (!await Dialogs.ConfirmAsync(XamlRoot, Loc.Get("Proc_CriticalTitle"), Loc.F("Proc_CriticalText", name), Loc.Get("Proc_EndAnyway"))) return;
        }
        else if (entireTree && !await Dialogs.ConfirmAsync(XamlRoot, Loc.F("Proc_TreeTitle", name), Loc.Get("Proc_TreeText"), Loc.Get("Proc_EndTree")))
        {
            return;
        }

        ProcessActions.EndResult result = await ProcessActions.EndAsync(new[] { process.Key }, entireTree);
        App.Monitor.RequestRefresh();
        if (result.Failures.Count > 0)
        {
            string hint = App.Monitor.IsElevated ? "" : "\n\n" + Loc.Get("Proc_RestartHint");
            await Dialogs.ShowAsync(XamlRoot, Loc.F("Proc_CouldNotEnd", name), string.Join("\n", result.Failures) + hint);
        }
    }

    private async Task SetPriorityAsync(ProcessSample process, PriorityClass priority)
    {
        if (process.Priority == priority) return;
        if (!await Dialogs.ConfirmAsync(XamlRoot, Loc.F("Details_PriorityTitle", process.ImageName), Loc.Get("Details_PriorityText"), Loc.Get("Details_PriorityButton"))) return;
        ProcessActions.ChangeResult result = await ProcessActions.ChangeAsync(new[] { process.Key },
            (pid, createTime) => ProcessControl.SetPriority(pid, createTime, priority));
        App.Monitor.InvalidateProcessDetails();
        await ReportAsync(result, Loc.F("Details_PriorityFailed", process.ImageName));
    }

    private async Task ToggleEfficiencyAsync(ProcessSample process)
    {
        bool on = !process.Efficiency;
        ProcessActions.ChangeResult result = await ProcessActions.ChangeAsync(new[] { process.Key },
            (pid, createTime) => ProcessControl.SetEfficiency(pid, createTime, on));
        App.Monitor.InvalidateProcessDetails();
        await ReportAsync(result, Loc.F("Proc_EfficiencyFailed", process.ImageName));
    }

    private async Task ReportAsync(ProcessActions.ChangeResult result, string title)
    {
        if (result.Failures.Count == 0) return;
        string hint = result.AccessDenied && !App.Monitor.IsElevated ? "\n\n" + Loc.Get("Proc_RestartHintChange") : "";
        await Dialogs.ShowAsync(XamlRoot, title, string.Join("\n", result.Failures) + hint);
    }
}
