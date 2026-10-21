using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Network as master-detail: apps on the left, the selected app's connections in the middle with its traffic chart
/// underneath, and the details card on the right for whichever row is selected last.
/// </summary>
public sealed partial class NetworkPage : Page
{
    private readonly ColumnReorder columnReorder;
    private readonly Button detailRules, detailFirewall, detailOpen, detailCopy;
    private string? connectionKey;
    private string? detailKey;
    private string? trafficKey;
    private bool restoringSelection, settingSort;
    private HistoryBuffer detailDown = new(MonitorHost.HistoryLength), detailUp = new(MonitorHost.HistoryLength);
    private HistoryBuffer trafficDown = new(MonitorHost.HistoryLength), trafficUp = new(MonitorHost.HistoryLength);

    public NetworkPage()
    {
        ViewModel = new NetworkViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        columnReorder = new ColumnReorder(ColumnHeader, ViewModel.Layout);
        DownloadTile.Caption = Loc.Get("Tile_Download");
        UploadTile.Caption = Loc.Get("Tile_Upload");
        ConnectionsTile.Caption = Loc.Get("Tile_Connections");
        Details.Tile1.Caption = Loc.Get("Tile_Download");
        Details.Tile1.Stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChartReceiveBrush"];
        Details.Tile2.Caption = Loc.Get("Tile_Upload");
        Details.Tile2.Stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChartSendBrush"];
        detailRules = Controls.DetailPane.ActionButton("", Loc.Get("Rules_Menu"), (_, _) => { if (Selected is { Path.Length: > 0 } slot) _ = EditRulesAsync(slot); });
        detailFirewall = Controls.DetailPane.ActionButton("", Loc.Get("Firewall_Block"), Firewall_Click);
        detailOpen = Controls.DetailPane.ActionButton("", Loc.Get("Menu_OpenLocation"), (_, _) => { if (Selected is { Path.Length: > 0 } slot) ProcessActions.OpenFileLocation(slot.Path); });
        detailCopy = Controls.DetailPane.ActionButton("", Loc.Get("Menu_CopyRemoteAddress"), (_, _) => { if (Selected is { } slot) ProcessActions.CopyText(slot.Remote); });
        Details.SetActions([detailRules, detailFirewall, detailOpen, detailCopy]);
        DetailsToggle.IsChecked = App.Settings.ShowDetailsPane;
        Details.Visibility = App.Settings.ShowDetailsPane ? Visibility.Visible : Visibility.Collapsed;
        TrafficTitle.Text = Loc.Get("Network_TrafficTitle");
        SyncSortBox();
        UpdateSortIndicators();
    }

    public NetworkViewModel ViewModel { get; }

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
        SummaryText.Text = ViewModel.Summary;
        UpdateTiles();
        restoringSelection = true;
        try
        {
            // Slots are reused, so both selections are restored by key after every refresh.
            int appIndex = IndexOf(ViewModel.Apps, ViewModel.SelectedAppKey);
            if (appIndex < 0) appIndex = 0;
            if (AppList.SelectedIndex != appIndex) AppList.SelectedIndex = appIndex;
            int index = connectionKey is null ? -1 : IndexOf(ViewModel.Connections, connectionKey);
            if (ConnectionList.SelectedIndex != index) ConnectionList.SelectedIndex = index;
            if (index < 0) connectionKey = null;
        }
        finally
        {
            restoringSelection = false;
        }
        UpdateSelectedApp();
        UpdateCommands();
        UpdateDetails();
    }

    private static int IndexOf(SlotCollection<NetworkSlot, (NetworkRowData, Func<string, NetworkAccess>)> rows, string key)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Key == key) return index;
        }
        return -1;
    }

    /// <summary>The row the toolbar and the details card act on: the connection if one is selected, else the app.</summary>
    private NetworkSlot? Selected => ConnectionList.SelectedItem as NetworkSlot ?? AppList.SelectedItem as NetworkSlot;

    private NetworkSlot? SelectedApp => AppList.SelectedItem as NetworkSlot;

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RowMenu.TrackSelection(e);
        if (restoringSelection) return;
        if (SelectedApp is not { } app) return;
        ViewModel.SelectedAppKey = app.Key;
        connectionKey = null;
        Refresh();
    }

    private void ConnectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RowMenu.TrackSelection(e);
        if (restoringSelection) return;
        connectionKey = (ConnectionList.SelectedItem as NetworkSlot)?.Key;
        UpdateCommands();
        UpdateDetails();
    }

    /// <summary>The strip above the connections: whose connections these are, and their traffic chart below.</summary>
    private void UpdateSelectedApp()
    {
        NetworkSlot? app = SelectedApp;
        SelectedAppIcon.Source = app?.Icon;
        SelectedAppIcon.Visibility = app?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedAppName.Text = app?.Name ?? "";
        SelectedAppDetail.Text = app is null ? "" : Loc.F("Network_ConnectionsShown", Format.Count(ViewModel.Connections.Count)) + (app.Summary.Length > 0 ? " · " + app.Summary : "");
        SelectedAppIcon.Visibility = app?.Key == NetworkViewModel.AllAppsKey ? Visibility.Collapsed : SelectedAppIcon.Visibility;

        string key = ViewModel.SelectedAppKey;
        if (key != trafficKey)
        {
            trafficKey = key;
            trafficDown = new HistoryBuffer(MonitorHost.HistoryLength);
            trafficUp = new HistoryBuffer(MonitorHost.HistoryLength);
        }
        trafficDown.Add(ViewModel.SelectedDown);
        trafficUp.Add(ViewModel.SelectedUp);
        double max = Math.Max(Math.Max(trafficDown.Max(), trafficUp.Max()), 1024);
        TrafficChart.Update(trafficDown, max, trafficUp);
        TrafficDown.Text = Loc.F("Network_TrafficDown", Format.NetworkRate(ViewModel.SelectedDown));
        TrafficUp.Text = Loc.F("Network_TrafficUp", Format.NetworkRate(ViewModel.SelectedUp));
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowDetailsPane = DetailsToggle.IsChecked == true;
        Details.Visibility = App.Settings.ShowDetailsPane ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }

    /// <summary>The card on the right: the selected connection, or the selected app with its traffic history.</summary>
    private void UpdateDetails()
    {
        if (Details.Visibility == Visibility.Collapsed) return;
        if (Selected is not { Data: { } row } slot)
        {
            detailKey = null;
            Details.ShowEmpty(Loc.Get("Details_EmptyNetwork"));
            return;
        }
        if (row.Key != detailKey)
        {
            detailKey = row.Key;
            detailDown = new HistoryBuffer(MonitorHost.HistoryLength);
            detailUp = new HistoryBuffer(MonitorHost.HistoryLength);
        }
        detailDown.Add(row.Down);
        detailUp.Add(row.Up);
        double max = Math.Max(Math.Max(detailDown.Max(), detailUp.Max()), 1024);
        Details.Tile1.Update(Format.NetworkRate(row.Down), "", detailDown, max);
        Details.Tile2.Update(Format.NetworkRate(row.Up), "", detailUp, max);
        bool group = row.Kind == RowKind.Group;
        bool hasPath = slot.Path.Length > 0;
        Details.BeginFacts();
        if (group)
        {
            Details.SetHeader(hasPath ? IconCache.Get(row.Path) : null, row.Name, hasPath ? System.IO.Path.GetFileName(row.Path) : "", row.Count == 1 ? Loc.Get("Common_OneConnection") : Loc.F("Common_Connections", row.Count));
            Details.Fact(Loc.Get("Details_Established"), Format.Count(row.Established));
            Details.Fact(Loc.Get("Details_Listening"), Format.Count(row.Listening));
            if (row.Received + row.Sent > 0) Details.Fact(Loc.Get("Details_Received"), Format.Bytes(row.Received));
            if (row.Received + row.Sent > 0) Details.Fact(Loc.Get("Details_Sent"), Format.Bytes(row.Sent));
            if (hasPath)
            {
                NetworkAccess access = App.Monitor.AccessOf(slot.Path);
                Details.Fact(Loc.Get("Details_Access"), access switch { NetworkAccess.Blocked => Loc.Get("Details_AccessBlocked"), NetworkAccess.Rules => Loc.Get("Details_AccessRules"), _ => Loc.Get("Details_AccessOpen") });
                Details.Fact(Loc.Get("Details_Path"), slot.Path, copyable: true);
            }
        }
        else
        {
            Details.SetHeader(IconCache.Get(row.Path), row.Remote.Length > 0 ? row.Remote : row.Local, row.AppName, row.Name + (row.State.Length > 0 ? " · " + row.State : ""));
            if (row.RemoteAddress.Length > 0) Details.Fact(Loc.Get("Details_Remote"), row.RemoteAddress, copyable: true);
            Details.Fact(Loc.Get("Details_Local"), row.Local);
            Details.Fact(Loc.Get("Details_Protocol"), row.Name);
            if (row.State.Length > 0) Details.Fact(Loc.Get("Details_State"), row.State);
            if (row.Scope.Length > 0) Details.Fact(Loc.Get("Details_Scope"), row.Scope);
            Details.Fact("PID", row.Pid.ToString(System.Globalization.CultureInfo.CurrentCulture));
            if (hasPath) Details.Fact(Loc.Get("Details_Path"), slot.Path, copyable: true);
        }
        Details.EndFacts();
        detailRules.IsEnabled = detailFirewall.IsEnabled = detailOpen.IsEnabled = hasPath;
        detailCopy.Visibility = group ? Visibility.Collapsed : Visibility.Visible;
        Controls.DetailPane.SetActionText(detailFirewall, hasPath && App.Monitor.IsBlocked(slot.Path) ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block"));
    }

    /// <summary>The strip under the title: throughput and connection count with the last minute as sparklines.</summary>
    private void UpdateTiles()
    {
        if (App.Monitor.Latest is not { } snapshot) return;
        SystemSample system = snapshot.System;
        MonitorHost monitor = App.Monitor;
        double max = Math.Max(Math.Max(monitor.ReceiveHistory.Max(), monitor.SendHistory.Max()), 131072);
        DownloadTile.Update(Format.NetworkRate(system.NetworkReceiveBytesPerSecond), Loc.F("Tile_PeakDetail", Format.NetworkRate(monitor.ReceiveHistory.Max())), monitor.ReceiveHistory, max);
        UploadTile.Update(Format.NetworkRate(system.NetworkSendBytesPerSecond), Loc.F("Tile_PeakDetail", Format.NetworkRate(monitor.SendHistory.Max())), monitor.SendHistory, max);
        int established = snapshot.Connections.Count(connection => connection.State == "Established");
        ConnectionsTile.Update(Format.Count(snapshot.Connections.Count), Loc.F("Tile_Established", Format.Count(established), ViewModel.AppCount), monitor.ConnectionHistory, Math.Max(monitor.ConnectionHistory.Max(), 10));
    }

    private void UpdateCommands()
    {
        string path = Selected?.Path ?? "";
        bool hasPath = path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(path);
        FirewallButton.IsEnabled = hasPath;
        RulesButton.IsEnabled = hasPath;
        FirewallButton.Label = blocked ? Loc.Get("Firewall_AllowShort") : Loc.Get("Firewall_BlockShort");
        FirewallIcon.Glyph = blocked ? "" : "";
    }

    // ===== Sorting =====

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (columnReorder.SuppressClick) return;
        if (sender is not FrameworkElement { Tag: string column }) return;
        ViewModel.Sort(column);
        SyncSortBox();
        UpdateSortIndicators();
        Refresh();
    }

    private void AppSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settingSort || AppSortBox.SelectedItem is not ComboBoxItem { Tag: string column }) return;
        ViewModel.SortApps(column);
        UpdateSortIndicators();
        Refresh();
    }

    /// <summary>Shows the app list's current order in the dropdown; a connection sort leaves it blank.</summary>
    private void SyncSortBox()
    {
        settingSort = true;
        AppSortBox.SelectedIndex = ViewModel.SortColumn switch { NetworkViewModel.SortName => 0, NetworkViewModel.SortData => 1, NetworkViewModel.SortSpeed => 2, _ => -1 };
        settingSort = false;
    }

    private void UpdateSortIndicators()
    {
        var indicators = new Dictionary<string, FontIcon>
        {
            [NetworkViewModel.SortRemote] = SortRemote, [NetworkViewModel.SortState] = SortState, [NetworkViewModel.SortSpeed] = SortSpeed
        };
        foreach (var (column, icon) in indicators)
        {
            icon.Visibility = column == ViewModel.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.SortDescending ? "" : "";
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => App.Monitor.Paused = PauseButton.IsChecked == true;

    private void EstablishedOnly_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.EstablishedOnly = EstablishedOnlyButton.IsChecked == true;
        Refresh();
    }

    private void Splitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    // ===== Row menus =====

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, true);

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => RowMenu.Hover(sender, false);

    private void Select(NetworkSlot slot)
    {
        if (slot.Data?.Kind == RowKind.Group) AppList.SelectedItem = slot;
        else ConnectionList.SelectedItem = slot;
    }

    /// <summary>The row's "…" button: selects its row and opens the same menu as a right-click.</summary>
    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NetworkSlot slot }) return;
        Select(slot);
        RowMenu.Show(BuildMenu(slot), sender);
    }

    /// <summary>Right-click, Shift+F10 and the Menu key all arrive here, from either list.</summary>
    private void List_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source) return;
        if ((source.DataContext as NetworkSlot ?? (source as ListViewItem)?.Content as NetworkSlot) is not { } slot) return;
        Select(slot);
        MenuFlyout menu = BuildMenu(slot);
        if (e.TryGetPosition(source, out Windows.Foundation.Point point)) menu.ShowAt(source, point);
        else menu.ShowAt(source);
        e.Handled = true;
    }

    private MenuFlyout BuildMenu(NetworkSlot slot)
    {
        bool hasPath = slot.Path.Length > 0;
        bool blocked = hasPath && App.Monitor.IsBlocked(slot.Path);

        var menu = new MenuFlyout();
        var firewall = new MenuFlyoutItem
        {
            Text = blocked ? Loc.Get("Firewall_Allow") : Loc.Get("Firewall_Block"),
            Icon = new FontIcon { Glyph = blocked ? "" : "" },
            IsEnabled = hasPath
        };
        firewall.Click += (_, _) => _ = ToggleFirewallAsync(slot.Path);
        menu.Items.Add(firewall);
        var rules = new MenuFlyoutItem { Text = Loc.Get("Rules_Menu"), Icon = new FontIcon { Glyph = "" }, IsEnabled = hasPath };
        rules.Click += (_, _) => _ = EditRulesAsync(slot);
        menu.Items.Add(rules);
        var location = new MenuFlyoutItem { Text = Loc.Get("Menu_OpenLocation"), Icon = new FontIcon { Glyph = "" }, IsEnabled = hasPath };
        location.Click += (_, _) => ProcessActions.OpenFileLocation(slot.Path);
        menu.Items.Add(location);
        if (slot.Data?.Kind == RowKind.Child)
        {
            var copy = new MenuFlyoutItem { Text = Loc.Get("Menu_CopyRemoteAddress"), Icon = new FontIcon { Glyph = "" } };
            copy.Click += (_, _) => ProcessActions.CopyText(slot.Remote);
            menu.Items.Add(copy);
        }
        return menu;
    }

    // ===== Actions =====

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Path.Length: > 0 } slot) _ = ToggleFirewallAsync(slot.Path);
    }

    private void Rules_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Path.Length: > 0 } slot) _ = EditRulesAsync(slot);
    }

    private async Task EditRulesAsync(NetworkSlot slot)
    {
        // Connection rows carry their app's path; the dialog is titled with the app, not the connection.
        string name = slot.Data?.Kind == RowKind.Group ? slot.Name : (slot.Data?.AppName is { Length: > 0 } app ? app : System.IO.Path.GetFileName(slot.Path));
        await AppRulesDialog.ShowAsync(XamlRoot, slot.Path, name);
        UpdateCommands();
    }

    private async Task ToggleFirewallAsync(string path)
    {
        bool block = !App.Monitor.IsBlocked(path);
        if (block)
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get("Firewall_ConfirmTitle"),
                Content = new TextBlock { Text = Loc.F("Firewall_ConfirmText", path), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Loc.Get("Firewall_BlockButton"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        }

        string? error = await App.Monitor.SetBlockedAsync(path, block);
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = Loc.Get("Firewall_NotChanged"), Content = error, CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
        }
        UpdateCommands();
    }
}
