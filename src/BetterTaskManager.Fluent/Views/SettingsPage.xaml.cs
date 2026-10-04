using System.Diagnostics;
using System.Reflection;
using BetterTaskManager.Core.History;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class SettingsPage : Page
{
    private bool loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Select(IntervalBox, App.Settings.RefreshIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
        Select(ThemeBox, App.Settings.Theme, 0);

        bool elevated = App.Monitor.IsElevated;
        ElevationTitle.Text = elevated ? "Running as administrator" : "Running as standard user";
        ElevationText.Text = elevated
            ? "All processes show their path, publisher and icon, per-app network speed is measured, and firewall changes need no extra prompt."
            : "Per-app network speed is unavailable, system processes hide their path, publisher and icon, and each firewall change asks for administrator approval.";
        ElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;

        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = "Nax-TaskManager " + version.Split('+')[0];
        ShowHistoryState();
        loading = false;
    }

    private void ShowHistoryState()
    {
        HistoryServiceState state = HistoryServiceControl.QueryState();
        HistoryToggle.IsOn = state != HistoryServiceState.NotInstalled;
        HistoryStatusText.Text = state switch
        {
            HistoryServiceState.Running when HistoryServiceControl.NeedsUpdate(HistoryServiceSetup.BundledFolder) =>
                "On, but the service is older than this app. Use Update on the History page.",
            HistoryServiceState.Running => "On. Recording while Windows runs, even with this window closed.",
            HistoryServiceState.NotInstalled => App.Monitor.IsElevated ? "Off." : "Off. Turning it on asks for administrator approval once.",
            HistoryServiceState.Starting => "Starting…",
            _ => "Installed, but the service is not running."
        };
    }

    private async void HistoryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool enable = HistoryToggle.IsOn;
        HistoryToggle.IsEnabled = false;
        HistoryStatusText.Text = enable ? "Turning on…" : "Turning off…";
        string? error = await HistoryServiceSetup.SetEnabledAsync(enable, App.Monitor.IsElevated);
        HistoryToggle.IsEnabled = true;
        loading = true;
        ShowHistoryState();
        loading = false;
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = enable ? "Background recording not turned on" : "Background recording not turned off", Content = error, CloseButtonText = "OK" }.ShowAsync();
        }
    }

    private static void Select(ComboBox box, string tag, int fallback)
    {
        box.SelectedIndex = fallback;
        for (int index = 0; index < box.Items.Count; index++)
        {
            if (box.Items[index] is ComboBoxItem { Tag: string itemTag } && itemTag == tag) box.SelectedIndex = index;
        }
    }

    private void IntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || IntervalBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        App.Monitor.Interval = TimeSpan.FromMilliseconds(int.Parse(tag, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        App.Settings.Theme = tag;
        App.Window.ApplyTheme(tag);
    }

    private void Elevate_Click(object sender, RoutedEventArgs e) => App.RestartElevated();

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaxTaskManager");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
}
