using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.Views;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Reflection;
using Windows.Graphics;
using Windows.System;

namespace BetterTaskManager.Fluent;

public sealed partial class MainWindow : Window
{
    private OverviewPage? overview;
    private MemoryPage? memoryPage;
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Overview"] = typeof(OverviewPage),
        ["Processes"] = typeof(ProcessesPage),
        ["Performance"] = typeof(PerformancePage),
        ["Memory"] = typeof(MemoryPage),
        ["Network"] = typeof(NetworkPage),
        ["Disk"] = typeof(DiskPage),
        ["History"] = typeof(HistoryPage),
        ["Startup"] = typeof(StartupPage),
        ["Settings"] = typeof(SettingsPage)
    };

    public MainWindow()
    {
        InitializeComponent();
        Navigation.DisplayModeChanged += (_, _) => UpdateNavigationChrome();
        Navigation.Loaded += (_, _) => UpdateNavigationChrome();
        Navigation.PaneOpened += (_, _) => UpdateNavigationChrome();
        Navigation.PaneClosed += (_, _) => UpdateNavigationChrome();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(Math.Max(1024, App.Settings.WindowWidth), Math.Max(700, App.Settings.WindowHeight)));
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange || args.DidSizeChange)
            {
                App.Monitor.ViewSuspended = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
            }
            if (args.DidSizeChange && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            {
                App.Settings.WindowWidth = AppWindow.Size.Width;
                App.Settings.WindowHeight = AppWindow.Size.Height;
            }
        };

        // Limited mode lives in the pane footer card; the old bar across the top stays closed.
        ElevationBar.IsOpen = false;
        LimitedCard.Visibility = App.Monitor.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        LimitedText.Text = ElevationBar.Message;
        VersionText.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
        MachineText.Text = Environment.MachineName;
        MachineStats.Text = Loc.Get("Design_Collecting");
        LiveText.Text = Loc.Get("Status_Waiting");
        SourceText.Text = Loc.Get("Status_SourceUnknown");
        // "Replace Windows Task Manager" was on and the redirect is gone: say so instead of silently opening Windows'.
        TaskManagerSwitch.RememberExistingChoice();
        ReplaceLostBar.IsOpen = TaskManagerSwitch.WasRemoved;
        ReplaceLostBar.Closed += (_, args) =>
        {
            if (args.Reason == InfoBarCloseReason.CloseButton) App.Settings.ReplaceTaskManagerWanted = false;
        };
        // Remember when the user closes the notice, so it does not reappear on every launch.
        ElevationBar.Closed += (_, args) =>
        {
            if (args.Reason == InfoBarCloseReason.CloseButton) App.Settings.HideElevationNotice = true;
        };
        string limitedMessage = ElevationBar.Message;
        // A failed collection leaves the last numbers on screen: say so until a new snapshot arrives.
        App.Monitor.CollectionFailed += message =>
        {
            CollectionBar.Message = Loc.F("Collection_Failed", message);
            CollectionBar.IsOpen = true;
        };
        App.Monitor.Updated += snapshot =>
        {
            CollectionBar.IsOpen = false;
            ElevationBar.Message = snapshot.System.PerProcessNetworkFromService
                ? Loc.Get("Limited_FeedMessage")
                : limitedMessage;
            LimitedText.Text = ElevationBar.Message;
            UpdateStatusStrip(snapshot);
        };
        ApplyTheme(App.Settings.Theme);
        WindowRoot.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        UpdateCaptionButtons();

        WindowRoot.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        AddAccelerator(VirtualKey.Number0, () => Navigate("Overview"));
        AddAccelerator(VirtualKey.Number1, () => Navigate("Processes"));
        AddAccelerator(VirtualKey.Number2, () => Navigate("Performance"));
        AddAccelerator(VirtualKey.Number3, () => Navigate("Network"));
        AddAccelerator(VirtualKey.Number4, () => Navigate("Disk"));
        AddAccelerator(VirtualKey.Number5, () => Navigate("History"));
        AddAccelerator(VirtualKey.Number6, () => Navigate("Startup"));
        AddAccelerator(VirtualKey.Number7, () => Navigate("Memory"));
        AddAccelerator(VirtualKey.F, () => SearchBox.Focus(FocusState.Keyboard));
        AddAccelerator(VirtualKey.K, () => SearchBox.Focus(FocusState.Keyboard));

        string[] args = Environment.GetCommandLineArgs();
        int pageIndex = Array.IndexOf(args, "--page");
        string startPage = pageIndex >= 0 && pageIndex + 1 < args.Length ? args[pageIndex + 1] : "Overview";
        // The settings entry only exists once the NavigationView template has loaded.
        if (startPage == "Settings") Navigation.Loaded += (_, _) => Navigation.SelectedItem = Navigation.SettingsItem;
        else
        {
            Navigation.SelectedItem = Navigation.MenuItems[0];
            Navigate(startPage);
        }
    }

    /// <summary>The strip at the bottom: whether the numbers are live and which process measures them.</summary>
    private void UpdateStatusStrip(Core.Monitoring.MonitorSnapshot snapshot)
    {
        MachineStats.Text = Loc.F("Design_MachineStats", Format.WholePercent(snapshot.System.CpuPercent), Format.WholePercent(snapshot.System.Memory.LoadPercent));
        bool paused = App.Monitor.Paused;
        LiveDot.Fill = (Brush)Application.Current.Resources[paused ? "StateWarnBrush" : "StateGoodBrush"];
        LiveText.Text = paused ? Loc.Get("Status_Paused") : Loc.F("Status_Live", snapshot.Timestamp.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture));
        SourceText.Text = snapshot.System.PerProcessNetworkFromService ? Loc.Get("Status_SourceService")
            : App.Monitor.IsElevated ? Loc.Get("Status_SourceOwn")
            : Loc.Get("Status_SourceNone");
    }

    public void ApplyTheme(string theme)
    {
        WindowRoot.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        bool dark = WindowRoot.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(24, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
    }

    public void NavigatePage(string tag) => Navigate(tag);

    private void UpdateNavigationChrome()
    {
        // Footer cards need the expanded pane; in the icon rail they would wrap into very tall strips.
        bool expanded = Navigation.DisplayMode == NavigationViewDisplayMode.Expanded && Navigation.IsPaneOpen;
        BrandPanel.Visibility = PaneFooterContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetSearch(string text) => SearchBox.Text = text;

    public void ShowProcess(BetterTaskManager.Core.Monitoring.ProcessSample process)
    {
        SetSearch("");
        Navigate("Processes");
        if (ContentFrame.Content is ProcessesPage page) page.ShowProcess(process);
    }

    public void ShowAppNetwork(BetterTaskManager.Core.Monitoring.ProcessSample process)
    {
        SetSearch("");
        Navigate("Network");
        if (ContentFrame.Content is NetworkPage page) page.ShowApp(process);
    }

    private void Navigate(string tag)
    {
        foreach (object item in Navigation.MenuItems)
        {
            if (item is NavigationViewItem navigationItem && Equals(navigationItem.Tag, tag))
            {
                Navigation.SelectedItem = navigationItem;
                return;
            }
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected ? "Settings" : (args.SelectedItemContainer?.Tag as string ?? "Overview");
        if (Pages.TryGetValue(tag, out Type? page) && ContentFrame.Content?.GetType() != page)
        {
            try
            {
                if (tag == "Overview") ContentFrame.Content = overview ??= new OverviewPage();
                else if (tag == "Memory") ContentFrame.Content = memoryPage ??= new MemoryPage();
                else ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                throw;
            }
        }

        SearchBox.IsEnabled = tag is "Processes" or "Network" or "Disk" or "History" or "Startup";
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
        UpdateNavigationChrome();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        App.Monitor.SearchText = sender.Text;

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => App.RestartElevated();

    private async void RestoreReplace_Click(object sender, RoutedEventArgs e)
    {
        RestoreReplaceButton.IsEnabled = false;
        string? error = await TaskManagerSwitch.SetAsync(true);
        RestoreReplaceButton.IsEnabled = true;
        if (error is null) ReplaceLostBar.IsOpen = false;
        else ReplaceLostBar.Message = error;
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        WindowRoot.KeyboardAccelerators.Add(accelerator);
    }
}
