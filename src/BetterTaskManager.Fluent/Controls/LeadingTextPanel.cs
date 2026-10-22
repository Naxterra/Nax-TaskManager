using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// Puts the first child (a text) at the left and the other children (status badges) right after it, so a badge
/// stays next to the name instead of at the far edge of the column. When room runs out the text shrinks (and trims)
/// and the badges keep their size.
/// </summary>
public sealed class LeadingTextPanel : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double others = 0, height = 0;
        for (int index = 1; index < Children.Count; index++)
        {
            UIElement child = Children[index];
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            if (child.DesiredSize.Width > 0) others += child.DesiredSize.Width + Spacing;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (Children.Count > 0)
        {
            Children[0].Measure(new Size(Math.Max(0, available.Width - others), available.Height));
            height = Math.Max(height, Children[0].DesiredSize.Height);
        }
        double width = (Children.Count > 0 ? Children[0].DesiredSize.Width : 0) + others;
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double others = 0;
        for (int index = 1; index < Children.Count; index++)
        {
            if (Children[index].DesiredSize.Width > 0) others += Children[index].DesiredSize.Width + Spacing;
        }
        double x = 0;
        if (Children.Count > 0)
        {
            double textWidth = Math.Min(Children[0].DesiredSize.Width, Math.Max(0, final.Width - others));
            Children[0].Arrange(new Rect(0, 0, textWidth, final.Height));
            x = textWidth;
        }
        for (int index = 1; index < Children.Count; index++)
        {
            Size size = Children[index].DesiredSize;
            if (size.Width <= 0) continue;
            x += Spacing;
            Children[index].Arrange(new Rect(x, (final.Height - size.Height) / 2, size.Width, size.Height));
            x += size.Width;
        }
        return final;
    }
}
