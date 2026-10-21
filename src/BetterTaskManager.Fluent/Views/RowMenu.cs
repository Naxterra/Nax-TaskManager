using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// The per-row "…" button: pages route their row pointer events and selection changes here, so the button appears on
/// the hovered and the selected row, and open the same menu there that the right-click shows.
/// </summary>
internal static class RowMenu
{
    public static void Hover(object sender, bool over)
    {
        if ((sender as FrameworkElement)?.DataContext is RowSlot slot) slot.Hovered = over;
    }

    /// <summary>Call from a list's SelectionChanged, before any early return, so the state follows every change.</summary>
    public static void TrackSelection(SelectionChangedEventArgs e)
    {
        foreach (object item in e.RemovedItems)
        {
            if (item is RowSlot slot) slot.Selected = false;
        }
        foreach (object item in e.AddedItems)
        {
            if (item is RowSlot slot) slot.Selected = true;
        }
    }

    /// <summary>Opens the menu below the button that was clicked.</summary>
    public static void Show(MenuFlyout menu, object button)
    {
        if (button is FrameworkElement element) menu.ShowAt(element, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }
}
