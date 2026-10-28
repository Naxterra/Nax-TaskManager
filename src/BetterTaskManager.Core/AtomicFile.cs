using System.Text;

namespace BetterTaskManager.Core;

/// <summary>
/// Writes a file so that a crash or power loss leaves either the old or the new content, never a half-written or
/// zero-filled file: the text goes to a temporary file next to the target, is flushed to the disk, and only then
/// replaces the target.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        // A name of its own per write, so two writers of the same file never collide on the temporary file.
        string temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                // Without this the rename can reach the disk before the data, and a power loss leaves zeros behind.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines) =>
        WriteAllText(path, string.Concat(lines.Select(line => line + Environment.NewLine)));
}
