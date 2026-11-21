using System.Diagnostics;
using System.Reflection;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Native;
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
        ReplaceToggle.IsEnabled = false;
        HistoryToggle.IsEnabled = false;
        loading = false;
        Loaded += (_, _) => _ = RefreshSystemStatesAsync();
    }

    private async Task RefreshSystemStatesAsync()
    {
        await Task.WhenAll(RefreshReplaceStateAsync(), RefreshHistoryStateAsync());
    }

    private void OpenTuning_Click(object sender, RoutedEventArgs e) => App.Window.NavigatePage("Tuning");

    private async Task RefreshReplaceStateAsync()
    {
        string? exe = Environment.ProcessPath;
        (string? other, bool ours, bool allowed) = await Task.Run(() =>
        {
            if (exe is null) return ((string?)null, false, false);
            return (TaskManagerReplacement.OtherTarget(exe), TaskManagerReplacement.IsReplacedBy(exe), TaskManagerReplacement.CanReplaceWith(exe));
        });
        if (!IsLoaded) return;
        loading = true;
        try
        {
            ReplaceToggle.IsOn = ours;
            // Leave another tool's replacement alone: show it and disable the switch so toggling cannot overwrite it. A
            // replacement that is on stays switchable, so an unsafe one from an earlier version can be turned off.
            ReplaceToggle.IsEnabled = other is null && (allowed || ours);
            ReplaceStatusText.Text = ours ? Loc.Get(allowed ? "Replace_On" : "Replace_OnUnsafe")
                : other is not null ? Loc.F("Replace_Other", other)
                : !allowed ? Loc.Get("Replace_NeedsMachineInstall")
                : App.Monitor.IsElevated ? Loc.Get("Replace_Off") : Loc.Get("Replace_OffUac");
        }
        finally
        {
            loading = false;
        }
    }

    private async void ReplaceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool enable = ReplaceToggle.IsOn;
        string? exe = Environment.ProcessPath;
        if (exe is null) return;

        ReplaceToggle.IsEnabled = false;
        string? error = null;
        try
        {
            error = await TaskManagerSwitch.SetAsync(enable);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            error = ex.Message;
        }
        finally
        {
            await RefreshReplaceStateAsync();
        }
        if (error is not null) await Dialogs.ShowAsync(XamlRoot, Loc.Get("Replace_NotChanged"), error);
    }

    private async Task RefreshHistoryStateAsync()
    {
        (HistoryServiceState state, bool needsUpdate) = await Task.Run(() =>
        {
            HistoryServiceState current = HistoryServiceControl.QueryState();
            return (current, current == HistoryServiceState.Running && HistoryServiceControl.NeedsUpdate(HistoryServiceSetup.BundledFolder));
        });
        if (!IsLoaded) return;
        loading = true;
        try
        {
            HistoryToggle.IsOn = state != HistoryServiceState.NotInstalled;
            HistoryToggle.IsEnabled = true;
            HistoryStatusText.Text = state switch
            {
                HistoryServiceState.Running when needsUpdate => Loc.Get("Settings_HistoryOld"),
                HistoryServiceState.Running => Loc.Get("Settings_HistoryOn"),
                HistoryServiceState.NotInstalled => App.Monitor.IsElevated ? Loc.Get("Settings_HistoryOff") : Loc.Get("Settings_HistoryOffUac"),
                HistoryServiceState.Starting => Loc.Get("Settings_HistoryStarting"),
                _ => Loc.Get("Settings_HistoryNotRunning")
            };
        }
        finally
        {
            loading = false;
        }
    }

    private async void HistoryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool enable = HistoryToggle.IsOn;
        HistoryToggle.IsEnabled = false;
        HistoryStatusText.Text = enable ? Loc.Get("Settings_TurningOn") : Loc.Get("Settings_TurningOff");
        string? error = null;
        try
        {
            error = await HistoryServiceSetup.SetEnabledAsync(enable, App.Monitor.IsElevated);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            error = ex.Message;
        }
        finally
        {
            await RefreshHistoryStateAsync();
        }
        if (error is not null)
        {
            await Dialogs.ShowAsync(XamlRoot, enable ? Loc.Get("Service_NotTurnedOn") : Loc.Get("Service_NotTurnedOff"), error);
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
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaxTaskManager");
            Directory.CreateDirectory(folder);
            using Process? process = Process.Start(new ProcessStartInfo(BetterTaskManager.Core.Native.ExplorerShell.ExplorerPath, folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
    }
}
