using BetterTaskManager.Core.History;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class HistoryPage : Page
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private readonly DispatcherTimer timer = new() { Interval = RefreshInterval };
    private bool loading, reloadPending, restoringSelection, busy;

    public HistoryPage()
    {
        ViewModel = new HistoryViewModel();
        InitializeComponent();
        timer.Tick += (_, _) => _ = LoadAsync();
    }

    public HistoryViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.SearchChanged += OnSearchChanged;
        timer.Start();
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Monitor.SearchChanged -= OnSearchChanged;
        timer.Stop();
    }

    private void OnSearchChanged() => _ = LoadAsync();

    /// <summary>Reloads from the database; a request that arrives during a load runs once more afterwards.</summary>
    private async Task LoadAsync()
    {
        if (loading)
        {
            reloadPending = true;
            return;
        }
        loading = true;
        try
        {
            do
            {
                reloadPending = false;
                await ViewModel.LoadAsync(App.Monitor.SearchText);
            }
            while (reloadPending);
        }
        finally
        {
            loading = false;
        }

        SummaryText.Text = ViewModel.Summary;
        ConnectionSummaryText.Text = ViewModel.ConnectionSummary;
        bool empty = ViewModel.Connections.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = !ViewModel.HasData ? "Nothing has been recorded yet."
            : App.Monitor.SearchText.Trim().Length > 0 ? "No connections match the search."
            : "No connections in this period.";
        RestoreAppSelection();
        UpdateServiceBar();
    }

    private void RestoreAppSelection()
    {
        restoringSelection = true;
        try
        {
            string key = ViewModel.AppKey ?? "";
            int index = 0;
            for (int i = 0; i < ViewModel.Apps.Count; i++)
            {
                if (ViewModel.Apps[i].Key == key)
                {
                    index = i;
                    break;
                }
            }
            if (AppList.SelectedIndex != index) AppList.SelectedIndex = index;
        }
        finally
        {
            restoringSelection = false;
        }
    }

    private void UpdateServiceBar()
    {
        ServiceButton.IsEnabled = !busy;
        switch (ViewModel.ServiceState)
        {
            case HistoryServiceState.Running:
                ServiceBar.IsOpen = false;
                break;
            case HistoryServiceState.NotInstalled:
                ServiceBar.IsOpen = true;
                ServiceBar.Severity = InfoBarSeverity.Informational;
                ServiceBar.Title = "Background recording is off";
                ServiceBar.Message = "A small Windows service can record which apps connect where and how much data they use, even while this window is closed. History is kept for 30 days.";
                ServiceButton.Content = "Turn on";
                break;
            default:
                ServiceBar.IsOpen = true;
                ServiceBar.Severity = InfoBarSeverity.Warning;
                ServiceBar.Title = "The history service is not running";
                ServiceBar.Message = "Nothing is recorded until it runs again.";
                ServiceButton.Content = "Start";
                break;
        }
    }

    private async void ServiceButton_Click(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateServiceBar();
        string? error = await HistoryServiceSetup.SetEnabledAsync(enable: true, App.Monitor.IsElevated);
        busy = false;
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Background recording not turned on", Content = error, CloseButtonText = "OK" }.ShowAsync();
        }
        await LoadAsync();
    }

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.Range = (HistoryRange)Math.Max(0, RangeBox.SelectedIndex);
        _ = LoadAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection || AppList.SelectedItem is not AppUsageSlot slot) return;
        string? key = slot.Key.Length == 0 ? null : slot.Key;
        if (key == ViewModel.AppKey) return;
        ViewModel.AppKey = key;
        _ = LoadAsync();
    }

    /// <summary>Right-click, Shift+F10 and the Menu key all arrive here.</summary>
    private void ConnectionList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source) return;
        if ((source.DataContext as ConnectionLogSlot ?? (source as ListViewItem)?.Content as ConnectionLogSlot) is not { } slot) return;
        ConnectionList.SelectedItem = slot;
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy remote host", Icon = new FontIcon { Glyph = "" } };
        copy.Click += (_, _) => ProcessActions.CopyText(slot.Remote);
        menu.Items.Add(copy);
        var location = new MenuFlyoutItem { Text = "Open file location", Icon = new FontIcon { Glyph = "" }, IsEnabled = slot.Path.Length > 0 };
        location.Click += (_, _) => ProcessActions.OpenFileLocation(slot.Path);
        menu.Items.Add(location);
        if (e.TryGetPosition(source, out Windows.Foundation.Point point)) menu.ShowAt(source, point);
        else menu.ShowAt(source);
        e.Handled = true;
    }
}
