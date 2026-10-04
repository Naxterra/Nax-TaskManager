using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.Views;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.System;

namespace BetterTaskManager.Fluent;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Processes"] = typeof(ProcessesPage),
        ["Performance"] = typeof(PerformancePage),
        ["Network"] = typeof(NetworkPage),
        ["History"] = typeof(HistoryPage),
        ["Details"] = typeof(DetailsPage),
        ["Startup"] = typeof(StartupPage),
        ["Settings"] = typeof(SettingsPage)
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(Math.Max(900, App.Settings.WindowWidth), Math.Max(600, App.Settings.WindowHeight)));
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

        ElevationBar.IsOpen = !App.Monitor.IsElevated && !App.Settings.HideElevationNotice;
        // Remember when the user closes the notice, so it does not reappear on every launch.
        ElevationBar.Closed += (_, args) =>
        {
            if (args.Reason == InfoBarCloseReason.CloseButton) App.Settings.HideElevationNotice = true;
        };
        string limitedMessage = ElevationBar.Message;
        App.Monitor.Updated += snapshot =>
        {
            ElevationBar.Message = snapshot.System.PerProcessNetworkFromService
                ? Loc.Get("Limited_FeedMessage")
                : limitedMessage;
        };
        ApplyTheme(App.Settings.Theme);
        WindowRoot.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        UpdateCaptionButtons();

        WindowRoot.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        AddAccelerator(VirtualKey.Number1, () => Navigate("Processes"));
        AddAccelerator(VirtualKey.Number2, () => Navigate("Performance"));
        AddAccelerator(VirtualKey.Number3, () => Navigate("Network"));
        AddAccelerator(VirtualKey.Number4, () => Navigate("History"));
        AddAccelerator(VirtualKey.Number5, () => Navigate("Details"));
        AddAccelerator(VirtualKey.Number6, () => Navigate("Startup"));
        AddAccelerator(VirtualKey.F, () => SearchBox.Focus(FocusState.Keyboard));

        string[] args = Environment.GetCommandLineArgs();
        int pageIndex = Array.IndexOf(args, "--page");
        string startPage = pageIndex >= 0 && pageIndex + 1 < args.Length ? args[pageIndex + 1] : "Processes";
        // The settings entry only exists once the NavigationView template has loaded.
        if (startPage == "Settings") Navigation.Loaded += (_, _) => Navigation.SelectedItem = Navigation.SettingsItem;
        else
        {
            Navigation.SelectedItem = Navigation.MenuItems[0];
            Navigate(startPage);
        }
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
        string tag = args.IsSettingsSelected ? "Settings" : (args.SelectedItemContainer?.Tag as string ?? "Processes");
        if (Pages.TryGetValue(tag, out Type? page) && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new SuppressNavigationTransitionInfo());
        }
        SearchBox.IsEnabled = tag is "Processes" or "Network" or "History" or "Details" or "Startup";
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        App.Monitor.SearchText = sender.Text;

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => App.RestartElevated();

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
