using System.Diagnostics;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Every app that Nax-TaskManager restricts, in one list: whole-app blocks and per-app rules, with what each one does
/// and buttons to edit or remove it. The list is read fresh each time the dialog opens.
/// </summary>
internal static class RulesOverviewDialog
{
    private sealed record Entry(string Path, string Name, bool Blocked, AppNetworkPolicy? Policy);

    public static async Task ShowAsync(XamlRoot root)
    {
        while (true)
        {
            await App.Monitor.RefreshFirewallAsync();
            List<Entry> entries = Collect();
            string? edit = null;
            string? unblock = null;
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = Loc.F("RulesList_Title", entries.Count),
                CloseButtonText = Loc.Get("Common_OK"),
                DefaultButton = ContentDialogButton.Close
            };
            dialog.Resources["ContentDialogMaxWidth"] = 860.0;

            var list = new StackPanel { Spacing = 10, MinWidth = 640 };
            list.Children.Add(Muted(App.Monitor.FirewallStateKnown ? Loc.Get("RulesList_Intro") : Loc.Get("Workflow_PolicyUnknown")));
            if (entries.Count == 0) list.Children.Add(Muted(Loc.Get("RulesList_Empty")));
            foreach (Entry entry in entries)
            {
                list.Children.Add(Card(entry,
                    () => { edit = entry.Path; dialog.Hide(); },
                    () => { unblock = entry.Path; dialog.Hide(); }));
            }
            dialog.Content = new ScrollViewer { Content = list, MaxHeight = 640, Padding = new Thickness(0, 0, 16, 0) };
            await dialog.ShowAsync();

            // ContentDialogs cannot stack: the overview closes, the action runs, then the overview opens again.
            if (edit is not null)
            {
                Entry entry = entries.First(e => e.Path == edit);
                await AppRulesDialog.ShowAsync(root, entry.Path, entry.Name);
                continue;
            }
            if (unblock is not null)
            {
                string? error = await App.Monitor.SetBlockedAsync(unblock, false);
                if (error is not null)
                {
                    await new ContentDialog { XamlRoot = root, Title = Loc.Get("Firewall_NotChanged"), Content = error, CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
                }
                continue;
            }
            return;
        }
    }

    private static List<Entry> Collect()
    {
        var paths = new HashSet<string>(App.Monitor.BlockedPaths, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(App.Monitor.Policies.Keys);
        return paths
            .Select(path => new Entry(path, DisplayName(path), App.Monitor.BlockedPaths.Contains(path), App.Monitor.Policies.GetValueOrDefault(path)))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The file description (as Task Manager shows it), else the file name.</summary>
    private static string DisplayName(string path)
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

    private static Border Card(Entry entry, Action edit, Action unblock)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = IconCache.Get(entry.Path), Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top });

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = entry.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(Muted(entry.Path));
        if (!File.Exists(entry.Path)) text.Children.Add(Colored(Loc.Get("RulesList_Missing"), "StateWarnBrush"));
        if (entry.Blocked) text.Children.Add(Colored(Loc.Get("RulesList_Blocked"), "StateBadBrush"));
        if (entry.Policy is { } policy)
        {
            var switches = new List<string>();
            if (policy.BlockInternet) switches.Add(Loc.Get("Rules_BlockInternet"));
            if (policy.BlockLan) switches.Add(Loc.Get("Rules_BlockLan"));
            if (policy.BlockLocalhost) switches.Add(Loc.Get("Rules_BlockLocalhost"));
            if (policy.BlockInbound) switches.Add(Loc.Get("Rules_BlockInbound"));
            if (switches.Count > 0) text.Children.Add(Colored(string.Join(" · ", switches), "StateWarnBrush"));
            AddLines(text, Loc.Get("RulesList_Outgoing"), policy.Outgoing);
            AddLines(text, Loc.Get("RulesList_Incoming"), policy.Incoming);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        var editButton = new Button { Content = Loc.Get("RulesList_Edit"), HorizontalAlignment = HorizontalAlignment.Stretch };
        editButton.Click += (_, _) => edit();
        buttons.Children.Add(editButton);
        if (entry.Blocked)
        {
            var unblockButton = new Button { Content = Loc.Get("Firewall_AllowShort"), HorizontalAlignment = HorizontalAlignment.Stretch };
            unblockButton.Click += (_, _) => unblock();
            buttons.Children.Add(unblockButton);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        };
    }

    private static void AddLines(StackPanel panel, string title, List<string> lines)
    {
        if (lines.Count == 0) return;
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 4, 0, 0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("\n", lines.Select((line, index) => $"{index + 1}.  {line}")),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    private static TextBlock Colored(string text, string brush) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources[brush]
    };
}
