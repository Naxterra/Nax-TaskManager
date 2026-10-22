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
        ViewModel = new HistoryViewModel(App.Settings);
        InitializeComponent();
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        logReorder = new ColumnReorder(LogHeader, ViewModel.Layout);
        UpdateSortIndicators();
        timer.Tick += (_, _) => _ = LoadAsync();
    }

    private readonly ColumnReorder logReorder;

    public HistoryViewModel ViewModel { get; }

    private void AppHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string column }) return;
        ViewModel.SortApps(column);
        UpdateSortIndicators();
        RestoreAppSelection();
    }

    private void LogHeader_Click(object sender, RoutedEventArgs e)
    {
        if (logReorder.SuppressClick || sender is not FrameworkElement { Tag: string column }) return;
        bool reload = ViewModel.SortLog(column);
        UpdateSortIndicators();
        if (reload) _ = LoadAsync();
    }

    private void LogSplitter_DragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    private void UpdateSortIndicators()
    {
        var apps = new Dictionary<string, FontIcon> { [HistoryViewModel.SortName] = AppSortName, [HistoryViewModel.SortData] = AppSortData };
        foreach (var (column, icon) in apps)
        {
            icon.Visibility = column == ViewModel.AppSortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.AppSortDescending ? "\uE70D" : "\uE70E";
        }
        var log = new Dictionary<string, FontIcon>
        {
            [HistoryViewModel.SortTime] = LogSortTime, [HistoryViewModel.SortApp] = LogSortApp, [HistoryViewModel.SortRemote] = LogSortRemote,
            [HistoryViewModel.SortProtocol] = LogSortProtocol, [HistoryViewModel.SortData] = LogSortData
        };
        foreach (var (column, icon) in log)
        {
            icon.Visibility = column == ViewModel.LogSortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.LogSortDescending ? "\uE70D" : "\uE70E";
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        App.Monitor.SearchChanged -= OnSearchChanged;
        App.Monitor.SearchChanged += OnSearchChanged;
        timer.Start();
        _ = LoadAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
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
        EmptyText.Text = !ViewModel.HasData ? Loc.Get("History_EmptyNothing")
            : App.Monitor.SearchText.Trim().Length > 0 ? Loc.Get("History_EmptySearch")
            : Loc.Get("History_EmptyPeriod");
        RestoreAppSelection();
        UpdateFilterBar();
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
            case HistoryServiceState.Running when HistoryServiceControl.NeedsUpdate(HistoryServiceSetup.BundledFolder):
                ServiceBar.IsOpen = true;
                ServiceBar.Severity = InfoBarSeverity.Informational;
                ServiceBar.Title = Loc.Get("Service_UpdateTitle");
                string? installed = HistoryServiceControl.VersionIn(HistoryServiceControl.InstallFolder);
                string? bundled = HistoryServiceControl.VersionIn(HistoryServiceSetup.BundledFolder);
                // Same version number from another commit (development builds): show the commit too.
                bool sameNumber = Short(installed) == Short(bundled);
                ServiceBar.Message = Loc.F("Service_UpdateMessage", sameNumber ? WithCommit(installed) : Short(installed), sameNumber ? WithCommit(bundled) : Short(bundled));
                ServiceButton.Content = Loc.Get("Service_Update");
                break;
            case HistoryServiceState.Running:
                ServiceBar.IsOpen = false;
                break;
            case HistoryServiceState.NotInstalled:
                ServiceBar.IsOpen = true;
                ServiceBar.Severity = InfoBarSeverity.Informational;
                ServiceBar.Title = Loc.Get("History_Off");
                ServiceBar.Message = Loc.Get("Service_OffMessage");
                ServiceButton.Content = Loc.Get("Service_TurnOn");
                break;
            default:
                ServiceBar.IsOpen = true;
                ServiceBar.Severity = InfoBarSeverity.Warning;
                ServiceBar.Title = Loc.Get("History_ServiceStopped");
                ServiceBar.Message = Loc.Get("Service_StoppedMessage");
                ServiceButton.Content = Loc.Get("Service_Start");
                break;
        }
    }

    private static string Short(string? version) => version?.Split('+')[0] ?? Loc.Get("Common_Unknown");

    /// <summary>"2.0.0-alpha.6 (fa7c9e9)" from "2.0.0-alpha.6+fa7c9e9…".</summary>
    private static string WithCommit(string? version) =>
        version?.Split('+') is [string number, string commit] ? $"{number} ({commit[..Math.Min(7, commit.Length)]})" : Short(version);

    private async void ServiceButton_Click(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateServiceBar();
        string? error = await HistoryServiceSetup.SetEnabledAsync(enable: true, App.Monitor.IsElevated);
        busy = false;
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = Loc.Get("Service_NotTurnedOn"), Content = error, CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
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

    private void DnsToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IncludeDns = DnsToggle.IsChecked == true;
        _ = LoadAsync();
    }

    /// <summary>A selected app filters the connection list; say so plainly, with the way back.</summary>
    private void UpdateFilterBar()
    {
        string? name = ViewModel.AppKey is { } key ? ViewModel.Apps.FirstOrDefault(app => app.Key == key)?.Name : null;
        FilterBar.Visibility = name is null ? Visibility.Collapsed : Visibility.Visible;
        if (name is not null) FilterText.Text = Loc.F("History_FilteredTo", name);
    }

    private void ShowAllApps_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AppKey = null;
        _ = LoadAsync();
    }

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection || AppList.SelectedItem is not AppUsageSlot slot) return;
        string? key = slot.Key.Length == 0 ? null : slot.Key;
        if (key == ViewModel.AppKey) return;
        ViewModel.AppKey = key;
        _ = LoadAsync();
    }

    private void ConnectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RowMenu.TrackSelection(e);

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, true);

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, false);

    /// <summary>The row's "…" button: selects its row and opens the same menu as a right-click.</summary>
    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ConnectionLogSlot slot }) return;
        ConnectionList.SelectedItem = slot;
        RowMenu.Show(BuildMenu(slot), sender);
    }

    /// <summary>Right-click, Shift+F10 and the Menu key all arrive here.</summary>
    private void ConnectionList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source) return;
        if ((source.DataContext as ConnectionLogSlot ?? (source as ListViewItem)?.Content as ConnectionLogSlot) is not { } slot) return;
        ConnectionList.SelectedItem = slot;
        MenuFlyout menu = BuildMenu(slot);
        if (e.TryGetPosition(source, out Windows.Foundation.Point point)) menu.ShowAt(source, point);
        else menu.ShowAt(source);
        e.Handled = true;
    }

    private static MenuFlyout BuildMenu(ConnectionLogSlot slot)
    {
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = Loc.Get("Menu_CopyRemoteHost"), Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) => ProcessActions.CopyText(slot.Remote);
        menu.Items.Add(copy);
        var location = new MenuFlyoutItem { Text = Loc.Get("Menu_OpenLocation"), Icon = new FontIcon { Glyph = "\uE838" }, IsEnabled = slot.Path.Length > 0 };
        location.Click += (_, _) => ProcessActions.OpenFileLocation(slot.Path);
        menu.Items.Add(location);
        return menu;
    }

    private void Blocked_Click(object sender, RoutedEventArgs e) => App.Window.ShowBlockedLog();
}
