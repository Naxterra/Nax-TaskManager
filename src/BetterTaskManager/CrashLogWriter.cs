using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BetterTaskManager
{
    internal static class CrashLogWriter
    {
        internal const int DefaultMaximumBytes = 1024 * 1024;

        public static void Append(string logPath, string entry, int maximumBytes = DefaultMaximumBytes)
        {
            if (string.IsNullOrWhiteSpace(logPath)) throw new ArgumentException("A crash log path is required.", nameof(logPath));
            if (maximumBytes < 128) throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            string fullPath = Path.GetFullPath(logPath);
            string folder = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("The crash log path has no parent folder.");
            Directory.CreateDirectory(folder);

            byte[] bytes = BoundedUtf8(entry ?? "", maximumBytes);
            string mutexName = BuildMutexName(fullPath);
            using (var mutex = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Timed out waiting for another Better Task Manager instance to release the crash log.");

                    if (File.Exists(fullPath) && new FileInfo(fullPath).Length + bytes.Length > maximumBytes)
                    {
                        string previousPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(fullPath) + ".previous" + Path.GetExtension(fullPath));
                        File.Move(fullPath, previousPath, true);
                    }

                    using (var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                finally
                {
                    if (acquired) mutex.ReleaseMutex();
                }
            }
        }

        private static byte[] BoundedUtf8(string value, int maximumBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= maximumBytes) return bytes;

            byte[] marker = Encoding.UTF8.GetBytes(Environment.NewLine + "[Crash entry truncated to fit log limit]" + Environment.NewLine);
            int contentLength = Math.Max(0, maximumBytes - marker.Length);
            int safeCharacterCount = Math.Min(value.Length, contentLength / 3);
            byte[] prefix = Encoding.UTF8.GetBytes(value.Substring(0, safeCharacterCount));
            var bounded = new byte[prefix.Length + marker.Length];
            Buffer.BlockCopy(prefix, 0, bounded, 0, prefix.Length);
            Buffer.BlockCopy(marker, 0, bounded, prefix.Length, marker.Length);
            return bounded;
        }

        private static string BuildMutexName(string path)
        {
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
                return "Local\\BetterTaskManager-CrashLog-" + hash.Substring(0, 24);
            }
        }
    }
}
