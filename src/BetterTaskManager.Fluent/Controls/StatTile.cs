using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// A small card with a caption, the current value, a one-line detail and a sparkline of the last 60 samples, for the
/// strip under a page title. The strip gives the page's numbers a face before the table starts.
/// </summary>
public sealed class StatTile : Grid
{
    private readonly TextBlock caption = new();
    private readonly TextBlock value = new();
    private readonly TextBlock detail = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TrendChart chart = new() { Compact = true, Height = 34, Width = 96, VerticalAlignment = VerticalAlignment.Center };

    public StatTile()
    {
        Padding = new Thickness(14, 10, 14, 10);
        MinWidth = 190;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(caption);
        text.Children.Add(value);
        text.Children.Add(detail);
        grid.Children.Add(text);
        Grid.SetColumn(chart, 1);
        grid.Children.Add(chart);
        Children.Add(grid);
        Loaded += (_, _) => ApplyResources();
        ActualThemeChanged += (_, _) => ApplyResources();
    }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StatTile), new PropertyMetadata("", (d, e) => ((StatTile)d).caption.Text = (string?)e.NewValue ?? ""));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(StatTile), new PropertyMetadata(null, (d, e) => ((StatTile)d).chart.Stroke = (Brush?)e.NewValue));

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
        value.Style = (Style)resources["SubtitleTextBlockStyle"];
        detail.Style = (Style)resources["CaptionTextBlockStyle"];
        detail.Foreground = (Brush)resources["TextFillColorSecondaryBrush"];
    }
}
