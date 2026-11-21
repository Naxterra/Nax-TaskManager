using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// The card on the right of a table page that describes the selected row: icon and name on top, two mini tiles with
/// sparklines, a list of facts (label and value, some with a copy button) and the actions as full-width buttons.
/// Pages fill it from their selection on every refresh; while nothing is selected it shows a hint.
/// </summary>
public sealed class DetailPane : Grid
{
    private readonly Image icon = new() { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock subtitle = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock detail = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid tiles = new() { ColumnSpacing = 8 };
    private readonly Grid facts = new() { ColumnSpacing = 12 };
    private readonly StackPanel actions = new() { Spacing = 8 };
    private readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
    private readonly StackPanel content = new() { Spacing = 14 };
    private readonly Dictionary<string, FactRow> factRows = new();
    private readonly List<string> factOrder = new();
    private readonly HashSet<string> activeCopyable = new();

    private sealed class FactRow
    {
        public required TextBlock Label { get; init; }
        public required TextBlock Value { get; init; }
        public required int RowIndex { get; init; }
        public Button? Copy { get; set; }
    }

    public DetailPane()
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(18);
        Tile1 = new StatTile { MinWidth = 0, Stacked = true };
        Tile2 = new StatTile { MinWidth = 0, Stacked = true };
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(Tile2, 1);
        tiles.Children.Add(Tile1);
        tiles.Children.Add(Tile2);

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        texts.Children.Add(detail);
        Grid.SetColumn(texts, 1);
        header.Children.Add(texts);

        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(header);
        content.Children.Add(tiles);
        content.Children.Add(facts);
        content.Children.Add(actions);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        Children.Add(scroll);
                var emptyIcon = new FontIcon { Glyph = "\uE8A0", FontSize = 30, Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,16) };
        var emptyPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 230 };
        emptyPanel.Children.Add(emptyIcon);
        emptyPanel.Children.Add(empty);
        Children.Add(emptyPanel);
        emptyPanel.SetBinding(VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding { Source = empty, Path = new PropertyPath("Visibility"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        ApplyResources();
    }

    public StatTile Tile1 { get; }
    public StatTile Tile2 { get; }

    /// <summary>Hides the content and shows a hint instead, for an empty selection.</summary>
    public void ShowEmpty(string text)
    {
        empty.Text = text;
        empty.Visibility = Visibility.Visible;
        content.Visibility = Visibility.Collapsed;
    }

    public void SetHeader(ImageSource? image, string titleText, string subtitleText, string detailText)
    {
        empty.Visibility = Visibility.Collapsed;
        content.Visibility = Visibility.Visible;
        icon.Source = image;
        icon.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        title.Text = titleText;
        subtitle.Text = subtitleText;
        subtitle.Visibility = subtitleText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        detail.Text = detailText;
        detail.Visibility = detailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTilesVisible(bool visible) => tiles.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Starts a new set of facts; call <see cref="Fact"/> for each, then <see cref="EndFacts"/>.</summary>
    public void BeginFacts()
    {
        factOrder.Clear();
        activeCopyable.Clear();
    }

    public void Fact(string label, string value, bool copyable = false)
    {
        if (!factOrder.Contains(label)) factOrder.Add(label);
        if (copyable) activeCopyable.Add(label);
        if (!factRows.TryGetValue(label, out FactRow? row))
        {
            var labelBlock = new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var valueBlock = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Top };
            int rowIndex = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(labelBlock, rowIndex);
            Grid.SetRow(valueBlock, rowIndex);
            Grid.SetColumn(valueBlock, 1);
            facts.Children.Add(labelBlock);
            facts.Children.Add(valueBlock);
            row = new FactRow { Label = labelBlock, Value = valueBlock, RowIndex = rowIndex };
            factRows[label] = row;
        }
        if (copyable && row.Copy is null)
        {
            var copy = new Button { Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 }, Padding = new Thickness(4), MinWidth = 0, Height = 26, Width = 28, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(copy, Loc.Get("Details_Copy"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, Loc.Get("Details_Copy"));
            TextBlock target = row.Value;
            copy.Click += (_, _) => ProcessActions.CopyText(target.Text);
            Grid.SetRow(copy, row.RowIndex);
            Grid.SetColumn(copy, 2);
            facts.Children.Add(copy);
            row.Copy = copy;
        }
        if (row.Value.Text != value) row.Value.Text = value;
    }

    /// <summary>Hides facts that this round did not set, so a different kind of selection shows only its own.</summary>
    public void EndFacts()
    {
        var active = new HashSet<string>(factOrder);
        bool firstVisible = true;
        foreach (var (label, row) in factRows)
        {
            bool visible = active.Contains(label);
            Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;
            Thickness margin = visible && !firstVisible ? new Thickness(0, 8, 0, 0) : new Thickness(0);
            if (visible) firstVisible = false;
            row.Label.Visibility = state;
            row.Label.Margin = margin;
            row.Value.Visibility = state;
            row.Value.Margin = margin;
            if (row.Copy is not null)
            {
                row.Copy.Visibility = visible && activeCopyable.Contains(label) ? Visibility.Visible : Visibility.Collapsed;
                row.Copy.Margin = margin;
            }
        }
    }

    public void SetActions(Button[] buttons)
    {
        actions.Children.Clear();
        foreach (Button button in buttons)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            actions.Children.Add(button);
        }
    }

    /// <summary>A full-width action button with an icon and a label.</summary>
    public static Button ActionButton(string glyph, string text, RoutedEventHandler click)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, label } },
            Padding = new Thickness(12, 7, 12, 7),
            Tag = label
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        button.Click += click;
        return button;
    }

    /// <summary>Changes the label (and automation name) of a button made by <see cref="ActionButton"/>.</summary>
    public static void SetActionText(Button button, string text)
    {
        if (button.Tag is TextBlock label) label.Text = text;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
    }

    /// <summary>High-contrast red for the destructive action (End task), in every pointer state.</summary>
    public static void MakeDanger(Button button)
    {
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xB9, 0x1C, 0x1C));
        var hover = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x99, 0x1B, 0x1B));
        var pressed = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x7F, 0x1D, 0x1D));
        var white = new SolidColorBrush(Microsoft.UI.Colors.White);
        button.Background = red;
        button.Foreground = white;
        button.BorderBrush = red;
        button.Resources["ButtonBackground"] = red;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = pressed;
        button.Resources["ButtonForeground"] = white;
        button.Resources["ButtonForegroundPointerOver"] = white;
        button.Resources["ButtonForegroundPressed"] = white;
        button.Resources["ButtonBorderBrush"] = red;
        button.Resources["ButtonBorderBrushPointerOver"] = hover;
        button.Resources["ButtonBorderBrushPressed"] = pressed;
    }

    private void ApplyResources()
    {
        ResourceDictionary resources = Application.Current.Resources;
        this.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        this.SetResourceReference(BorderBrushProperty, "SurfaceStrokeBrush");
        title.Style = (Style)resources["SubtitleTextBlockStyle"];
        subtitle.Style = (Style)resources["CaptionTextBlockStyle"];
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        detail.Style = (Style)resources["CaptionTextBlockStyle"];
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
    }
}
