using Microsoft.UI.Xaml;

namespace BetterTaskManager.Fluent.Controls;

/// <summary>Theme-aware resources for controls constructed in C#, including a runtime theme switch.</summary>
internal static class ThemeResources
{
    public static void SetResourceReference(this FrameworkElement element, DependencyProperty property, string key)
    {
        void Apply()
        {
            var resources = Application.Current.Resources;
            string theme = element.ActualTheme == ElementTheme.Light ? "Light" : "Default";
            object value = resources.ThemeDictionaries.TryGetValue(theme, out object? dictionary) && dictionary is ResourceDictionary themed && themed.TryGetValue(key, out object? found)
                ? found : resources[key];
            element.SetValue(property, value);
        }
        Apply();
        element.Loaded += (_, _) => Apply();
        element.ActualThemeChanged += (_, _) => Apply();
    }
}
