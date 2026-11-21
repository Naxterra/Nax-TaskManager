using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BetterTaskManager.Fluent.Services;

internal static class Dialogs
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static XamlRoot? ResolveRoot(XamlRoot? preferred) => preferred ?? App.Window?.Content?.XamlRoot;

    public static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, XamlRoot? preferredRoot = null)
    {
        await Gate.WaitAsync();
        try
        {
            XamlRoot? root = ResolveRoot(preferredRoot ?? dialog.XamlRoot);
            if (root is null) return ContentDialogResult.None;
            dialog.XamlRoot = root;
            return await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return ContentDialogResult.None;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> ConfirmAsync(XamlRoot? root, string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await ShowDialogAsync(dialog, root) == ContentDialogResult.Primary;
    }

    public static async Task ShowAsync(XamlRoot? root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Loc.Get("Common_OK")
        };
        await ShowDialogAsync(dialog, root);
    }
}
