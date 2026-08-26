using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterTaskManager
{
    internal static class CsvFileWriter
    {
        public static void Write(string path, IEnumerable<IEnumerable<string>> rows)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An export path is required.", nameof(path));
            ArgumentNullException.ThrowIfNull(rows);

            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                foreach (IEnumerable<string> row in rows)
                {
                    writer.WriteLine(string.Join(",", (row ?? Array.Empty<string>()).Select(Escape)));
                }
            }
        }

        internal static string Escape(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
    }
}
