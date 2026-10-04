using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BetterTaskManager.Fluent.Services;

internal static class Dialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Loc.Get("Common_OK")
        };
        await dialog.ShowAsync();
    }
}
