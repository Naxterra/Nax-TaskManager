using System.Diagnostics;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Startup;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class StartupPage : Page
{
    private string? selectedKey;

    public StartupPage()
    {
        ViewModel = new StartupViewModel(App.Settings);
        InitializeComponent();
        columnReorder = new ColumnReorder(ColumnHeader, ViewModel.Layout);
    }

    private readonly ColumnReorder columnReorder;

    public StartupViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.SearchChanged += OnSearchChanged;
        _ = ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => App.Monitor.SearchChanged -= OnSearchChanged;

    private void OnSearchChanged()
    {
        ViewModel.Apply(App.Monitor.SearchText);
        RestoreSelection();
    }

    private async Task ReloadAsync()
    {
        await ViewModel.LoadAsync(App.Monitor.SearchText);
        RestoreSelection();
    }

    private void RestoreSelection()
    {
        RowList.SelectedItem = ViewModel.Rows.FirstOrDefault(row => row.App.Key == selectedKey);
        UpdateCommands();
    }

    private StartupApp? Selected => (RowList.SelectedItem as StartupRow)?.App;

    private void RowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowList.SelectedItem is StartupRow row) selectedKey = row.App.Key;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        StartupApp? app = Selected;
        ToggleButton.IsEnabled = app is { Locked: false };
        ToggleButton.Label = app is { Enabled: false } ? Loc.Get("Startup_Enable") : Loc.Get("Startup_Disable");
        ToggleIcon.Glyph = app is { Enabled: false } ? "" : "";
        bool hasPath = app is not null && app.ExecutablePath.Length > 0;
        OpenLocationButton.IsEnabled = PropertiesButton.IsEnabled = hasPath;
        CopyCommandButton.IsEnabled = app is not null;
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (columnReorder.SuppressClick) return;
        if (sender is not FrameworkElement { Tag: string column }) return;
        ViewModel.Sort(column, App.Monitor.SearchText);
        var indicators = new Dictionary<string, FontIcon>
        {
            [StartupViewModel.SortName] = SortName, [StartupViewModel.SortPublisher] = SortPublisher,
            [StartupViewModel.SortStatus] = SortStatus, [StartupViewModel.SortScope] = SortScope
        };
        foreach (var (key, icon) in indicators)
        {
            icon.Visibility = key == ViewModel.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.SortDescending ? "" : "";
        }
        RestoreSelection();
    }

    private void Splitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.Layout.Resize(column, e.HorizontalChange);
    }

    private void RowList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Selected is { Locked: false } app) _ = SetEnabledAsync(app, !app.Enabled);
    }

    private void RowList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var source = e.OriginalSource as FrameworkElement;
        StartupRow? row = source?.DataContext as StartupRow ?? (source as ListViewItem)?.Content as StartupRow ?? RowList.SelectedItem as StartupRow;
        if (source is null || row is null) return;
        RowList.SelectedItem = row;
        StartupApp app = row.App;
        bool hasPath = app.ExecutablePath.Length > 0;

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(app.Enabled ? Loc.Get("Startup_Disable") : Loc.Get("Startup_Enable"), app.Enabled ? "" : "",
            (_, _) => _ = SetEnabledAsync(app, !app.Enabled), !app.Locked));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Menu_OpenLocation"), "", (_, _) => ProcessActions.OpenFileLocation(app.ExecutablePath), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Proc_Properties"), "", (_, _) => ProcessActions.ShowProperties(app.ExecutablePath), hasPath));
        menu.Items.Add(MenuItem(Loc.Get("Startup_CopyCommand"), "", (_, _) => ProcessActions.CopyText(app.Command)));
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

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } app) _ = SetEnabledAsync(app, !app.Enabled);
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } app) ProcessActions.OpenFileLocation(app.ExecutablePath);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } app) ProcessActions.ShowProperties(app.ExecutablePath);
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } app) ProcessActions.CopyText(app.Command);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    /// <summary>
    /// Current-user entries change directly. All-users entries need administrator rights: without them a short-lived
    /// elevated copy of the app makes the change, so Windows shows one UAC prompt.
    /// </summary>
    private async Task SetEnabledAsync(StartupApp app, bool enabled)
    {
        string? error = null;
        if (!app.AllUsers || FirewallRules.IsElevated)
        {
            try
            {
                await Task.Run(() => StartupApps.SetEnabled(app, enabled));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                error = ex.Message;
            }
        }
        else
        {
            var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            startInfo.ArgumentList.Add(enabled ? Program.StartupEnableArgument : Program.StartupDisableArgument);
            startInfo.ArgumentList.Add(app.Source.ToString());
            startInfo.ArgumentList.Add(app.Name);
            try
            {
                using Process helper = Process.Start(startInfo)!;
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) error = Loc.F("Common_ExitCode", helper.ExitCode);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                error = Loc.Get("Uac_CancelledNothing");
            }
        }
        await ReloadAsync();
        if (error is not null) await Dialogs.ShowAsync(XamlRoot, Loc.F("Startup_NotChanged", app.DisplayName), error);
    }
}
