using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>Theme-aware resources for controls constructed in C#, including a runtime theme switch without handler leaks.</summary>
internal static class ThemeResources
{
    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<DependencyProperty, string>> Bindings = new();

    public static object Get(FrameworkElement? element, string key)
    {
        var resources = Application.Current.Resources;
        ElementTheme actual = element?.ActualTheme ?? (App.Window?.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default);
        string theme = actual == ElementTheme.Light ? "Light" : "Default";
        return resources.ThemeDictionaries.TryGetValue(theme, out object? dictionary) && dictionary is ResourceDictionary themed && themed.TryGetValue(key, out object? found)
            ? found : resources[key];
    }

    public static Brush GetBrush(FrameworkElement? element, string key) => (Brush)Get(element, key);

    public static void SetResourceReference(this FrameworkElement element, DependencyProperty property, string key)
    {
        bool attach = false;
        Dictionary<DependencyProperty, string> map = Bindings.GetValue(element, _ =>
        {
            attach = true;
            return new Dictionary<DependencyProperty, string>();
        });
        map[property] = key;
        element.SetValue(property, Get(element, key));
        if (!attach) return;

        void ApplyAll(FrameworkElement target)
        {
            if (!Bindings.TryGetValue(target, out Dictionary<DependencyProperty, string>? entries)) return;
            foreach (var (prop, resKey) in entries) target.SetValue(prop, Get(target, resKey));
        }
        element.Loaded += (sender, _) => { if (sender is FrameworkElement target) ApplyAll(target); };
        element.ActualThemeChanged += (sender, _) => ApplyAll(sender);
    }
}
