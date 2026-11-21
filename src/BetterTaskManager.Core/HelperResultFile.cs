namespace BetterTaskManager.Core;

/// <summary>
/// The file an elevated helper writes its result into, for the window that started it. The path comes from the command
/// line, which any program can supply, so the helper only creates a new file named "nax…" inside the current user's
/// temporary folder, never through a link, and never overwrites anything.
/// </summary>
public static class HelperResultFile
{
    /// <summary>True when <paramref name="path"/> may be written by an elevated helper.</summary>
    public static bool IsAcceptable(string path, string? tempFolder = null) => TryNormalize(path, tempFolder, out _);

    private static bool TryNormalize(string path, string? tempFolder, out string canonical)
    {
        canonical = "";
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            string full = Path.GetFullPath(path);
            // Reject alternate data streams (e.g. nax-result.txt:stream.txt) or extra colons after the drive prefix.
            if (full.LastIndexOf(':') > 1) return false;
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempFolder ?? Path.GetTempPath()));
            string prefix = Path.EndsInDirectorySeparator(temp) ? temp : temp + Path.DirectorySeparatorChar;
            string name = Path.GetFileName(full);
            if (!name.StartsWith("nax", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(full) || Directory.Exists(full)) return false;
            // No junction or symbolic link on or inside the temporary folder between it and the file, so the write stays in it.
            DirectoryInfo? folder = new FileInfo(full).Directory;
            if (folder is null || !folder.Exists) return false;
            for (; folder is not null && folder.FullName.Length >= temp.Length; folder = folder.Parent)
            {
                if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            }
            canonical = full;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Creates the file with the text, or does nothing when the path is not acceptable. Never throws.</summary>
    public static bool TryWrite(string path, string text, string? tempFolder = null)
    {
        if (!TryNormalize(path, tempFolder, out string canonical)) return false;
        try
        {
            using var stream = new FileStream(canonical, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
