using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterTaskManager
{
    internal sealed class NetworkHistoryStore
    {
        private const string Header = "Timestamp,Process,PID,User,Protocol,LocalAddress,LocalPort,RemoteAddress,RemotePort,State,Path";
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
        private readonly string historyPath;
        private readonly object syncRoot = new object();
        private HashSet<string> previousConnectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime lastWrite = DateTime.MinValue;
        private DateTime lastPrune = DateTime.MinValue;

        public NetworkHistoryStore(string historyPath)
        {
            if (string.IsNullOrWhiteSpace(historyPath)) throw new ArgumentException("A history path is required.", nameof(historyPath));
            this.historyPath = historyPath;
        }

        public int SaveSnapshot(IEnumerable<NetworkRow> rows, DateTime observedAt)
        {
            lock (syncRoot) return SaveSnapshotCore(rows, observedAt);
        }

        private int SaveSnapshotCore(IEnumerable<NetworkRow> rows, DateTime observedAt)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (lastWrite != DateTime.MinValue && observedAt - lastWrite < SnapshotInterval) return 0;

            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keysWrittenThisSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newRows = new List<NetworkRow>();

            foreach (NetworkRow row in rows)
            {
                if (row == null) continue;
                string key = ConnectionKey(row);
                currentKeys.Add(key);
                if (!previousConnectionKeys.Contains(key) && keysWrittenThisSnapshot.Add(key)) newRows.Add(row);
            }

            string folder = Path.GetDirectoryName(historyPath);
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("The history path has no parent folder.");
            Directory.CreateDirectory(folder);
            PruneIfNeeded(observedAt);
            EnsureHeader();

            if (newRows.Count > 0)
            {
                using (var writer = new StreamWriter(historyPath, true, Encoding.UTF8))
                {
                    foreach (NetworkRow row in newRows) writer.WriteLine(Serialize(row));
                }
            }

            previousConnectionKeys = currentKeys;
            lastWrite = observedAt;
            return newRows.Count;
        }

        public List<string[]> LoadRecent(int maximumRows)
        {
            lock (syncRoot) return LoadRecentCore(maximumRows);
        }

        private List<string[]> LoadRecentCore(int maximumRows)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
            var rows = new Queue<string[]>(maximumRows);
            if (!File.Exists(historyPath)) return rows.ToList();

            foreach (string line in File.ReadLines(historyPath).Skip(1))
            {
                List<string> fields = ParseCsvLine(line);
                if (fields.Count < 11) continue;
                rows.Enqueue(fields.Take(11).ToArray());
                if (rows.Count > maximumRows) rows.Dequeue();
            }

            return rows.Reverse().ToList();
        }

        internal static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < (line ?? "").Length; index++)
            {
                char character = line[index];
                if (character == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        private void PruneIfNeeded(DateTime now)
        {
            if (lastPrune.Date == now.Date) return;
            if (!File.Exists(historyPath))
            {
                lastPrune = now;
                return;
            }

            string folder = Path.GetDirectoryName(historyPath);
            string temporaryPath = Path.Combine(folder, Path.GetFileName(historyPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            DateTime cutoff = now - Retention;

            try
            {
                using (var reader = new StreamReader(historyPath, Encoding.UTF8, true))
                using (var writer = new StreamWriter(temporaryPath, false, Encoding.UTF8))
                {
                    reader.ReadLine();
                    writer.WriteLine(Header);

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        List<string> fields = ParseCsvLine(line);
                        DateTime timestamp;
                        if (fields.Count >= 11 && TryParseTimestamp(fields[0], out timestamp) && timestamp >= cutoff) writer.WriteLine(line);
                    }
                }

                File.Move(temporaryPath, historyPath, true);
                lastPrune = now;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void EnsureHeader()
        {
            if (File.Exists(historyPath) && new FileInfo(historyPath).Length > 0) return;
            File.WriteAllText(historyPath, Header + Environment.NewLine, Encoding.UTF8);
        }

        private static bool TryParseTimestamp(string value, out DateTime timestamp)
        {
            return DateTime.TryParseExact(value, "s", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp) ||
                DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out timestamp);
        }

        private static string Serialize(NetworkRow row)
        {
            return string.Join(",", new[]
            {
                CsvFileWriter.Escape(row.Timestamp.ToString("s", CultureInfo.InvariantCulture)),
                CsvFileWriter.Escape(row.Process),
                CsvFileWriter.Escape(row.Pid.ToString(CultureInfo.InvariantCulture)),
                CsvFileWriter.Escape(row.User),
                CsvFileWriter.Escape(row.Protocol),
                CsvFileWriter.Escape(row.LocalAddress),
                CsvFileWriter.Escape(row.LocalPort),
                CsvFileWriter.Escape(row.RemoteAddress),
                CsvFileWriter.Escape(row.RemotePort),
                CsvFileWriter.Escape(row.State),
                CsvFileWriter.Escape(row.Path)
            });
        }

        private static string ConnectionKey(NetworkRow row)
        {
            return string.Join("\u001F", new[]
            {
                row.Pid.ToString(CultureInfo.InvariantCulture),
                row.Protocol ?? "",
                row.LocalAddress ?? "",
                row.LocalPort ?? "",
                row.RemoteAddress ?? "",
                row.RemotePort ?? "",
                row.State ?? "",
                row.Path ?? ""
            });
        }
    }
}
