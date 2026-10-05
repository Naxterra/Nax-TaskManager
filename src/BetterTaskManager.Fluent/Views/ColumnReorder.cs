using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Lets the user move columns of a table, like Task Manager: drag a column header sideways and drop it where it
/// should go (an accent bar shows the spot), or right-click it for move left / move right / reset. A drag never
/// sorts; a plain click still does.
/// </summary>
internal sealed class ColumnReorder
{
    private const double DragThreshold = 8;

    private readonly Grid header;
    private readonly ColumnLayout layout;
    private readonly Border indicator;
    private readonly MenuFlyoutItem moveLeft, moveRight, reset;
    private Button? source;
    private string? column;
    private double startX;
    private bool dragging;
    private int target;
    private long suppressClickUntil;
    private string? menuColumn;

    public ColumnReorder(Grid header, ColumnLayout layout)
    {
        this.header = header;
        this.layout = layout;
        indicator = new Border
        {
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumnSpan(indicator, header.ColumnDefinitions.Count);
        header.Children.Add(indicator);

        header.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        header.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        header.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        header.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost), true);

        moveLeft = new MenuFlyoutItem { Text = Loc.Get("Column_MoveLeft"), Icon = new FontIcon { Glyph = "" } };
        moveRight = new MenuFlyoutItem { Text = Loc.Get("Column_MoveRight"), Icon = new FontIcon { Glyph = "" } };
        reset = new MenuFlyoutItem { Text = Loc.Get("Column_ResetOrder") };
        moveLeft.Click += (_, _) => { if (menuColumn is not null) layout.Move(menuColumn, layout.ColumnOf(menuColumn) - 1); };
        moveRight.Click += (_, _) => { if (menuColumn is not null) layout.Move(menuColumn, layout.ColumnOf(menuColumn) + 1); };
        reset.Click += (_, _) => layout.ResetOrder();
        var menu = new MenuFlyout { Items = { moveLeft, moveRight, new MenuFlyoutSeparator(), reset } };
        menu.Opening += (_, _) =>
        {
            menuColumn = (menu.Target as Button)?.Tag as string;
            int position = menuColumn is null ? -1 : layout.ColumnOf(menuColumn);
            moveLeft.IsEnabled = position > 1;
            moveRight.IsEnabled = position >= 1 && position < layout.Order.Count;
            reset.IsEnabled = !layout.IsDefaultOrder;
        };
        foreach (Button button in header.Children.OfType<Button>())
        {
            if (button.Tag is string tag && layout.IsMovable(tag)) button.ContextFlyout = menu;
        }
    }

    /// <summary>
    /// True during a drag and shortly after, so the header's Click handler skips sorting. The button raises Click
    /// before the release reaches the header row, so the drag itself has to count.
    /// </summary>
    public bool SuppressClick => dragging || Environment.TickCount64 < suppressClickUntil;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        Cancel();
        if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
        if (FindHeaderButton(e.OriginalSource as DependencyObject) is not { Tag: string tag } button || !layout.IsMovable(tag)) return;
        source = button;
        column = tag;
        startX = e.GetCurrentPoint(header).Position.X;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (column is null || source is null) return;
        double x = e.GetCurrentPoint(header).Position.X;
        if (!dragging)
        {
            if (Math.Abs(x - startX) < DragThreshold) return;
            dragging = true;
            source.Opacity = 0.5;
        }

        target = layout.PositionAt(x);
        int current = layout.ColumnOf(column);
        if (target == current)
        {
            indicator.Visibility = Visibility.Collapsed;
            return;
        }
        // Dropping moves the column into the target's place; the bar marks the side it lands on.
        double edge = target < current ? layout.LeftEdgeOf(target) : layout.LeftEdgeOf(target) + layout.WidthOf(target);
        indicator.Margin = new Thickness(Math.Max(0, edge - 1.5), 4, 0, 4);
        indicator.Visibility = Visibility.Visible;
    }

    // Release and capture loss both end the drag; whichever arrives first drops the column.
    private void OnReleased(object sender, PointerRoutedEventArgs e) => Drop();

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e) => Drop();

    private void Drop()
    {
        if (dragging && column is not null)
        {
            suppressClickUntil = Environment.TickCount64 + 400;
            string moved = column;
            int position = target;
            Cancel();
            layout.Move(moved, position);
            return;
        }
        Cancel();
    }

    private void Cancel()
    {
        if (source is not null) source.Opacity = 1;
        source = null;
        column = null;
        dragging = false;
        indicator.Visibility = Visibility.Collapsed;
    }

    private Button? FindHeaderButton(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, header))
        {
            if (element is Button button && ReferenceEquals(VisualTreeHelper.GetParent(button), header)) return button;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}
