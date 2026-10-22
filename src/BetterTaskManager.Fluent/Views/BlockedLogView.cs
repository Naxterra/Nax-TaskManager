using System.Diagnostics;
using System.Globalization;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;
using BetterTaskManager.Fluent.Controls;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// The blocked log: every connection a firewall filter dropped, as the History service recorded it, grouped by app,
/// remote end and reason, newest first. Each row says which of your rules (or which other firewall) blocked it.
/// Clicking a row opens that app's rules. The same rows show in the Rules page editor for one app.
/// </summary>
internal sealed class BlockedLogView : Grid
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The service's database; tests pass another one with --history-db.</summary>
    private static readonly string Database = Environment.GetCommandLineArgs() is var args && Array.IndexOf(args, "--history-db") is int index and >= 0 && index + 1 < args.Length
        ? args[index + 1] : HistoryStore.DefaultPath;
    private readonly ComboBox range = new() { MinWidth = 150 };
    private readonly ToggleSwitch others = new() { OnContent = "", OffContent = "", MinWidth = 0 };
    private readonly TextBox search = new() { MinWidth = 220 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly Grid empty = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Action<string> openApp;
    private bool loading;

    public BlockedLogView(Action<string> openApp)
    {
        this.openApp = openApp;
        RowSpacing = 12;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Filters: time range, other firewalls, and a search over app and remote end.
        var filters = new WrapPanel { Spacing = 12 };
        foreach (string key in new[] { "Blocked_RangeHour", "Blocked_RangeDay", "Blocked_RangeWeek", "Blocked_RangeMonth" }) range.Items.Add(Loc.Get(key));
        range.SelectedIndex = 1;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(range, Loc.Get("Blocked_Range"));
        range.SelectionChanged += (_, _) => _ = LoadAsync();
        search.PlaceholderText = Loc.Get("Blocked_Search");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, Loc.Get("Blocked_Search"));
        search.TextChanged += (_, _) => _ = LoadAsync();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(others, Loc.Get("Blocked_Others"));
        others.Toggled += (_, _) => _ = LoadAsync();
        var othersLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        othersLabel.Children.Add(new TextBlock { Text = Loc.Get("Blocked_Others"), VerticalAlignment = VerticalAlignment.Center });
        othersLabel.Children.Add(others);
        filters.Children.Add(range);
        filters.Children.Add(search);
        filters.Children.Add(othersLabel);
        Children.Add(filters);

        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        SetRow(summary, 1);
        Children.Add(summary);

        var scroll = new ScrollViewer { Content = rows, Padding = new Thickness(0, 0, 14, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SetRow(scroll, 2);
        Children.Add(scroll);
        SetRow(empty, 2);
        Children.Add(empty);

        timer.Tick += (_, _) => _ = LoadAsync();
        Loaded += (_, _) => { timer.Start(); _ = LoadAsync(); };
        Unloaded += (_, _) => timer.Stop();
    }

    /// <summary>Shows only one app's drops; used by "Show all" in the editor.</summary>
    public void FilterApp(string path)
    {
        search.Text = AppName(path);
        _ = LoadAsync();
    }

    private TimeSpan Range => range.SelectedIndex switch { 0 => TimeSpan.FromHours(1), 1 => TimeSpan.FromDays(1), 2 => TimeSpan.FromDays(7), _ => TimeSpan.FromDays(30) };

    private async Task LoadAsync()
    {
        if (loading || !IsLoaded) return;
        loading = true;
        try
        {
            DateTime since = DateTime.UtcNow - Range;
            bool oursOnly = !others.IsOn;
            List<BlockedRecord>? records = await Task.Run(() => Read(since, null, 500, oursOnly));
            string query = search.Text.Trim();
            List<BlockedRecord> shown = (records ?? []).Where(record => query.Length == 0
                || AppName(record.AppPath).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || record.AppPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (record.RemoteHost ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.RemoteAddress.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            rows.Children.Clear();
            foreach (BlockedRecord record in shown.Take(300)) rows.Children.Add(Row(record, showApp: true, openApp));
            summary.Text = records is null ? "" : Loc.F("Blocked_Summary", Format.Count(shown.Sum(record => record.Count)), Format.Count(shown.Select(record => record.AppPath.ToLowerInvariant()).Distinct().Count()));
            empty.Children.Clear();
            if (shown.Count == 0) empty.Children.Add(EmptyFor(records is null, query.Length > 0));
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>The service's records, or null when there is no history database to read.</summary>
    public static List<BlockedRecord>? Read(DateTime sinceUtc, string? appPath, int limit, bool oursOnly)
    {
        try
        {
            using HistoryStore? store = HistoryStore.OpenReadOnly(Database);
            return store?.ReadBlocked(sinceUtc, appPath, limit, oursOnly);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static StackPanel EmptyFor(bool noDatabase, bool searching)
    {
        if (noDatabase || HistoryServiceControl.QueryState() != HistoryServiceState.Running)
        {
            var settings = new Button { Content = Loc.Get("Blocked_OpenSettings") };
            settings.Click += (_, _) => App.Window.NavigatePage("Settings");
            return Ui.EmptyState("", "Warn", Loc.Get("Blocked_NoServiceTitle"), Loc.Get("Blocked_NoServiceText"), settings);
        }
        return searching ? Ui.EmptyState("", "Idle", Loc.Get("Blocked_NoMatchTitle"), Loc.Get("Blocked_NoMatchText"))
            : Ui.EmptyState("", "Good", Loc.Get("Blocked_NothingTitle"), Loc.Get("Blocked_NothingText"));
    }

    /// <summary>
    /// One drop group: icon, app (or remote end), direction and remote end, the reason as a colored chip, and how
    /// often and when. Clicking opens the app's rules.
    /// </summary>
    public static Button Row(BlockedRecord record, bool showApp, Action<string> openApp)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 46 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });

        FrameworkElement lead = showApp
            ? new Image { Source = IconCache.Get(record.AppPath), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center }
            : Ui.IconBadge(record.Inbound ? "" : "", record.Inbound ? "Receive" : "Send", 28);
        grid.Children.Add(lead);

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        string remote = Remote(record);
        texts.Children.Add(Line(showApp ? AppName(record.AppPath) : remote, 13, primary: true));
        var detail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (showApp)
        {
            var arrow = new FontIcon { Glyph = record.Inbound ? "" : "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(IconElement.ForegroundProperty, record.Inbound ? "ToneReceiveBrush" : "ToneSendBrush");
            detail.Children.Add(arrow);
            detail.Children.Add(Line(remote, 12, primary: false));
        }
        else detail.Children.Add(Line(Loc.Get(record.Inbound ? "Blocked_Inbound" : "Blocked_Outbound"), 12, primary: false));
        texts.Children.Add(detail);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        (string reason, string tone) = Reason(record);
        Border chip = Ui.Chip(reason, tone);
        chip.MaxWidth = 280;
        Grid.SetColumn(chip, 2);
        grid.Children.Add(chip);

        var when = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var count = Line("×" + Format.Count(record.Count), 13, primary: true);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        var time = Line(When(record.LastSeen), 11, primary: false);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        when.Children.Add(count);
        when.Children.Add(time);
        Grid.SetColumn(when, 3);
        grid.Children.Add(when);

        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        string tip = Loc.F("Blocked_Tip", AppName(record.AppPath), record.AppPath, record.FirstSeen.ToLocalTime().ToString("g"), record.LastSeen.ToLocalTime().ToString("g"));
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, AppName(record.AppPath) + ", " + remote + ", " + reason);
        string path = record.AppPath;
        button.Click += (_, _) => openApp(path);
        return button;
    }

    /// <summary>The remote end with port and protocol; incoming drops name the local port they were aimed at.</summary>
    private static string Remote(BlockedRecord record)
    {
        string protocol = record.Protocol switch { 6 => "TCP", 17 => "UDP", 1 or 58 => "ICMP", _ => record.Protocol.ToString(CultureInfo.InvariantCulture) };
        string host = record.RemoteHost ?? (record.RemoteAddress.Length > 0 ? record.RemoteAddress : "?");
        // With a host name, the address follows it: one name often has several addresses, each its own row.
        string address = record.RemoteHost is not null && record.RemoteAddress.Length > 0 ? " · " + record.RemoteAddress : "";
        if (record.Inbound) return Loc.F("Blocked_RemoteIn", host, record.LocalPort, protocol) + address;
        return (record.RemotePort > 0 ? $"{host}:{record.RemotePort} {protocol}" : $"{host} {protocol}") + address;
    }

    /// <summary>Which rule or switch blocked it, as text and a tone (red whole app, amber switches, purple rules).</summary>
    public static (string Text, string Tone) Reason(BlockedRecord record)
    {
        if (!record.Ours) return (Loc.F("Blocked_OtherFirewall", record.Reason), "Idle");
        return record.Reason switch
        {
            PolicyReason.WholeApp => (Loc.Get("Blocked_WholeApp"), "Bad"),
            PolicyReason.BlockInternet => (Loc.Get("Blocked_Internet"), "Warn"),
            PolicyReason.BlockLan => (Loc.Get("Blocked_Lan"), "Warn"),
            PolicyReason.BlockLocalhost => (Loc.Get("Blocked_Localhost"), "Warn"),
            PolicyReason.BlockInbound => (Loc.Get("Blocked_InboundSwitch"), "Warn"),
            string rule when rule.StartsWith(PolicyReason.RulePrefix, StringComparison.Ordinal) => (Loc.F("Blocked_Rule", rule[PolicyReason.RulePrefix.Length..]), "Memory"),
            string other => (other, "Rose")
        };
    }

    /// <summary>"3 min ago" for the last hour, the time for today, else date and time.</summary>
    private static string When(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        if (age < TimeSpan.FromMinutes(1)) return Loc.Get("Blocked_JustNow");
        if (age < TimeSpan.FromHours(1)) return Loc.F("Blocked_MinutesAgo", (int)age.TotalMinutes);
        DateTime local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("t", CultureInfo.CurrentCulture) : local.ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>The file description, cached, else the file name (the service records lower-case paths for ended processes).</summary>
    public static string AppName(string path)
    {
        if (Names.TryGetValue(path, out string? name)) return name;
        name = Path.GetFileName(path);
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description) name = description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        Names[path] = name;
        return name;
    }

    private static TextBlock Line(string text, double size, bool primary)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        if (primary) block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, primary ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        return block;
    }
}
