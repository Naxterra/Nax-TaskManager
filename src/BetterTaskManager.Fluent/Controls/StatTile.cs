using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>Value-first metric card with history below, keeping narrow cards readable.</summary>
public sealed class StatTile : Grid
{
    private readonly TextBlock caption = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock value = new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TrendChart chart = new() { Compact = true, Height = 25, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Ellipse dot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    public StatTile()
    {
        Padding = new Thickness(16, 12, 16, 10);
        CornerRadius = new CornerRadius(12); BorderThickness = new Thickness(1);
        for (int i = 0; i < 4; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid(); header.Children.Add(caption); dot.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(dot); Children.Add(header);
        value.Margin = new Thickness(0, 5, 0, 0);
        Grid.SetRow(value, 1); Children.Add(value); Grid.SetRow(detail, 2); Children.Add(detail); Grid.SetRow(chart, 3); Children.Add(chart);
        foreach (var block in new[] { caption, value, detail }) { block.TextWrapping = TextWrapping.NoWrap; block.TextTrimming = TextTrimming.CharacterEllipsis; }
        this.SetResourceReference(BackgroundProperty, "SurfaceBrush"); this.SetResourceReference(BorderBrushProperty, "SurfaceStrokeBrush");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
    }
    private bool stacked;
    public bool Stacked { get => stacked; set { stacked = value; Padding = value ? new Thickness(12, 10, 12, 8) : new Thickness(16, 12, 16, 10); this.value.FontSize = value ? 20 : 28; chart.Height = value ? 22 : 25; } }
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatTile), new PropertyMetadata("", (d, e) => ((StatTile)d).caption.Text = (string?)e.NewValue ?? ""));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(StatTile), new PropertyMetadata(null, (d, e) => { var tile = (StatTile)d; tile.chart.Stroke = (Brush?)e.NewValue; tile.dot.Fill = (Brush?)e.NewValue; }));
    public static readonly DependencyProperty SecondStrokeProperty = DependencyProperty.Register(nameof(SecondStroke), typeof(Brush), typeof(StatTile), new PropertyMetadata(null, (d, e) => ((StatTile)d).chart.SecondStroke = (Brush?)e.NewValue));
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(StatTile), new PropertyMetadata(true, (d, e) => ((StatTile)d).chart.Filled = (bool)e.NewValue));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? SecondStroke { get => (Brush?)GetValue(SecondStrokeProperty); set => SetValue(SecondStrokeProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public void Update(string valueText, string detailText, HistoryBuffer history, double max, HistoryBuffer? second = null)
    {
        value.Text = valueText; detail.Text = detailText; detail.Visibility = detailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, Caption + ": " + valueText + " · " + detailText); chart.Update(history, max, second);
    }
}
