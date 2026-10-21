using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// A small card with a caption, the current value, a one-line detail and a sparkline of the last 60 samples, for the
/// strip under a page title. The strip gives the page's numbers a face before the table starts.
/// </summary>
public sealed class StatTile : Grid
{
    private readonly TextBlock caption = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock value = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock detail = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid grid = new() { ColumnSpacing = 10 };
    private readonly StackPanel text = new() { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
    private readonly TrendChart chart = new() { Compact = true, Height = 44, Width = 96, VerticalAlignment = VerticalAlignment.Center };
    private readonly Rectangle edge = new() { Width = 3, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 2) };

    public StatTile()
    {
        Padding = new Thickness(12, 10, 12, 10);
        MinWidth = 190;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(edge);
        text.Children.Add(caption);
        text.Children.Add(value);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(chart, 2);
        grid.Children.Add(chart);
        Children.Add(grid);
        Loaded += (_, _) => ApplyResources();
        ActualThemeChanged += (_, _) => ApplyResources();
        // The sparkline takes about a third of the tile, so the numbers keep their room in narrow tiles.
        SizeChanged += (_, args) => { if (!stacked) chart.Width = Math.Clamp(args.NewSize.Width * 0.34, 56, 130); };
    }

    private bool stacked;

    /// <summary>Text above a full-width sparkline instead of beside a fixed one, for narrow places like the details card.</summary>
    public bool Stacked
    {
        get => stacked;
        set
        {
            stacked = value;
            if (value)
            {
                grid.ColumnDefinitions[2].Width = new GridLength(0);
                grid.RowDefinitions.Clear();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(chart, 1);
                Grid.SetRow(chart, 1);
                Grid.SetRowSpan(edge, 2);
                chart.Width = double.NaN;
                chart.HorizontalAlignment = HorizontalAlignment.Stretch;
                chart.Height = 36;
                chart.Margin = new Thickness(0, 6, 0, 0);
                Padding = new Thickness(10, 8, 12, 8);
            }
        }
    }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StatTile), new PropertyMetadata("", (d, e) => ((StatTile)d).caption.Text = (string?)e.NewValue ?? ""));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(StatTile), new PropertyMetadata(null, (d, e) =>
        {
            var tile = (StatTile)d;
            tile.chart.Stroke = (Brush?)e.NewValue;
            tile.edge.Fill = (Brush?)e.NewValue;
        }));

    public static readonly DependencyProperty SecondStrokeProperty = DependencyProperty.Register(
        nameof(SecondStroke), typeof(Brush), typeof(StatTile), new PropertyMetadata(null, (d, e) => ((StatTile)d).chart.SecondStroke = (Brush?)e.NewValue));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(StatTile), new PropertyMetadata(true, (d, e) => ((StatTile)d).chart.Filled = (bool)e.NewValue));

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? SecondStroke
    {
        get => (Brush?)GetValue(SecondStrokeProperty);
        set => SetValue(SecondStrokeProperty, value);
    }

    /// <summary>Sets the texts and redraws the sparkline; <paramref name="max"/> is the value that reaches the top.</summary>
    public void Update(string valueText, string detailText, HistoryBuffer history, double max, HistoryBuffer? second = null)
    {
        value.Text = valueText;
        detail.Text = detailText;
        detail.Visibility = detailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        chart.Update(history, max, second);
    }

    private void ApplyResources()
    {
        ResourceDictionary resources = Application.Current.Resources;
        Background = (Brush)resources["CardBackgroundFillColorDefaultBrush"];
        BorderBrush = (Brush)resources["CardStrokeColorDefaultBrush"];
        caption.Style = (Style)resources["CaptionTextBlockStyle"];
        caption.Foreground = (Brush)resources["TextFillColorSecondaryBrush"];
        value.Style = (Style)resources["TitleTextBlockStyle"];
        value.FontSize = stacked ? 20 : 24;
        detail.Style = (Style)resources["CaptionTextBlockStyle"];
        detail.Foreground = (Brush)resources["TextFillColorSecondaryBrush"];
        // The text styles wrap; a tile's numbers must stay on one line and trim instead.
        foreach (TextBlock block in new[] { caption, value, detail })
        {
            block.TextWrapping = TextWrapping.NoWrap;
            block.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }
}
