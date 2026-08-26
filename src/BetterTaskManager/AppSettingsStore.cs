using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BetterTaskManager
{
    internal sealed class AppSettings
    {
        public int WindowWidth { get; set; } = 1560;
        public int WindowHeight { get; set; } = 900;
        public bool Maximized { get; set; }
        public int RefreshIntervalIndex { get; set; } = 2;
    }

    internal sealed class AppSettingsStore
    {
        private readonly string settingsPath;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public AppSettingsStore(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath)) throw new ArgumentException("A settings path is required.", nameof(settingsPath));
            this.settingsPath = settingsPath;
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new AppSettings();
                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (IOException) { return new AppSettings(); }
            catch (UnauthorizedAccessException) { return new AppSettings(); }
            catch (JsonException) { return new AppSettings(); }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string folder = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("The settings path has no parent folder.");
            Directory.CreateDirectory(folder);

            string temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
                File.Move(temporaryPath, settingsPath, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
