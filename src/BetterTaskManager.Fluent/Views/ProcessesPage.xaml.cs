using BetterTaskManager.Core.Monitoring;
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
    private string? selectedKey;
    private bool restoringSelection;

    public ProcessesPage()
    {
        ViewModel = new ProcessesViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        UpdateSortIndicators();
    }

    public ProcessesViewModel ViewModel { get; }

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

    private void Refresh()
    {
        ViewModel.Refresh();
        RestoreSelection();
        UpdateSummary();
        UpdateCommands();
    }

    private void UpdateSummary()
    {
        if (App.Monitor.Latest is not { } snapshot) return;
        SystemSample system = snapshot.System;
        SummaryText.Text = $"{Format.Count(system.ProcessCount)} processes · {Format.Count(system.ThreadCount)} threads · " +
            $"{Format.Count(system.HandleCount)} handles · up {Format.Duration(system.Uptime)}";
    }

    /// <summary>Rows are reused slots, so selection follows the row key rather than the list position.</summary>
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
                    if (ViewModel.Rows[i].Key == selectedKey)
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

    private void UpdateCommands()
    {
        ProcessSlot? slot = Selected;
        EndTaskButton.IsEnabled = slot is not null;
        bool hasPath = slot is not null && !string.IsNullOrWhiteSpace(slot.Path);
        OpenLocationButton.IsEnabled = hasPath;
        FirewallButton.IsEnabled = hasPath;
        bool blocked = hasPath && App.Monitor.IsBlocked(slot!.Path);
        FirewallButton.Label = blocked ? "Allow network" : "Block network";
        FirewallIcon.Glyph = blocked ? "" : "";
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
            [ProcessTree.SortName] = SortName, [ProcessTree.SortCpu] = SortCpu, [ProcessTree.SortMemory] = SortMemory,
            [ProcessTree.SortIo] = SortIo, [ProcessTree.SortBandwidth] = SortBandwidth, [ProcessTree.SortNetwork] = SortNetwork, [ProcessTree.SortPublisher] = SortPublisher,
            [ProcessTree.SortPath] = SortPath
        };
        foreach (var (column, icon) in indicators)
        {
            bool active = column == ViewModel.SortColumn;
            icon.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.SortDescending ? "" : "";
        }
    }

    private void Splitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

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

    private void RowList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: ProcessSlot slot } || slot.Data?.Kind == RowKind.Section) return;
        RowList.SelectedItem = slot;

        bool hasPath = !string.IsNullOrWhiteSpace(slot.Path);
        bool blocked = hasPath && App.Monitor.IsBlocked(slot.Path);
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("End task", "", (_, _) => _ = EndSelectedAsync(entireTree: false)));
        menu.Items.Add(MenuItem("End process tree", "", (_, _) => _ = EndSelectedAsync(entireTree: true)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Open file location", "", (_, _) => ProcessActions.OpenFileLocation(slot.Path), hasPath));
        menu.Items.Add(MenuItem("Properties", "", (_, _) => ProcessActions.ShowProperties(slot.Path), hasPath));
        menu.Items.Add(MenuItem("Copy path", "", (_, _) => ProcessActions.CopyText(slot.Path), hasPath));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(blocked ? "Allow network access" : "Block network access", blocked ? "" : "",
            (_, _) => _ = ToggleFirewallAsync(slot.Path), hasPath));
        menu.ShowAt((FrameworkElement)e.OriginalSource, e.GetPosition((FrameworkElement)e.OriginalSource));
        e.Handled = true;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, RoutedEventHandler click, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
        item.Click += click;
        return item;
    }

    private void EndTask_Click(object sender, RoutedEventArgs e) => _ = EndSelectedAsync(entireTree: false);

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } slot) ProcessActions.OpenFileLocation(slot.Path);
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } slot) _ = ToggleFirewallAsync(slot.Path);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => App.Monitor.Paused = PauseButton.IsChecked == true;

    private async Task EndSelectedAsync(bool entireTree)
    {
        if (Selected is not { Data: { } data }) return;
        var processes = data.Processes;
        string name = data.Name;

        if (ProcessActions.AnyCritical(processes))
        {
            bool confirmed = await ConfirmAsync("End a critical system process?",
                $"Windows marks \"{name}\" as critical. Ending it will crash or restart Windows immediately and unsaved work is lost.",
                "End it anyway");
            if (!confirmed) return;
        }
        else if (entireTree)
        {
            bool confirmed = await ConfirmAsync($"End \"{name}\" and everything it started?",
                "Ending the process tree also ends every child process, which can include unrelated apps started from it.",
                "End process tree");
            if (!confirmed) return;
        }

        ProcessActions.EndResult result = await ProcessActions.EndAsync(processes, entireTree);
        App.Monitor.RequestRefresh();
        if (result.Failures.Count > 0)
        {
            string hint = App.Monitor.IsElevated ? "" : "\n\nRestarting as administrator allows ending more processes.";
            await ShowMessageAsync($"Could not end \"{name}\"", string.Join("\n", result.Failures.Take(5)) + hint);
        }
    }

    private async Task ToggleFirewallAsync(string path)
    {
        bool block = !App.Monitor.IsBlocked(path);
        if (block && !await ConfirmAsync("Block network access?",
                $"Adds a Windows Firewall rule that blocks all outbound connections for:\n{path}", "Block"))
        {
            return;
        }

        string? error = await App.Monitor.SetBlockedAsync(path, block);
        if (error is not null) await ShowMessageAsync("Firewall rule not changed", error);
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
            CloseButtonText = "Cancel",
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
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}
