using System.Diagnostics;
using System.Globalization;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Controls;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// The one place for every rule: on the left each app that has a network block, network rules or a memory rule; on
/// the right the selected app's rules to edit. Saving applies everything for that app in one step (one UAC prompt
/// without elevation). The editor is part of the page, so it follows the window's size and scrolls on its own.
/// </summary>
public sealed partial class RulesPage : Page
{
    private static readonly (string Key, string Rules)[] OutgoingPresets =
    [
        ("Rules_PresetWeb", "+ Localhost\n+ * TCP/80\n+ * TCP/443\n+ * UDP/443 # HTTP/3\n- *"),
        ("Rules_PresetLan", "+ Localhost\n+ LAN\n- *"),
        ("Rules_PresetLocal", "+ Localhost\n- *")
    ];
    private static readonly int?[] Priorities = [null, 5, 4, 3, 2, 1];
    private const double StackedWidth = 860;

    private readonly Grid body = new() { ColumnSpacing = 18, RowSpacing = 12 };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0, 0, 14, 0) };
    private readonly TextBlock summary = Text("", 13, secondary: true);
    private readonly StackPanel listEmpty = Ui.EmptyState("\uE8D7", "Rose", Loc.Get("RulesPage_EmptyTitle"), Loc.Get("RulesPage_Empty"));
    private readonly Border listCard, editorCard;
    private readonly Grid editorHost = new();
    private readonly SelectorBar sections = new();
    private readonly Border logCard;
    private readonly BlockedLogView log;
    private readonly Button back = new() { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(8), Visibility = Visibility.Collapsed };
    private Editor? editor;
    private string? pendingPath;
    private bool selecting, stacked;

    public RulesPage()
    {
        InitializeComponent();
        var root = new Grid { Padding = new Thickness(28, 20, 28, 20), RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        titleRow.Children.Add(Ui.IconBadge("\uE8D7", "Rose", 38));
        titleRow.Children.Add(Text(Loc.Get("RulesPage_Title"), 30, strong: true));
        titles.Children.Add(titleRow);
        summary.TextWrapping = TextWrapping.Wrap;
        summary.Margin = new Thickness(50, 0, 0, 0);
        titles.Children.Add(summary);
        header.Children.Add(titles);
        var add = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = Loc.Get("RulesPage_AddApp") } } },
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            VerticalAlignment = VerticalAlignment.Top
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, Loc.Get("RulesPage_AddApp"));
        add.Click += async (_, _) => { if (await PickAppAsync() is { } path) _ = SelectAsync(path); };
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        root.Children.Add(header);

        // Two sections: the apps and their rules, and the log of what the rules blocked.
        sections.Items.Add(new SelectorBarItem { Text = Loc.Get("RulesPage_TabRules"), Icon = new FontIcon { Glyph = "\uE8D7" }, Tag = "Rules" });
        sections.Items.Add(new SelectorBarItem { Text = Loc.Get("RulesPage_TabBlocked"), Icon = new FontIcon { Glyph = "\uE72E" }, Tag = "Blocked" });
        sections.SelectedItem = sections.Items[0];
        sections.SelectionChanged += (_, _) => ShowSection();
        Grid.SetRow(sections, 1);
        root.Children.Add(sections);

        var listContent = new Grid { RowSpacing = 8 };
        listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        listContent.Children.Add(Text(Loc.Get("RulesPage_ListTitle"), 14, strong: true));
        Grid.SetRow(listEmpty, 1);
        listContent.Children.Add(listEmpty);
        Grid.SetRow(list, 2);
        listContent.Children.Add(list);
        list.SelectionChanged += List_SelectionChanged;
        listCard = Card(listContent, new Thickness(12, 14, 0, 12));

        var addFromPlaceholder = new Button { Content = Loc.Get("RulesPage_AddApp") };
        addFromPlaceholder.Click += async (_, _) => { if (await PickAppAsync() is { } path) _ = SelectAsync(path); };
        editorHost.Children.Add(Ui.EmptyState("\uE8D7", "Rose", Loc.Get("RulesPage_ChooseTitle"), Loc.Get("RulesPage_Choose"), addFromPlaceholder));
        editorCard = Card(editorHost, new Thickness(0));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back, Loc.Get("RulesPage_Back"));
        ToolTipService.SetToolTip(back, Loc.Get("RulesPage_Back"));
        back.Click += (_, _) => { if (stacked) ShowList(); };

        body.Children.Add(listCard);
        body.Children.Add(editorCard);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        log = new BlockedLogView(path => ShowApp(path));
        logCard = Card(log, new Thickness(20, 16, 6, 12));
        logCard.Visibility = Visibility.Collapsed;
        Grid.SetRow(logCard, 2);
        root.Children.Add(logCard);
        Content = root;
        SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Loaded += (_, _) =>
        {
            App.Monitor.FirewallChanged -= OnRulesChanged;
            App.Monitor.FirewallChanged += OnRulesChanged;
            App.Monitor.Updated -= OnUpdated;
            App.Monitor.Updated += OnUpdated;
            RebuildList();
            _ = App.Monitor.RefreshFirewallAsync();
        };
        Unloaded += (_, _) =>
        {
            App.Monitor.FirewallChanged -= OnRulesChanged;
            App.Monitor.Updated -= OnUpdated;
        };
    }

    /// <summary>Opens one app's rules; from the Processes and Network pages, the Overview and the blocked log.</summary>
    public void ShowApp(string path)
    {
        sections.SelectedItem = sections.Items[0];
        _ = SelectAsync(path);
    }

    /// <summary>Opens the blocked log, optionally searched for one app.</summary>
    public void ShowBlocked(string? path = null)
    {
        sections.SelectedItem = sections.Items[1];
        if (path is not null) log.FilterApp(path);
    }

    private void ShowSection()
    {
        bool blocked = Equals(sections.SelectedItem?.Tag, "Blocked");
        body.Visibility = blocked ? Visibility.Collapsed : Visibility.Visible;
        logCard.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Arrange(double width)
    {
        bool now = width < StackedWidth;
        stacked = now;
        body.ColumnDefinitions.Clear();
        if (stacked)
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(editorCard, 0);
            if (editor is null) ShowList(); else ShowEditor();
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(width * 0.28, 250, 340)) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(editorCard, 1);
            listCard.Visibility = editorCard.Visibility = Visibility.Visible;
            back.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowList()
    {
        listCard.Visibility = Visibility.Visible;
        editorCard.Visibility = Visibility.Collapsed;
    }

    private void ShowEditor()
    {
        listCard.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        editorCard.Visibility = Visibility.Visible;
        back.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== List =====

    private sealed record Entry(string Path, string Name, List<(string Text, string Tone)> Chips, bool Missing)
    {
        public string Summary => Chips.Count == 0 ? Loc.Get("RulesPage_NoRulesYet") : string.Join(", ", Chips.Select(chip => chip.Text));
    }

    private List<Entry> Collect()
    {
        var paths = new HashSet<string>(App.Monitor.BlockedPaths, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(App.Monitor.Policies.Keys);
        paths.UnionWith(App.Monitor.MemoryRules.Keys);
        if (editor is not null) paths.Add(editor.Path);
        // A helper program that follows an app's rules shows as part of that app, not as an app of its own.
        paths.RemoveWhere(path => App.Monitor.HelperOwner(path) is { } owner && !string.Equals(owner, path, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(editor?.Path, path, StringComparison.OrdinalIgnoreCase));
        return paths.Select(path => new Entry(path, DisplayName(path), Describe(path), !File.Exists(path)))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>What the app's rules do as colored chips: block (red), switches (amber), network rules (teal), memory (purple).</summary>
    private static List<(string Text, string Tone)> Describe(string path)
    {
        var parts = new List<(string, string)>();
        if (App.Monitor.BlockedPaths.Contains(path)) parts.Add((Loc.Get("RulesList_Blocked"), "Bad"));
        if (App.Monitor.Policies.TryGetValue(path, out AppNetworkPolicy? policy))
        {
            int switches = new[] { policy.BlockInternet, policy.BlockLan, policy.BlockLocalhost, policy.BlockInbound }.Count(on => on);
            if (switches > 0) parts.Add((Loc.F("RulesPage_Switches", switches), "Warn"));
            int rules = policy.Outgoing.Count + policy.Incoming.Count;
            if (rules > 0) parts.Add((Loc.F("RulesPage_RuleCount", rules), "Receive"));
            if (policy.Helpers.Count > 0) parts.Add((Loc.F("RulesPage_HelperCount", policy.Helpers.Count), "Gpu"));
        }
        if (App.Monitor.MemoryRules.TryGetValue(path, out AppMemoryRule? memory))
        {
            if (memory.LimitMegabytes is { } mb) parts.Add((Loc.F("RulesPage_LimitShort", Format.Memory(mb * 1048576L)), "Memory"));
            if (memory.Priority is { } priority) parts.Add((Loc.F("RulesPage_PriorityShort", PriorityName(priority)), "Memory"));
        }
        return parts;
    }

    private void RebuildList()
    {
        List<Entry> entries = Collect();
        int count = entries.Count(entry => entry.Chips.Count > 0);
        summary.Text = !App.Monitor.FirewallStateKnown ? Loc.Get("Workflow_PolicyUnknown") : Loc.F("RulesPage_Summary", count);
        listEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        selecting = true;
        list.Items.Clear();
        foreach (Entry entry in entries)
        {
            var item = new ListViewItem { Tag = entry.Path, Content = ListRow(entry), Padding = new Thickness(10, 8, 10, 8) };
            ToolTipService.SetToolTip(item, entry.Path);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, entry.Name + ", " + entry.Summary);
            list.Items.Add(item);
            if (editor is not null && string.Equals(entry.Path, editor.Path, StringComparison.OrdinalIgnoreCase)) list.SelectedItem = item;
        }
        selecting = false;
    }

    private static Grid ListRow(Entry entry)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Image { Source = IconCache.Get(entry.Path), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(Text(entry.Name, 13, strong: true));
        List<(string Text, string Tone)> chips = entry.Missing ? [(Loc.Get("RulesList_Missing"), "Warn")]
            : entry.Chips.Count > 0 ? entry.Chips : [(Loc.Get("RulesPage_NoRulesYet"), "Idle")];
        var chipRow = Ui.Chips(chips);
        chipRow.Margin = new Thickness(0, 3, 0, 0);
        texts.Children.Add(chipRow);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        return row;
    }

    private async void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selecting || list.SelectedItem is not ListViewItem { Tag: string path }) return;
        if (editor is not null && string.Equals(editor.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            ShowEditor();
            return;
        }
        if (!await SelectAsync(path)) RebuildList();
    }

    private void OnRulesChanged()
    {
        // After a save, or when something else changed the rules: show the saved state unless the user is editing.
        if (editor is { IsDirty: false, Saving: false }) Open(editor.Path);
        RebuildList();
    }

    private void OnUpdated(MonitorSnapshot snapshot) => editor?.ShowUsage(snapshot);

    // ===== Selection =====

    private async Task<bool> SelectAsync(string path)
    {
        pendingPath = path;
        if (editor is { IsDirty: true } && !string.Equals(editor.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            var ask = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get("RulesPage_DiscardTitle"),
                Content = Loc.F("RulesPage_DiscardText", editor.Name),
                PrimaryButtonText = Loc.Get("RulesPage_Discard"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await ask.ShowAsync() != ContentDialogResult.Primary || pendingPath != path) return false;
        }
        Open(path);
        RebuildList();
        return true;
    }

    private void Open(string path)
    {
        editor = new Editor(this, path);
        editorHost.Children.Clear();
        editorHost.Children.Add(editor.Root);
        if (App.Monitor.Latest is { } snapshot) editor.ShowUsage(snapshot);
        ShowEditor();
    }

    /// <summary>The file description (as Task Manager shows it), else the file name.</summary>
    internal static string DisplayName(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description) return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return System.IO.Path.GetFileName(path);
    }

    private static string PriorityName(int priority) => Loc.Get("RulesPage_Priority" + priority.ToString(CultureInfo.InvariantCulture));

    /// <summary>Lets the user pick a running app, or type the path of a program file.</summary>
    private async Task<string?> PickAppAsync()
    {
        var running = (App.Monitor.Latest?.Processes ?? []).Where(process => process.Path.Length > 0 && process.Pid > 4 && !AppIdentityRules.IsEmbeddedHost(process))
            .GroupBy(process => process.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Path: group.Key, Name: Loc.AppName(group.First().DisplayName), Count: group.Count()))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var filter = new TextBox { PlaceholderText = Loc.Get("RulesPage_PickFilter") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(filter, Loc.Get("RulesPage_PickFilter"));
        var apps = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 320 };
        var pathBox = new TextBox { PlaceholderText = Loc.Get("RulesPage_PickPath"), IsSpellCheckEnabled = false };
        void Fill()
        {
            apps.Items.Clear();
            string query = filter.Text.Trim();
            foreach (var app in running.Where(app => query.Length == 0 || app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || app.Path.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Image { Source = IconCache.Get(app.Path), Width = 20, Height = 20 });
                var texts = new StackPanel();
                texts.Children.Add(Text(app.Name + (app.Count > 1 ? $" ({app.Count})" : ""), 13));
                texts.Children.Add(Text(app.Path, 11, secondary: true));
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);
                var item = new ListViewItem { Content = row, Tag = app.Path };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, app.Name);
                apps.Items.Add(item);
            }
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(Text(Loc.Get("RulesPage_PickRunning"), 13, strong: true));
        panel.Children.Add(filter);
        panel.Children.Add(apps);
        panel.Children.Add(Text(Loc.Get("RulesPage_PickOr"), 13, strong: true));
        panel.Children.Add(pathBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("RulesPage_AddApp"),
            Content = panel,
            PrimaryButtonText = Loc.Get("RulesPage_PickOpen"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        bool picked = false;
        dialog.PrimaryButtonClick += (_, _) => picked = true;
        apps.DoubleTapped += (_, _) =>
        {
            if (apps.SelectedItem is null) return;
            picked = true;
            dialog.Hide();
        };
        await dialog.ShowAsync();
        if (!picked) return null;
        string typed = pathBox.Text.Trim().Trim('"');
        if (typed.Length > 0) return File.Exists(typed) ? System.IO.Path.GetFullPath(typed) : null;
        return (apps.SelectedItem as ListViewItem)?.Tag as string;
    }

    // ===== Helpers =====

    private static TextBlock Text(string text, double size, bool strong = false, bool secondary = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return block;
    }

    private static TextBlock Note(string text)
    {
        var block = Text(text, 12, secondary: true);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    private static Border Card(UIElement content, Thickness padding)
    {
        var card = new Border { Child = content, Padding = padding, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeBrush");
        return card;
    }

    /// <summary>A title with its explanation on the left and the control on the right, like a Windows settings row.</summary>
    private static Grid SettingRow(string title, string description, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var heading = Text(title, 13);
        heading.TextWrapping = TextWrapping.Wrap;
        texts.Children.Add(heading);
        if (description.Length > 0) texts.Children.Add(Note(description));
        row.Children.Add(texts);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static ToggleSwitch Switch(bool on, string name)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    /// <summary>A section heading with a colored badge, so the network, memory and blocked parts are told apart at a glance.</summary>
    private static StackPanel Section(string glyph, string tone, string title)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        heading.Children.Add(Ui.IconBadge(glyph, tone, 32));
        heading.Children.Add(Text(title, 17, strong: true));
        return heading;
    }

    /// <summary>
    /// The editor for one app. It keeps what was loaded, so it knows when something changed and saves only the parts
    /// that did.
    /// </summary>
    private sealed class Editor
    {
        private readonly RulesPage page;
        private readonly bool loadedBlocked;
        private readonly AppNetworkPolicy loadedPolicy;
        private readonly AppMemoryRule loadedMemory;
        private readonly string loadedNetworkKey, storedNetworkKey, loadedMemoryKey;
        private readonly List<(string Path, CheckBox Box)> helperBoxes = new();
        private readonly ToggleSwitch blockAll, internet, lan, localhost, inbound, limitOn;
        private readonly RuleListEditor outgoing, incoming;
        private readonly NumberBox limit;
        private readonly ComboBox priority = new() { MinWidth = 200 };
        private readonly TextBlock usage = Text("", 12, secondary: true), errors = Note(""), status = Note("");
        private readonly Button save = new() { Content = Loc.Get("Rules_Save") }, discard = new() { Content = Loc.Get("RulesPage_Discard") }, removeAll = new() { Content = Loc.Get("Rules_RemoveAll") };
        private readonly StackPanel networkDetails = new() { Spacing = 14 };
        private readonly StackPanel recent = new() { Spacing = 4 };
        private DateTime recentReadAt;

        public Editor(RulesPage page, string path)
        {
            this.page = page;
            Path = path;
            Name = DisplayName(path);
            loadedBlocked = App.Monitor.BlockedPaths.Contains(path) || App.Monitor.IsBlocked(path);
            loadedPolicy = App.Monitor.PolicyFor(path);
            loadedMemory = App.Monitor.MemoryRules.TryGetValue(path, out AppMemoryRule? memory)
                ? new AppMemoryRule { Path = path, LimitMegabytes = memory.LimitMegabytes, Priority = memory.Priority }
                : new AppMemoryRule { Path = path };
            storedNetworkKey = Key(loadedPolicy);
            loadedMemoryKey = Key(loadedMemory);

            var content = new StackPanel { Spacing = 14, Padding = new Thickness(24, 20, 30, 24) };

            // The app: icon, name, path, and what it uses right now.
            var header = new Grid { ColumnSpacing = 14 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (page.back.Parent is Panel previous) previous.Children.Remove(page.back);
            header.Children.Add(page.back);
            var icon = new Image { Source = IconCache.Get(path), Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(icon, 1);
            header.Children.Add(icon);
            var names = new StackPanel { Spacing = 3 };
            names.Children.Add(Text(Name, 22, strong: true));
            var pathText = Note(path);
            pathText.IsTextSelectionEnabled = true;
            names.Children.Add(pathText);
            names.Children.Add(usage);
            Grid.SetColumn(names, 2);
            header.Children.Add(names);
            content.Children.Add(header);

            // Network.
            content.Children.Add(Section("", "Receive", Loc.Get("RulesPage_Network")));
            blockAll = Switch(loadedBlocked, Loc.Get("RulesPage_BlockAll"));
            content.Children.Add(SettingRow(Loc.Get("RulesPage_BlockAll"), Loc.Get("RulesPage_BlockAllNote"), blockAll));
            if (BuildHelpers() is { } helpers) content.Children.Add(helpers);
            internet = Switch(loadedPolicy.BlockInternet, Loc.Get("Rules_BlockInternet"));
            lan = Switch(loadedPolicy.BlockLan, Loc.Get("Rules_BlockLan"));
            localhost = Switch(loadedPolicy.BlockLocalhost, Loc.Get("Rules_BlockLocalhost"));
            inbound = Switch(loadedPolicy.BlockInbound, Loc.Get("Rules_BlockInbound"));
            networkDetails.Children.Add(SettingRow(Loc.Get("Rules_BlockInternet"), "", internet));
            networkDetails.Children.Add(SettingRow(Loc.Get("Rules_BlockLan"), "", lan));
            networkDetails.Children.Add(SettingRow(Loc.Get("Rules_BlockLocalhost"), "", localhost));
            networkDetails.Children.Add(SettingRow(Loc.Get("Rules_BlockInbound"), Loc.Get("Rules_SwitchesNote"), inbound));

            outgoing = new RuleListEditor(loadedPolicy.Outgoing, "Rules_ErrLineOut", inbound: false);
            incoming = new RuleListEditor(loadedPolicy.Incoming, "Rules_ErrLineIn", inbound: true);
            var presets = new MenuFlyout();
            foreach ((string key, string rules) in OutgoingPresets)
            {
                var item = new MenuFlyoutItem { Text = Loc.Get(key) };
                item.Click += (_, _) => outgoing.SetLines(rules.Split('\n'));
                presets.Items.Add(item);
            }
            networkDetails.Children.Add(outgoing.Build(Loc.Get("Rules_OutgoingTitle"), Loc.Get("RulesPage_OrderNote"), new DropDownButton { Content = Loc.Get("Rules_Presets"), Flyout = presets }));
            networkDetails.Children.Add(incoming.Build(Loc.Get("Rules_IncomingTitle"), Loc.Get("RulesPage_IncomingNote"), null));
            networkDetails.Children.Add(new Expander
            {
                Header = Loc.Get("Rules_HelpTitle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = Loc.Get("Rules_Help"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            });
            content.Children.Add(networkDetails);

            // What the rules blocked lately: the effect of the rules right under them.
            var recentHeader = new Grid();
            recentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            recentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            recentHeader.Children.Add(Section("\uE72E", "Bad", Loc.Get("RulesPage_Recent")));
            var all = new HyperlinkButton { Content = Loc.Get("RulesPage_RecentAll"), VerticalAlignment = VerticalAlignment.Bottom };
            all.Click += (_, _) => page.ShowBlocked(path);
            Grid.SetColumn(all, 1);
            recentHeader.Children.Add(all);
            content.Children.Add(recentHeader);
            content.Children.Add(recent);

            // Memory.
            content.Children.Add(Section("", "Memory", Loc.Get("RulesPage_Memory")));
            limitOn = Switch(loadedMemory.LimitMegabytes is not null, Loc.Get("RulesPage_Limit"));
            long totalMegabytes = Math.Max(1024, (App.Monitor.Latest?.System.Memory.Total ?? 0) / 1048576);
            limit = new NumberBox
            {
                Minimum = AppMemoryRule.MinimumMegabytes,
                Maximum = totalMegabytes,
                Value = loadedMemory.LimitMegabytes ?? 2048,
                SmallChange = 256,
                LargeChange = 1024,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                MinWidth = 170,
                Header = Loc.Get("RulesPage_LimitUnit")
            };
            var limitControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            limitControls.Children.Add(limit);
            limitControls.Children.Add(limitOn);
            content.Children.Add(SettingRow(Loc.Get("RulesPage_Limit"), Loc.Get("RulesPage_LimitNote"), limitControls));
            foreach (int? value in Priorities) priority.Items.Add(value is { } p ? PriorityName(p) : Loc.Get("RulesPage_PriorityDefault"));
            priority.SelectedIndex = Array.IndexOf(Priorities, loadedMemory.Priority) is int index and >= 0 ? index : 0;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(priority, Loc.Get("RulesPage_Priority"));
            content.Children.Add(SettingRow(Loc.Get("RulesPage_Priority"), Loc.Get("RulesPage_PriorityNote"), priority));
            if (HistoryServiceControl.QueryState() != HistoryServiceState.Running)
            {
                content.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = Loc.Get("RulesPage_NeedsService") });
            }

            var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // The footer stays in place while the rules scroll: messages, then the buttons.
            var footer = new Grid { Padding = new Thickness(24, 12, 24, 14), RowSpacing = 8, ColumnSpacing = 8 };
            footer.SetResourceReference(Grid.BorderBrushProperty, "SurfaceStrokeBrush");
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            errors.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            var messages = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            messages.Children.Add(errors);
            messages.Children.Add(status);
            footer.Children.Add(messages);
            save.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(removeAll);
            buttons.Children.Add(discard);
            buttons.Children.Add(save);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            Root = new Grid();
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.Children.Add(scroll);
            Grid.SetRow(footer, 1);
            Root.Children.Add(footer);

            foreach (ToggleSwitch toggle in new[] { blockAll, internet, lan, localhost, inbound, limitOn }) toggle.Toggled += (_, _) => Validate();
            loadedNetworkKey = Key(CurrentPolicy());
            limit.ValueChanged += (_, _) => Validate();
            priority.SelectionChanged += (_, _) => Validate();
            outgoing.Changed = Validate;
            incoming.Changed = Validate;
            save.Click += async (_, _) => await SaveAsync(remove: false);
            discard.Click += (_, _) => page.Open(Path);
            removeAll.Click += async (_, _) => await SaveAsync(remove: true);
            Validate();
        }

        public string Path { get; }
        public string Name { get; }
        public Grid Root { get; }
        public bool Saving { get; private set; }
        public bool IsDirty => blockAll.IsOn != loadedBlocked || Key(CurrentPolicy()) != loadedNetworkKey || Key(CurrentMemory()) != loadedMemoryKey;

        private bool HasAnything => loadedBlocked || !loadedPolicy.IsEmpty || !loadedMemory.IsEmpty;

        /// <summary>The app's drops of the last 24 hours (re-read at most every 5 seconds).</summary>
        private async Task ShowRecentAsync()
        {
            if (DateTime.UtcNow - recentReadAt < TimeSpan.FromSeconds(5)) return;
            recentReadAt = DateTime.UtcNow;
            string path = Path;
            List<string> paths = [path, .. helperBoxes.Where(helper => helper.Box.IsChecked == true).Select(helper => helper.Path)];
            List<Core.History.BlockedRecord>? records = await Task.Run(() =>
            {
                List<Core.History.BlockedRecord>? all = null;
                foreach (string one in paths)
                {
                    if (BlockedLogView.Read(DateTime.UtcNow.AddDays(-1), one, 6, oursOnly: false) is { } part) (all ??= []).AddRange(part);
                }
                return all?.OrderByDescending(record => record.LastSeen).Take(6).ToList();
            });
            recent.Children.Clear();
            if (records is null || records.Count == 0)
            {
                recent.Children.Add(Note(Loc.Get(records is null ? "RulesPage_RecentNoService" : "RulesPage_RecentNone")));
                return;
            }
            foreach (Core.History.BlockedRecord record in records) recent.Children.Add(BlockedLogView.Row(record, showApp: false, _ => page.ShowBlocked(path)));
        }

        public void ShowUsage(MonitorSnapshot snapshot)
        {
            _ = ShowRecentAsync();
            // The app's own processes and the helpers it started, as the Processes page counts them.
            Dictionary<int, ProcessSample> byPid = snapshot.Processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
            List<ProcessSample> processes = snapshot.Processes.Where(p => string.Equals(AppIdentityRules.Owner(p, byPid).Path, Path, StringComparison.OrdinalIgnoreCase)).ToList();
            if (processes.Count == 0)
            {
                usage.Text = Loc.Get("RulesPage_NotRunning");
                return;
            }
            var pids = processes.Select(p => p.Pid).ToHashSet();
            usage.Text = Loc.F(processes.Count == 1 ? "RulesPage_UsageOne" : "RulesPage_Usage", processes.Count, Format.Memory(processes.Sum(p => p.PrivateWorkingSet)),
                snapshot.Connections.Count(c => pids.Contains(c.Pid)));
        }

        private AppNetworkPolicy CurrentPolicy() => new()
        {
            Path = Path,
            BlockInternet = internet.IsOn,
            BlockLan = lan.IsOn,
            BlockLocalhost = localhost.IsOn,
            BlockInbound = inbound.IsOn,
            Outgoing = outgoing.Lines(),
            Incoming = incoming.Lines(),
            Helpers = helperBoxes.Where(helper => helper.Box.IsChecked == true).Select(helper => helper.Path).ToList()
        };

        private AppMemoryRule CurrentMemory() => new()
        {
            Path = Path,
            LimitMegabytes = limitOn.IsOn && !double.IsNaN(limit.Value) ? (int)Math.Round(limit.Value) : null,
            Priority = Priorities[Math.Max(0, priority.SelectedIndex)]
        };

        private static string Key(AppNetworkPolicy policy) => string.Join("|", policy.BlockInternet, policy.BlockLan, policy.BlockLocalhost, policy.BlockInbound,
            string.Join("\n", policy.Outgoing), string.Join("\n", policy.Incoming),
            string.Join("\n", policy.Helpers.Select(helper => helper.ToLowerInvariant()).Order(StringComparer.Ordinal)));

        /// <summary>
        /// The app's helper programs: the browser-engine processes it started (running now) plus the ones its rules
        /// already cover. Each is a check box; a helper only this app uses is checked for new rules, a shared one
        /// (the WebView2 runtime, or a program other apps use too) only when the user checks it.
        /// </summary>
        private FrameworkElement? BuildHelpers()
        {
            var found = new Dictionary<string, (string Name, HashSet<string> Others, bool Running)>(StringComparer.OrdinalIgnoreCase);
            if (App.Monitor.Latest is { } snapshot)
            {
                Dictionary<int, ProcessSample> byPid = snapshot.Processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
                foreach (ProcessSample process in snapshot.Processes.Where(p => p.Path.Length > 0 && AppIdentityRules.IsEmbeddedHost(p)))
                {
                    ProcessSample owner = AppIdentityRules.Owner(process, byPid);
                    if (!string.Equals(owner.Path, Path, StringComparison.OrdinalIgnoreCase)) continue;
                    found.TryAdd(process.Path, (Loc.AppName(process.DisplayName), new HashSet<string>(StringComparer.CurrentCultureIgnoreCase), true));
                }
                foreach (ProcessSample process in snapshot.Processes.Where(p => found.ContainsKey(p.Path)))
                {
                    ProcessSample owner = AppIdentityRules.Owner(process, byPid);
                    if (!string.Equals(owner.Path, Path, StringComparison.OrdinalIgnoreCase)) found[process.Path].Others.Add(Loc.AppName(owner.DisplayName));
                }
            }
            foreach (string saved in loadedPolicy.Helpers)
            {
                found.TryAdd(saved, (BlockedLogView.AppName(saved), new HashSet<string>(StringComparer.CurrentCultureIgnoreCase), false));
            }
            if (found.Count == 0) return null;

            bool nothingSaved = loadedPolicy.IsEmpty && !loadedBlocked;
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(SettingRow(Loc.Get("RulesPage_Helpers"), Loc.Get("RulesPage_HelpersNote"), new Border()));
            foreach (var (helperPath, (name, others, running)) in found.OrderBy(pair => pair.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                bool runtime = helperPath.Contains(@"\Microsoft\EdgeWebView\", StringComparison.OrdinalIgnoreCase);
                bool shared = runtime || others.Count > 0;
                bool saved = loadedPolicy.Helpers.Contains(helperPath, StringComparer.OrdinalIgnoreCase);
                var chips = new List<(string, string)>();
                if (others.Count > 0) chips.Add((Loc.F("RulesPage_HelperSharedBy", string.Join(", ", others.Order())), "Warn"));
                else if (runtime) chips.Add((Loc.Get("RulesPage_HelperRuntime"), "Warn"));
                else chips.Add((Loc.Get("RulesPage_HelperOwn"), "Good"));
                if (!running) chips.Add((Loc.Get(File.Exists(helperPath) ? "RulesPage_HelperNotRunning" : "RulesPage_HelperMissing"), "Idle"));

                var texts = new StackPanel { Spacing = 3 };
                var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                title.Children.Add(Text(name, 13, strong: true));
                title.Children.Add(Ui.Chips(chips));
                texts.Children.Add(title);
                texts.Children.Add(Note(helperPath));
                if (shared) texts.Children.Add(Note(Loc.Get(runtime ? "RulesPage_HelperRuntimeNote" : "RulesPage_HelperSharedNote")));
                var box = new CheckBox { Content = texts, IsChecked = saved || (nothingSaved && !shared) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, name);
                box.Checked += (_, _) => Validate();
                box.Unchecked += (_, _) => Validate();
                helperBoxes.Add((helperPath, box));
                panel.Children.Add(box);
            }
            var card = new Border { Child = panel, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeBrush");
            return card;
        }

        private static string Key(AppMemoryRule rule) => rule.LimitMegabytes + "|" + rule.Priority;

        private void Validate()
        {
            var messages = new List<string>();
            outgoing.Collect(messages);
            incoming.Collect(messages);
            limit.IsEnabled = limitOn.IsOn;
            if (limitOn.IsOn && (double.IsNaN(limit.Value) || limit.Value < AppMemoryRule.MinimumMegabytes)) messages.Add(Loc.F("RulesPage_LimitTooLow", AppMemoryRule.MinimumMegabytes));
            // A whole-app block makes the switches and rules irrelevant until it is lifted; say so instead of hiding them.
            networkDetails.Opacity = blockAll.IsOn ? 0.5 : 1;
            errors.Text = string.Join("\n", messages.Take(6));
            errors.Visibility = messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool dirty = IsDirty;
            save.IsEnabled = messages.Count == 0 && dirty && !Saving;
            discard.IsEnabled = dirty && !Saving;
            removeAll.IsEnabled = HasAnything && !Saving;
            if (!Saving) status.Text = dirty ? Loc.Get(App.Monitor.IsElevated ? "RulesPage_Unsaved" : "RulesPage_UnsavedUac") : "";
        }

        private async Task SaveAsync(bool remove)
        {
            if (remove)
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = page.XamlRoot,
                    Title = Loc.F("RulesPage_RemoveTitle", Name),
                    Content = Loc.Get("RulesPage_RemoveText"),
                    PrimaryButtonText = Loc.Get("Rules_RemoveAll"),
                    CloseButtonText = Loc.Get("Common_Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }
            AppNetworkPolicy policy = remove ? new AppNetworkPolicy { Path = Path } : CurrentPolicy();
            AppMemoryRule memory = remove ? new AppMemoryRule { Path = Path } : CurrentMemory();
            bool blocked = !remove && blockAll.IsOn;
            var change = new RuleChange
            {
                Path = Path,
                Blocked = blocked != loadedBlocked ? blocked : null,
                Network = Key(policy) != storedNetworkKey || (remove && !loadedPolicy.IsEmpty) ? policy : null,
                Memory = Key(memory) != loadedMemoryKey ? memory : null
            };
            Saving = true;
            status.Text = Loc.Get("RulesPage_Saving");
            Validate();
            string? error = await App.Monitor.ApplyRulesAsync(change);
            Saving = false;
            if (error is null)
            {
                // The page reloads this app from the saved state (FirewallChanged), which also clears the dirty state.
                page.Open(Path);
                page.RebuildList();
                if (page.editor is { } fresh) fresh.status.Text = Loc.Get(remove ? "RulesPage_Removed" : "RulesPage_Saved");
            }
            else
            {
                Validate();
                status.Text = Loc.Get("Rules_NotSaved") + ": " + error;
            }
        }
    }
}
