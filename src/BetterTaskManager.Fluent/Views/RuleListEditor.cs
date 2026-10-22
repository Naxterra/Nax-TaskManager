using System.Globalization;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// One direction's network rules on the Rules page: a list of editable rows, or the same rules as text in the
/// advanced view (one rule per line). The rows follow the width they get: wide, every field sits in one line under
/// column labels; narrow, the comment moves to a second line.
/// </summary>
internal sealed class RuleListEditor
{
    private const double NarrowWidth = 760;

    private readonly List<RuleRow> rows;
    private readonly string lineKey;
    private readonly StackPanel rowPanel = new() { Spacing = 6 };
    private readonly Grid labels = new() { ColumnSpacing = 6, Margin = new Thickness(2, 4, 0, 0) };
    private readonly TextBox text;
    private readonly TextBlock empty = Muted(Loc.Get("RulesPage_NoRules"));
    private readonly ToggleButton textMode = new() { Content = Loc.Get("Rules_TextMode") };
    private bool suppress, switching, narrow;

    public RuleListEditor(IEnumerable<string> initial, string lineKey, bool inbound)
    {
        rows = initial.Select(RuleRow.FromLine).OfType<RuleRow>().ToList();
        this.lineKey = lineKey;
        text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            MinHeight = 120,
            IsSpellCheckEnabled = false,
            Visibility = Visibility.Collapsed,
            PlaceholderText = inbound ? "+ LAN TCP/8080\r- *" : "+ LAN\r- Internet"
        };
    }

    public Action? Changed { get; set; }

    /// <summary>True while the text view is shown; then the text is the source of truth.</summary>
    private bool InTextMode => textMode.IsChecked == true;

    public FrameworkElement Build(string title, string note, FrameworkElement? extra)
    {
        var heading = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap };
        var add = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "", FontSize = 12 }, new TextBlock { Text = Loc.Get("Rules_AddRule") } } },
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, Loc.Get("Rules_AddRule"));
        add.Click += (_, _) => { rows.Add(new RuleRow()); RenderRows(); OnChanged(); };
        // Buttons wrap below each other only when the space really runs out.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(add);
        if (extra is not null) buttons.Children.Add(extra);
        buttons.Children.Add(textMode);
        // Checked and Unchecked, not Click: UI Automation and the keyboard toggle through those.
        textMode.Checked += (_, _) => { if (!switching) SwitchMode(); };
        textMode.Unchecked += (_, _) => { if (!switching) SwitchMode(); };

        text.TextChanged += (_, _) => { if (!suppress && InTextMode) OnChanged(); };
        rowPanel.SizeChanged += (_, args) =>
        {
            bool now = args.NewSize.Width < NarrowWidth;
            if (now == narrow) return;
            narrow = now;
            RenderRows();
        };
        BuildLabels();
        RenderRows();
        return new StackPanel { Spacing = 8, Children = { heading, Muted(note), buttons, empty, labels, rowPanel, text } };
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
        }
        text.Visibility = InTextMode ? Visibility.Visible : Visibility.Collapsed;
        rowPanel.Visibility = InTextMode ? Visibility.Collapsed : Visibility.Visible;
        textMode.Content = Loc.Get(InTextMode ? "Rules_RowMode" : "Rules_TextMode");
        RenderRows();
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke();

    private void RenderRows()
    {
        rowPanel.Children.Clear();
        for (int index = 0; index < rows.Count; index++) rowPanel.Children.Add(BuildRow(rows[index], index));
        empty.Visibility = !InTextMode && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        labels.Visibility = !InTextMode && rows.Count > 0 && !narrow ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Action, target, protocol, ports, comment, then three icon buttons; narrow rows leave out the comment column.</summary>
    private void AddColumns(Grid grid, bool withComment)
    {
        // Star widths share the room; the minimums keep "Erlauben", "Lokales Netzwerk (LAN)" and a port range readable.
        (double Stars, double Min)[] columns = [(1.1, 120), (2, 150), (1, 96), (1.1, 110), (1.6, 100)];
        foreach ((double stars, double min) in withComment ? columns : columns[..4])
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stars, GridUnitType.Star), MinWidth = min });
        }
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
    }

    private void BuildLabels()
    {
        AddColumns(labels, withComment: true);
        string[] keys = ["RulesPage_ColAction", "RulesPage_ColTarget", "RulesPage_ColProtocol", "RulesPage_ColPorts", "RulesPage_ColComment"];
        for (int i = 0; i < keys.Length; i++)
        {
            var label = new TextBlock { Text = Loc.Get(keys[i]), FontSize = 11, Foreground = Brush("TextFillColorSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, i);
            labels.Children.Add(label);
        }
    }

    private Grid BuildRow(RuleRow row, int index)
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        AddColumns(grid, withComment: !narrow);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (narrow) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int column = 0;
        void Place(FrameworkElement element)
        {
            Grid.SetColumn(element, column++);
            grid.Children.Add(element);
        }

        var action = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = -1 };
        action.Items.Add(Loc.Get("Rules_Allow"));
        action.Items.Add(Loc.Get("Rules_Block"));
        action.SelectedIndex = row.Allow ? 0 : 1;
        action.SelectionChanged += (_, _) => { row.Allow = action.SelectedIndex == 0; OnChanged(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, Loc.Get("RulesPage_ColAction"));
        Place(action);

        // The target is a scope from the list, or an address or range typed into the same box.
        var target = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, PlaceholderText = Loc.Get("Rules_AddressPlaceholder") };
        foreach (string key in new[] { "Rules_TargetAny", "Rules_TargetLan", "Rules_TargetInternet", "Rules_TargetLocalhost" }) target.Items.Add(Loc.Get(key));
        if (row.Target == RuleTargetKind.Address) target.Text = row.Address;
        else target.SelectedIndex = row.Target switch { RuleTargetKind.Any => 0, RuleTargetKind.Lan => 1, RuleTargetKind.Internet => 2, _ => 3 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target, Loc.Get("RulesPage_ColTarget"));
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(protocol, Loc.Get("RulesPage_ColProtocol"));
        Place(protocol);

        var ports = new TextBox { Text = row.Ports, PlaceholderText = Loc.Get("Rules_PortsPlaceholder"), IsSpellCheckEnabled = false };
        ports.TextChanged += (_, _) => { row.Ports = ports.Text.Trim(); OnChanged(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ports, Loc.Get("RulesPage_ColPorts"));
        Place(ports);

        var comment = new TextBox { Text = row.Comment, PlaceholderText = Loc.Get("Rules_CommentPlaceholder") };
        comment.TextChanged += (_, _) => { row.Comment = comment.Text.Trim(); OnChanged(); };
        if (narrow)
        {
            Grid.SetRow(comment, 1);
            Grid.SetColumnSpan(comment, 4);
            grid.Children.Add(comment);
        }
        else Place(comment);

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

    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorSecondaryBrush") };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

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
