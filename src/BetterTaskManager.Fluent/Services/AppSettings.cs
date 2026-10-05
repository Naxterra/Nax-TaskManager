using System.Text.Json;

namespace BetterTaskManager.Fluent.Services;

public sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaxTaskManager");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    /// <summary>Settings from before the rename to Nax-TaskManager; read once when no new file exists.</summary>
    private static readonly string LegacyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager", "fluent-settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int RefreshIntervalMilliseconds { get; set; } = 1000;
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    /// <summary>"System" (Windows display language), "en-US" or "de-DE". Takes effect on the next start.</summary>
    public string Language { get; set; } = "System";
    public string ProcessSortColumn { get; set; } = "Memory";
    public bool ProcessSortDescending { get; set; } = true;
    /// <summary>Network page: by app name by default, so rows do not move while traffic changes.</summary>
    public string NetworkSortColumn { get; set; } = "Name";
    public bool NetworkSortDescending { get; set; }
    public string DetailsSortColumn { get; set; } = "Name";
    public bool DetailsSortDescending { get; set; }
    /// <summary>Processes page: false shows the grouped "by app" layout, true the flat "all processes" layout.</summary>
    public bool ProcessFlatView { get; set; }
    /// <summary>The user dismissed the limited-mode notice; do not show it again.</summary>
    public bool HideElevationNotice { get; set; }
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 820;

    public static AppSettings Load()
    {
        try
        {
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
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
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaxTaskManager");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"{DateTimeOffset.Now:O}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Diagnostics must never hide the original failure.
        }
    }
}
