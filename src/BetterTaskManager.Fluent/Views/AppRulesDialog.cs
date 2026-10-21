using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Edits one app's network rules: four switches that block whole scopes, and an outgoing and an incoming rule list
/// where the first matching line decides. Used from the Network and Processes pages.
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

        var panel = new StackPanel { Spacing = 8, MinWidth = 560 };
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

        TextBox outgoing = RuleBox(policy.Outgoing, "+ LAN\n- Internet");
        TextBox incoming = RuleBox(policy.Incoming, "+ LAN TCP/8080\n- *");
        var presets = new MenuFlyout();
        foreach ((string key, string rules) in OutgoingPresets)
        {
            var item = new MenuFlyoutItem { Text = Loc.Get(key) };
            item.Click += (_, _) => outgoing.Text = rules.Replace("\n", "\r");
            presets.Items.Add(item);
        }
        var outgoingHeader = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        outgoingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outgoingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock outgoingTitle = Heading(Loc.Get("Rules_OutgoingTitle"));
        outgoingTitle.Margin = new Thickness(0);
        outgoingTitle.VerticalAlignment = VerticalAlignment.Center;
        outgoingHeader.Children.Add(outgoingTitle);
        var presetButton = new DropDownButton { Content = Loc.Get("Rules_Presets"), Flyout = presets };
        Grid.SetColumn(presetButton, 1);
        outgoingHeader.Children.Add(presetButton);
        panel.Children.Add(outgoingHeader);
        panel.Children.Add(outgoing);
        panel.Children.Add(Heading(Loc.Get("Rules_IncomingTitle")));
        panel.Children.Add(incoming);

        // Outside the scrolling part, so a mistake is visible wherever the list is scrolled.
        var errors = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("SystemFillColorCriticalBrush"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Rules_HelpTitle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = Loc.Get("Rules_Help"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
        });
        if (!App.Monitor.IsElevated) panel.Children.Add(Muted(Loc.Get("Rules_UacNote")));

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.F("Rules_Title", name),
            Content = Layout(new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) }, errors),
            PrimaryButtonText = Loc.Get("Rules_Save"),
            SecondaryButtonText = hadRules ? Loc.Get("Rules_RemoveAll") : "",
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;

        void Validate()
        {
            var messages = new List<string>();
            Collect(outgoing.Text, "Rules_ErrLineOut", messages);
            Collect(incoming.Text, "Rules_ErrLineIn", messages);
            errors.Text = string.Join("\n", messages.Take(6));
            errors.Visibility = messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = messages.Count == 0;
        }
        outgoing.TextChanged += (_, _) => Validate();
        incoming.TextChanged += (_, _) => Validate();
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
            changed.Outgoing = Lines(outgoing.Text);
            changed.Incoming = Lines(incoming.Text);
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

    /// <summary>Rule lines without blanks and comment-only lines, kept in their order.</summary>
    private static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static void Collect(string text, string lineKey, List<string> messages)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (NetworkRule.TryParse(line, out _, out RuleParseError error, out string token)) continue;
            messages.Add(Loc.F(lineKey, index + 1, Loc.F("Rules_Err_" + error, token)));
        }
    }

    private static TextBox RuleBox(List<string> rules, string placeholder) => new()
    {
        Text = string.Join("\r", rules),
        PlaceholderText = placeholder,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        Height = 120,
        IsSpellCheckEnabled = false
    };

    private static CheckBox Switch(string text, bool on) => new() { Content = text, IsChecked = on };

    private static TextBlock Heading(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) };

    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorSecondaryBrush") };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
