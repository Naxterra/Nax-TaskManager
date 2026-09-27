using BetterTaskManager.Fluent.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// A lightweight area chart for the last 60 samples: one filled series plus an optional second line.
/// Rebuilds two point collections per update, so it stays cheap at one update per second.
/// </summary>
public sealed class TrendChart : Grid
{
    private readonly Canvas canvas = new();
    private readonly Polygon fill = new() { Opacity = 0.22 };
    private readonly Polyline line = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polyline secondLine = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
    private readonly Line[] gridLines = Enumerable.Range(0, 3).Select(_ => new Line { StrokeThickness = 1 }).ToArray();
    private HistoryBuffer? primary;
    private HistoryBuffer? secondary;
    private double maximum = 100;

    public TrendChart()
    {
        MinHeight = 80;
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        foreach (Line gridLine in gridLines) canvas.Children.Add(gridLine);
        canvas.Children.Add(fill);
        canvas.Children.Add(line);
        canvas.Children.Add(secondLine);
        Children.Add(canvas);
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => ApplyThemeBrushes();
        Loaded += (_, _) => ApplyThemeBrushes();
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TrendChart), new PropertyMetadata(null, (d, e) =>
        {
            var chart = (TrendChart)d;
            chart.line.Stroke = (Brush?)e.NewValue;
            chart.fill.Fill = (Brush?)e.NewValue;
        }));

    public static readonly DependencyProperty SecondStrokeProperty = DependencyProperty.Register(
        nameof(SecondStroke), typeof(Brush), typeof(TrendChart), new PropertyMetadata(null, (d, e) =>
            ((TrendChart)d).secondLine.Stroke = (Brush?)e.NewValue));

    /// <summary>Line and fill brush. A dependency property so XAML can assign theme resources.</summary>
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

    public void Update(HistoryBuffer values, double max, HistoryBuffer? second = null)
    {
        primary = values;
        secondary = second;
        maximum = Math.Max(max, 1e-9);
        secondLine.Visibility = second is null ? Visibility.Collapsed : Visibility.Visible;
        Redraw();
    }

    private void ApplyThemeBrushes()
    {
        var gridBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        foreach (Line gridLine in gridLines) gridLine.Stroke = gridBrush;
        BorderBrush = gridBrush;
        Background = new SolidColorBrush(Colors.Transparent);
    }

    private void Redraw()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 2 || height <= 2) return;

        for (int index = 0; index < gridLines.Length; index++)
        {
            double y = height * (index + 1) / (gridLines.Length + 1);
            gridLines[index].X1 = 0;
            gridLines[index].X2 = width;
            gridLines[index].Y1 = gridLines[index].Y2 = y;
        }

        if (primary is null) return;
        line.Points = Points(primary, width, height);
        var area = Points(primary, width, height);
        if (area.Count > 0)
        {
            area.Add(new Point(area[^1].X, height));
            area.Add(new Point(area[0].X, height));
        }
        fill.Points = area;
        if (secondary is not null) secondLine.Points = Points(secondary, width, height);
    }

    private PointCollection Points(HistoryBuffer values, double width, double height)
    {
        var points = new PointCollection();
        int capacity = values.Capacity;
        double step = width / (capacity - 1);
        // Newest sample sits at the right edge; history grows leftwards like Task Manager.
        int offset = capacity - values.Count;
        for (int index = 0; index < values.Count; index++)
        {
            double value = Math.Clamp(values[index] / maximum, 0, 1);
            points.Add(new Point((offset + index) * step, height - value * (height - 2) - 1));
        }
        return points;
    }
}
