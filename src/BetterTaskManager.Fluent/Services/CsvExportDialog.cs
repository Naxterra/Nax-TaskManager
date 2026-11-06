using BetterTaskManager.Core.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace BetterTaskManager.Fluent.Services;

internal static class CsvExportDialog
{
    private static bool busy;

    public static async Task SaveAsync(XamlRoot root, string name, Action<TextWriter> write)
    {
        if (busy) return;
        busy = true;
        try
        {
            // The Windows App SDK picker also supports windows running as administrator.
            var picker = new FileSavePicker(App.Window.AppWindow.Id)
            {
                SuggestedFileName = name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                DefaultFileExtension = ".csv"
            };
            picker.FileTypeChoices.Add(Loc.Get("Export_Csv"), new List<string> { ".csv" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await Task.Run(() => CsvExport.Save(file.Path, write));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException
            or System.Runtime.InteropServices.COMException)
        {
            await new ContentDialog { XamlRoot = root, Title = Loc.Get("Export_Failed"), Content = ex.Message,
                CloseButtonText = Loc.Get("Common_OK") }.ShowAsync();
        }
        finally { busy = false; }
    }
}
