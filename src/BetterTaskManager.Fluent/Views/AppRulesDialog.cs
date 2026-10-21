using System.Globalization;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Edits one app's network rules: four switches that block whole scopes, and an outgoing and an incoming rule list
/// where the first matching line decides. Each rule is a row of dropdowns and fields; the text form (one rule per
/// line) stays available as an advanced view. Used from the Network and Processes pages.
/// </summary>
internal static class AppRulesDialog
{
    private static readonly (string Key, string Rules)[] OutgoingPresets =
    [
        ("Rules_PresetWeb", "+ Localhost\n+ * TCP/80\n+ * TCP/443\n+ * UDP/443 # HTTP/3\n- *"),
        ("Rules_PresetLan", "+ Localhost\n+ LAN\n- *"),
        ("Rules_PresetLocal", "+ Localhost\n- *")
    ];

    public static async Task ShowAsync(XamlRoot root, string path, string name)
    {
        AppNetworkPolicy policy = App.Monitor.PolicyFor(path);
        bool hadRules = App.Monitor.HasRules(path);

        var panel = new StackPanel { Spacing = 8, MinWidth = 700 };
        panel.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = Brush("TextFillColorSecondaryBrush") });
        if (App.Monitor.IsBlocked(path))
        {
            panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = Loc.Get("Rules_BlockedNote") });
        }

        panel.Children.Add(Heading(Loc.Get("Rules_SwitchesTitle")));
        CheckBox internet = Switch(Loc.Get("Rules_BlockInternet"), policy.BlockInternet);
        CheckBox lan = Switch(Loc.Get("Rules_BlockLan"), policy.BlockLan);
        CheckBox localhost = Switch(Loc.Get("Rules_BlockLocalhost"), policy.BlockLocalhost);
        CheckBox inbound = Switch(Loc.Get("Rules_BlockInbound"), policy.BlockInbound);
        foreach (CheckBox box in new[] { internet, lan, localhost, inbound }) panel.Children.Add(box);
        panel.Children.Add(Muted(Loc.Get("Rules_SwitchesNote")));

        var errors = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("SystemFillColorCriticalBrush"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.F("Rules_Title", name),
            PrimaryButtonText = Loc.Get("Rules_Save"),
            SecondaryButtonText = hadRules ? Loc.Get("Rules_RemoveAll") : "",
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;

        var outgoing = new RuleListEditor(policy.Outgoing, "Rules_ErrLineOut", inbound: false);
        var incoming = new RuleListEditor(policy.Incoming, "Rules_ErrLineIn", inbound: true);
        void Validate()
        {
            var messages = new List<string>();
            outgoing.Collect(messages);
            incoming.Collect(messages);
            errors.Text = string.Join("\n", messages.Take(6));
            errors.Visibility = messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = messages.Count == 0;
        }
        outgoing.Changed = Validate;
        incoming.Changed = Validate;

        var presets = new MenuFlyout();
        foreach ((string key, string rules) in OutgoingPresets)
        {
            var item = new MenuFlyoutItem { Text = Loc.Get(key) };
            item.Click += (_, _) => outgoing.SetLines(rules.Split('\n'));
            presets.Items.Add(item);
        }
        panel.Children.Add(outgoing.Build(Loc.Get("Rules_OutgoingTitle"), new DropDownButton { Content = Loc.Get("Rules_Presets"), Flyout = presets }));
        panel.Children.Add(incoming.Build(Loc.Get("Rules_IncomingTitle"), null));

        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Rules_HelpTitle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = Loc.Get("Rules_Help"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
        });
        if (!App.Monitor.IsElevated) panel.Children.Add(Muted(Loc.Get("Rules_UacNote")));

        dialog.Content = Layout(new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) }, errors);
        Validate();

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        var changed = new AppNetworkPolicy { Path = path };
        if (result == ContentDialogResult.Primary)
        {
            changed.BlockInternet = internet.IsChecked == true;
            changed.BlockLan = lan.IsChecked == true;
            changed.BlockLocalhost = localhost.IsChecked == true;
            changed.BlockInbound = inbound.IsChecked == true;
            changed.Outgoing = outgoing.Lines();
            changed.Incoming = incoming.Lines();
            if (changed.IsEmpty && !hadRules) return;
        }

        string? error = await App.Monitor.SetPolicyAsync(changed);
        if (error is not null)
        {
            await new ContentDialog { XamlRoot = root, Title = Loc.Get("Rules_NotSaved"), Content = error, CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
        }
    }

    /// <summary>The rules scroll in the space the dialog has; the error text below them always keeps its own room.</summary>
    private static Grid Layout(ScrollViewer rules, TextBlock errors)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(errors, 1);
        grid.Children.Add(rules);
        grid.Children.Add(errors);
        return grid;
    }

    private static CheckBox Switch(string text, bool on) => new() { Content = text, IsChecked = on };

    private static TextBlock Heading(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) };

    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorSecondaryBrush") };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>One direction's rules: a list of editable rows, or the same rules as text in the advanced view.</summary>
    private sealed class RuleListEditor(List<string> initial, string lineKey, bool inbound)
    {
        private readonly List<RuleRow> rows = initial.Select(RuleRow.FromLine).OfType<RuleRow>().ToList();
        private readonly StackPanel rowPanel = new() { Spacing = 6 };
        private readonly TextBox text = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Height = 120,
            IsSpellCheckEnabled = false,
            Visibility = Visibility.Collapsed,
            PlaceholderText = inbound ? "+ LAN TCP/8080\r- *" : "+ LAN\r- Internet"
        };
        private readonly TextBlock empty = new() { Text = Loc.Get("Rules_NoRules"), TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorSecondaryBrush") };
        private readonly ToggleButton textMode = new() { Content = Loc.Get("Rules_TextMode") };
        private bool suppress, switching;

        public Action? Changed { get; set; }

        /// <summary>True while the text view is shown; then the text is the source of truth.</summary>
        private bool InTextMode => textMode.IsChecked == true;

        public FrameworkElement Build(string title, FrameworkElement? extra)
        {
            var header = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = Heading(title);
            heading.Margin = new Thickness(0);
            heading.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(heading);
            var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "", FontSize = 12 }, new TextBlock { Text = Loc.Get("Rules_AddRule") } } } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, Loc.Get("Rules_AddRule"));
            add.Click += (_, _) => { rows.Add(new RuleRow()); RenderRows(); OnChanged(); };
            Grid.SetColumn(add, 1);
            header.Children.Add(add);
            if (extra is not null)
            {
                Grid.SetColumn(extra, 2);
                header.Children.Add(extra);
            }
            Grid.SetColumn(textMode, 3);
            // Checked and Unchecked, not Click: UI Automation and the keyboard toggle through those.
            textMode.Checked += (_, _) => { if (!switching) SwitchMode(); };
            textMode.Unchecked += (_, _) => { if (!switching) SwitchMode(); };
            header.Children.Add(textMode);

            text.TextChanged += (_, _) => { if (!suppress && InTextMode) OnChanged(); };
            RenderRows();
            return new StackPanel { Spacing = 6, Children = { header, empty, rowPanel, text } };
        }

        /// <summary>Replaces the rules (presets); in text mode the text is replaced, otherwise the rows.</summary>
        public void SetLines(IEnumerable<string> lines)
        {
            if (InTextMode)
            {
                text.Text = string.Join("\r", lines);
                return;
            }
            rows.Clear();
            rows.AddRange(lines.Select(RuleRow.FromLine).OfType<RuleRow>());
            RenderRows();
            OnChanged();
        }

        /// <summary>The rule lines as they will be saved.</summary>
        public List<string> Lines() => InTextMode ? TextLines().Where(line => line.Length > 0 && !line.StartsWith('#')).ToList() : rows.Select(row => row.ToLine()).ToList();

        private List<string> TextLines() => text.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.Trim()).ToList();

        /// <summary>Adds one message per invalid rule, with the rule's 1-based number.</summary>
        public void Collect(List<string> messages)
        {
            List<string> lines = InTextMode ? TextLines() : rows.Select(row => row.ToLine()).ToList();
            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index];
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (NetworkRule.TryParse(line, out _, out RuleParseError error, out string token)) continue;
                messages.Add(Loc.F(lineKey, index + 1, Loc.F("Rules_Err_" + error, token)));
            }
        }

        private void SwitchMode()
        {
            if (InTextMode)
            {
                // Rows to text: every row has a textual form, even an incomplete one.
                suppress = true;
                text.Text = string.Join("\r", rows.Select(row => row.ToLine()));
                suppress = false;
            }
            else
            {
                // Text to rows: only possible when every line parses; otherwise stay in text mode and say so.
                // The toggle has already flipped here, so the text is read directly, not through Lines().
                var parsed = new List<RuleRow>();
                foreach (string line in TextLines().Where(line => line.Length > 0 && !line.StartsWith('#')))
                {
                    if (RuleRow.FromLine(line) is { } row) parsed.Add(row);
                    else
                    {
                        switching = true;
                        textMode.IsChecked = true;
                        switching = false;
                        OnChanged();
                        return;
                    }
                }
                rows.Clear();
                rows.AddRange(parsed);
                RenderRows();
            }
            text.Visibility = InTextMode ? Visibility.Visible : Visibility.Collapsed;
            rowPanel.Visibility = InTextMode ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = !InTextMode && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            textMode.Content = Loc.Get(InTextMode ? "Rules_RowMode" : "Rules_TextMode");
            OnChanged();
        }

        private void OnChanged() => Changed?.Invoke();

        private void RenderRows()
        {
            rowPanel.Children.Clear();
            for (int index = 0; index < rows.Count; index++) rowPanel.Children.Add(BuildRow(rows[index], index));
            empty.Visibility = !InTextMode && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Grid BuildRow(RuleRow row, int index)
        {
            var grid = new Grid { ColumnSpacing = 6 };
            foreach (double width in new[] { 112, 210, 118, 132, 0, 34, 34, 34 })
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            }
            int column = 0;
            void Place(FrameworkElement element)
            {
                Grid.SetColumn(element, column++);
                grid.Children.Add(element);
            }

            var action = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = row.Allow ? 0 : 1 };
            action.Items.Add(Loc.Get("Rules_Allow"));
            action.Items.Add(Loc.Get("Rules_Block"));
            action.SelectionChanged += (_, _) => { row.Allow = action.SelectedIndex == 0; OnChanged(); };
            Place(action);

            // The target is a scope from the list, or an address or range typed into the same box.
            var target = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, PlaceholderText = Loc.Get("Rules_AddressPlaceholder") };
            foreach (string key in new[] { "Rules_TargetAny", "Rules_TargetLan", "Rules_TargetInternet", "Rules_TargetLocalhost" }) target.Items.Add(Loc.Get(key));
            if (row.Target == RuleTargetKind.Address) target.Text = row.Address;
            else target.SelectedIndex = row.Target switch { RuleTargetKind.Any => 0, RuleTargetKind.Lan => 1, RuleTargetKind.Internet => 2, _ => 3 };
            Place(target);
            target.SelectionChanged += (_, _) =>
            {
                if (target.SelectedIndex < 0) return;
                row.Target = target.SelectedIndex switch { 0 => RuleTargetKind.Any, 1 => RuleTargetKind.Lan, 2 => RuleTargetKind.Internet, _ => RuleTargetKind.Localhost };
                OnChanged();
            };
            target.TextSubmitted += (_, args) =>
            {
                int known = target.Items.IndexOf(args.Text);
                if (known >= 0) target.SelectedIndex = known;
                else
                {
                    row.Target = RuleTargetKind.Address;
                    row.Address = args.Text.Trim();
                    args.Handled = true;
                    OnChanged();
                }
            };

            var protocol = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, Text = row.Protocol };
            foreach (string option in new[] { Loc.Get("Rules_ProtoAny"), "TCP", "UDP", "ICMP" }) protocol.Items.Add(option);
            if (row.Protocol.Length == 0) protocol.SelectedIndex = 0;
            else if (protocol.Items.IndexOf(row.Protocol) is int found and >= 0) protocol.SelectedIndex = found;
            protocol.SelectionChanged += (_, _) => { row.Protocol = protocol.SelectedIndex <= 0 ? "" : (string)protocol.SelectedItem; OnChanged(); };
            protocol.TextSubmitted += (_, args) => { row.Protocol = args.Text.Trim(); OnChanged(); };
            Place(protocol);

            var ports = new TextBox { Text = row.Ports, PlaceholderText = Loc.Get("Rules_PortsPlaceholder"), IsSpellCheckEnabled = false };
            ports.TextChanged += (_, _) => { row.Ports = ports.Text.Trim(); OnChanged(); };
            Place(ports);

            var comment = new TextBox { Text = row.Comment, PlaceholderText = Loc.Get("Rules_CommentPlaceholder") };
            comment.TextChanged += (_, _) => { row.Comment = comment.Text.Trim(); OnChanged(); };
            Place(comment);

            Place(IconButton("", Loc.Get("Rules_MoveUp"), index > 0, () => Move(index, -1)));
            Place(IconButton("", Loc.Get("Rules_MoveDown"), index < rows.Count - 1, () => Move(index, 1)));
            Place(IconButton("", Loc.Get("Rules_Remove"), true, () => { rows.RemoveAt(index); RenderRows(); OnChanged(); }));
            return grid;
        }

        private void Move(int index, int delta)
        {
            (rows[index], rows[index + delta]) = (rows[index + delta], rows[index]);
            RenderRows();
            OnChanged();
        }

        private static Button IconButton(string glyph, string name, bool enabled, Action click)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13 }, Padding = new Thickness(0), Width = 34, Height = 32, IsEnabled = enabled };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => click();
            return button;
        }
    }

    /// <summary>A rule as the row editor holds it; <see cref="ToLine"/> gives the text form the engine parses.</summary>
    private sealed class RuleRow
    {
        public bool Allow { get; set; } = true;
        public RuleTargetKind Target { get; set; } = RuleTargetKind.Any;
        public string Address { get; set; } = "";
        /// <summary>Empty for any protocol, else TCP, UDP, ICMP or a number as typed.</summary>
        public string Protocol { get; set; } = "";
        public string Ports { get; set; } = "";
        public string Comment { get; set; } = "";

        public string ToLine()
        {
            string target = Target switch
            {
                RuleTargetKind.Any => "*",
                RuleTargetKind.Lan => "LAN",
                RuleTargetKind.Internet => "Internet",
                RuleTargetKind.Localhost => "Localhost",
                _ => Address.Length > 0 ? Address : "?"
            };
            string line = (Allow ? "+ " : "- ") + target;
            if (Protocol.Length > 0 || Ports.Length > 0)
            {
                line += " " + (Protocol.Length > 0 ? Protocol : "*");
                if (Ports.Length > 0) line += "/" + Ports;
            }
            return Comment.Length > 0 ? line + " # " + Comment : line;
        }

        public static RuleRow? FromLine(string line)
        {
            if (!NetworkRule.TryParse(line, out NetworkRule? rule, out _) || rule is null) return null;
            return new RuleRow
            {
                Allow = rule.Allow,
                Target = rule.Target,
                Address = rule.Address?.ToString() ?? "",
                Protocol = rule.Protocol switch
                {
                    NetworkRule.AnyProtocol => "",
                    NetworkRule.Tcp => "TCP",
                    NetworkRule.Udp => "UDP",
                    NetworkRule.Icmp => "ICMP",
                    _ => rule.Protocol.ToString(CultureInfo.InvariantCulture)
                },
                Ports = !rule.HasPorts ? "" : rule.PortFrom == rule.PortTo ? rule.PortFrom.ToString(CultureInfo.InvariantCulture) : $"{rule.PortFrom}-{rule.PortTo}",
                Comment = rule.Comment
            };
        }
    }
}
