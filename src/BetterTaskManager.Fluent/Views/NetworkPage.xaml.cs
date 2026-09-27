using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class NetworkPage : Page
{
    private string? selectedKey;
    private bool restoringSelection;

    public NetworkPage()
    {
        ViewModel = new NetworkViewModel(App.Monitor, App.Settings);
        InitializeComponent();
    }

    public NetworkViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.Updated += OnUpdated;
        App.Monitor.SearchChanged += Refresh;
        App.Monitor.FirewallChanged += Refresh;
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
        SummaryText.Text = ViewModel.Summary;
        restoringSelection = true;
        try
        {
            int index = -1;
            for (int i = 0; selectedKey is not null && i < ViewModel.Rows.Count; i++)
            {
                if (ViewModel.Rows[i].Key == selectedKey)
                {
                    index = i;
                    break;
                }
            }
            if (RowList.SelectedIndex != index) RowList.SelectedIndex = index;
            if (index < 0) selectedKey = null;
        }
        finally
        {
            restoringSelection = false;
        }
        UpdateCommands();
    }

    private NetworkSlot? Selected => RowList.SelectedItem as NetworkSlot;

    private void RowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection) return;
        selectedKey = Selected?.Key;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        string path = Selected?.Path ?? "";
        bool hasPath = path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(path);
        FirewallButton.IsEnabled = hasPath;
        FirewallButton.Label = blocked ? "Allow network" : "Block network";
        FirewallIcon.Glyph = blocked ? "" : "";
    }

    private void EstablishedOnly_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.EstablishedOnly = EstablishedOnlyButton.IsChecked == true;
        Refresh();
    }

    private void Splitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkSlot slot }) ViewModel.ToggleExpanded(slot.Key);
        Refresh();
    }

    private void RowList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: NetworkSlot { Data.Kind: RowKind.Group } slot }) ViewModel.ToggleExpanded(slot.Key);
        Refresh();
    }

    private void RowList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: NetworkSlot slot } source) return;
        RowList.SelectedItem = slot;
        bool hasPath = slot.Path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(slot.Path);

        var menu = new MenuFlyout();
        var firewall = new MenuFlyoutItem
        {
            Text = blocked ? "Allow network access" : "Block network access",
            Icon = new FontIcon { Glyph = blocked ? "" : "" },
            IsEnabled = hasPath
        };
        firewall.Click += (_, _) => _ = ToggleFirewallAsync(slot.Path);
        menu.Items.Add(firewall);
        var location = new MenuFlyoutItem { Text = "Open file location", Icon = new FontIcon { Glyph = "" }, IsEnabled = hasPath };
        location.Click += (_, _) => ProcessActions.OpenFileLocation(slot.Path);
        menu.Items.Add(location);
        if (slot.Data?.Kind == RowKind.Child)
        {
            var copy = new MenuFlyoutItem { Text = "Copy remote address", Icon = new FontIcon { Glyph = "" } };
            copy.Click += (_, _) => ProcessActions.CopyText(slot.Remote);
            menu.Items.Add(copy);
        }
        menu.ShowAt(source, e.GetPosition(source));
        e.Handled = true;
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Path.Length: > 0 } slot) _ = ToggleFirewallAsync(slot.Path);
    }

    private async Task ToggleFirewallAsync(string path)
    {
        bool block = !App.Monitor.IsBlocked(path);
        if (block)
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Block network access?",
                Content = new TextBlock { Text = $"Adds a Windows Firewall rule that blocks all outbound connections for:\n{path}", TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Block",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        }

        string? error = await App.Monitor.SetBlockedAsync(path, block);
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Firewall rule not changed", Content = error, CloseButtonText = "OK" }.ShowAsync();
        }
        UpdateCommands();
    }
}
