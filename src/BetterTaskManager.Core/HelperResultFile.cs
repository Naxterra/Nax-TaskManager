namespace BetterTaskManager.Core;

/// <summary>
/// The file an elevated helper writes its result into, for the window that started it. The path comes from the command
/// line, which any program can supply, so the helper only creates a new file named "nax…" inside the current user's
/// temporary folder, never through a link, and never overwrites anything.
/// </summary>
public static class HelperResultFile
{
    /// <summary>True when <paramref name="path"/> may be written by an elevated helper.</summary>
    public static bool IsAcceptable(string path, string? tempFolder = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            string full = Path.GetFullPath(path);
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempFolder ?? Path.GetTempPath()));
            string name = Path.GetFileName(full);
            if (!name.StartsWith("nax", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
            if (!full.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(full) || Directory.Exists(full)) return false;
            // No junction or symbolic link between the temporary folder and the file, so the write stays in it.
            for (DirectoryInfo? folder = new FileInfo(full).Directory; folder is not null && folder.FullName.Length >= temp.Length; folder = folder.Parent)
            {
                if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Creates the file with the text, or does nothing when the path is not acceptable. Never throws.</summary>
    public static bool TryWrite(string path, string text, string? tempFolder = null)
    {
        if (!IsAcceptable(path, tempFolder)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
