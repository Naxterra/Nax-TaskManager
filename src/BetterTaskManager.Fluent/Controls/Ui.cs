using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>
/// Small colored building blocks shared by the code-built pages. A tone names one area's color (Brand, Cpu, Memory,
/// Receive, Send, Disk, Gpu, Good, Warn, Bad, Rose, Idle); App.xaml defines ToneXBrush (the color), BadgeXBrush (a soft
/// fill) and WashXColor (a faint tint) for both themes.
/// </summary>
internal static class Ui
{
    /// <summary>A rounded square with a tinted fill and the icon in the tone's color, like the ones next to page titles.</summary>
    public static Border IconBadge(string glyph, string tone, double size = 36)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = Math.Round(size * 0.5) };
        icon.SetResourceReference(IconElement.ForegroundProperty, "Tone" + tone + "Brush");
        var badge = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Child = icon, VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, "Badge" + tone + "Brush");
        return badge;
    }

    /// <summary>A pill with text (and optionally an icon) in the tone's color on its soft fill.</summary>
    public static Border Chip(string text, string tone, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (glyph is not null)
        {
            var icon = new FontIcon { Glyph = glyph, FontSize = 10 };
            icon.SetResourceReference(IconElement.ForegroundProperty, "Tone" + tone + "Brush");
            content.Children.Add(icon);
        }
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Tone" + tone + "Brush");
        content.Children.Add(label);
        var chip = new Border { Child = content, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "Badge" + tone + "Brush");
        return chip;
    }

    /// <summary>A row of chips that wraps to the next line when it runs out of room.</summary>
    public static Panel Chips(IEnumerable<(string Text, string Tone)> chips)
    {
        var panel = new WrapPanel();
        foreach ((string text, string tone) in chips) panel.Children.Add(Chip(text, tone));
        return panel;
    }

    /// <summary>What a page or card shows when it has nothing yet: a big badge, a title, a sentence and an optional action.</summary>
    public static StackPanel EmptyState(string glyph, string tone, string title, string text, FrameworkElement? action = null)
    {
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440, Margin = new Thickness(16, 24, 16, 24) };
        var badge = IconBadge(glyph, tone, 56);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(badge);
        var heading = new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(heading);
        var body = new TextBlock { Text = text, FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(body);
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(action);
        }
        return panel;
    }

    /// <summary>A soft diagonal tint in the tone's color that fades out, for tile and card backgrounds.</summary>
    public static void SetWash(this Border element, string tone)
    {
        void Apply()
        {
            Color color = ThemeColor(element, "Wash" + tone + "Color");
            element.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = color, Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 0.75 }
                }
            };
        }
        Apply();
        element.ActualThemeChanged += (_, _) => Apply();
        element.Loaded += (_, _) => Apply();
    }

    /// <summary>A color from the theme dictionary that matches the element's current theme.</summary>
    public static Color ThemeColor(FrameworkElement element, string key)
    {
        var resources = Application.Current.Resources;
        string theme = element.ActualTheme == ElementTheme.Light ? "Light" : "Default";
        object? value = resources.ThemeDictionaries.TryGetValue(theme, out object? dictionary) && dictionary is ResourceDictionary themed && themed.TryGetValue(key, out object? found)
            ? found : resources.TryGetValue(key, out object? plain) ? plain : null;
        return value switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            _ => Colors.Transparent
        };
    }
}

/// <summary>Lays children out left to right and starts a new line when the next one does not fit.</summary>
internal sealed class WrapPanel : Panel
{
    public double Spacing { get; set; } = 6;

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        double x = 0, y = 0, line = 0, width = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Windows.Foundation.Size(available.Width, double.PositiveInfinity));
            Windows.Foundation.Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width)
            {
                y += line + Spacing;
                x = 0;
                line = 0;
            }
            x += size.Width + Spacing;
            line = Math.Max(line, size.Height);
            width = Math.Max(width, x - Spacing);
        }
        return new Windows.Foundation.Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), y + line);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        double x = 0, y = 0, line = 0;
        foreach (UIElement child in Children)
        {
            Windows.Foundation.Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                y += line + Spacing;
                x = 0;
                line = 0;
            }
            child.Arrange(new Windows.Foundation.Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            line = Math.Max(line, size.Height);
        }
        return final;
    }
}
