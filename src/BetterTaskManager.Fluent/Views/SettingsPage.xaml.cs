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
        Select(LanguageBox, App.Settings.Language, 0);

        bool elevated = App.Monitor.IsElevated;
        ElevationTitle.Text = elevated ? Loc.Get("Settings_Elevated") : Loc.Get("Settings_Standard");
        ElevationText.Text = elevated
            ? Loc.Get("Settings_ElevatedText")
            : Loc.Get("Settings_StandardText");
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
                Loc.Get("Settings_HistoryOld"),
            HistoryServiceState.Running => Loc.Get("Settings_HistoryOn"),
            HistoryServiceState.NotInstalled => App.Monitor.IsElevated ? Loc.Get("Settings_HistoryOff") : Loc.Get("Settings_HistoryOffUac"),
            HistoryServiceState.Starting => Loc.Get("Settings_HistoryStarting"),
            _ => Loc.Get("Settings_HistoryNotRunning")
        };
    }

    private async void HistoryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool enable = HistoryToggle.IsOn;
        HistoryToggle.IsEnabled = false;
        HistoryStatusText.Text = enable ? Loc.Get("Settings_TurningOn") : Loc.Get("Settings_TurningOff");
        string? error = await HistoryServiceSetup.SetEnabledAsync(enable, App.Monitor.IsElevated);
        HistoryToggle.IsEnabled = true;
        loading = true;
        ShowHistoryState();
        loading = false;
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = enable ? Loc.Get("Service_NotTurnedOn") : Loc.Get("Service_NotTurnedOff"), Content = error, CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
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

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        App.Settings.Language = tag;
        RestartForLanguage.Visibility = Visibility.Visible;
    }

    private void RestartForLanguage_Click(object sender, RoutedEventArgs e) => App.Restart();

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
