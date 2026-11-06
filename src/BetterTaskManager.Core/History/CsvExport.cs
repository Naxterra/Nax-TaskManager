using System.Globalization;
using System.Text;

namespace BetterTaskManager.Core.History;

public static class CsvExport
{
    public static void Save(string path, Action<TextWriter> write)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".nax-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true) { NewLine = "\r\n" };
                write(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Connections(TextWriter writer, IEnumerable<ConnectionRecord> records)
    {
        Row(writer, "First seen (UTC)", "Last seen (UTC)", "App", "Path", "PID", "Protocol", "Local address", "Local port",
            "Remote address", "Remote port", "Remote host", "Bytes received", "Bytes sent", "State", "Scope", "Inbound");
        foreach (var record in records)
            Row(writer, record.FirstSeen.ToString("O"), record.LastSeen.ToString("O"), record.AppName, record.AppPath,
                record.Pid, record.Protocol, record.LocalAddress, record.LocalPort, record.RemoteAddress, record.RemotePort,
                record.RemoteHost, record.BytesIn, record.BytesOut, record.State, record.Scope, record.Inbound);
    }

    public static void Blocked(TextWriter writer, IEnumerable<BlockedRecord> records)
    {
        Row(writer, "First seen (UTC)", "Last seen (UTC)", "Path", "Inbound", "Protocol", "Remote address", "Remote host",
            "Remote port", "Local port", "Blocked by this app", "Reason", "Count");
        foreach (var record in records)
            Row(writer, record.FirstSeen.ToString("O"), record.LastSeen.ToString("O"), record.AppPath, record.Inbound,
                record.Protocol, record.RemoteAddress, record.RemoteHost, record.RemotePort, record.LocalPort, record.Ours,
                record.Reason, record.Count);
    }

    private static void Row(TextWriter writer, params object?[] fields)
    {
        writer.WriteLine(string.Join(",", fields.Select(field =>
        {
            string text = Convert.ToString(field, CultureInfo.InvariantCulture) ?? "";
            // Treat executable names/hostnames/rule text as data when opened in a spreadsheet.
            if (field is string && text.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        })));
    }
}
