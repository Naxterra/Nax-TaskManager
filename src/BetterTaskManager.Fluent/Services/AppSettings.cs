using System.Text.Json;

namespace BetterTaskManager.Fluent.Services;

public sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
    private static readonly string FilePath = Path.Combine(Folder, "fluent-settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int RefreshIntervalMilliseconds { get; set; } = 1000;
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public string ProcessSortColumn { get; set; } = "Memory";
    public bool ProcessSortDescending { get; set; } = true;
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 820;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail on exit because of them.
        }
    }
}

internal static class CrashLog
{
    public static void Write(Exception exception)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "fluent-crash.log"), $"{DateTimeOffset.Now:O}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Diagnostics must never hide the original failure.
        }
    }
}
