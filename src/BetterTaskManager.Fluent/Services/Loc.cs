using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Interface text from Strings/&lt;language&gt;/Resources.resw. Only the app's own wording is translated: process names,
/// window titles, paths, host names and service names stay exactly as Windows reports them.
/// </summary>
internal static class Loc
{
    private static ResourceLoader? loader;

    /// <summary>
    /// Applies the language setting before any window exists. "System" follows the Windows display language;
    /// English is the fallback for languages without a translation.
    /// </summary>
    public static void ApplyLanguage(string setting)
    {
        string tag = setting is "en-US" or "de-DE" ? setting : "";
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Without the override the app follows Windows, which is the default anyway.
        }
        if (tag.Length > 0)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(tag);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }

    public static string Get(string key)
    {
        try
        {
            loader ??= new ResourceLoader();
            string value = loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return key;
        }
    }

    /// <summary>A resource with {0}-style placeholders.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
