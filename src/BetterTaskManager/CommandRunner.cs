using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace BetterTaskManager
{
    internal sealed class CommandResult
    {
        public CommandResult(int exitCode, string standardOutput, string standardError, bool timedOut)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? "";
            StandardError = standardError ?? "";
            TimedOut = timedOut;
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool TimedOut { get; }
        public bool Succeeded => !TimedOut && ExitCode == 0;

        public string CombinedOutput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StandardError)) return StandardOutput;
                if (string.IsNullOrWhiteSpace(StandardOutput)) return StandardError;
                return StandardOutput.TrimEnd() + Environment.NewLine + StandardError;
            }
        }

        public string FailureSummary()
        {
            if (TimedOut) return "The Windows command timed out after 15 seconds.";

            string detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            detail = (detail ?? "").Trim();
            if (detail.Length > 1500) detail = string.Concat(detail.AsSpan(0, 1500), "...");

            string summary = "Windows returned exit code " + ExitCode + ".";
            return string.IsNullOrWhiteSpace(detail) ? summary : summary + Environment.NewLine + detail;
        }
    }

    internal static class CommandRunner
    {
        private const int DefaultTimeoutMilliseconds = 15000;

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Command, termination, and stream failures are converted into a result that the UI can report safely.")]
        public static CommandResult Run(string file, params string[] arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string argument in arguments ?? Array.Empty<string>())
            {
                psi.ArgumentList.Add(argument ?? "");
            }

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    if (!process.Start()) return new CommandResult(-1, "", "Windows did not start the command.", false);

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    bool exited = process.WaitForExit(DefaultTimeoutMilliseconds);

                    if (!exited)
                    {
                        try { process.Kill(true); } catch { }
                        try { process.WaitForExit(2000); } catch { }

                        bool streamsCompleted = false;
                        try { streamsCompleted = Task.WaitAll(new Task[] { outputTask, errorTask }, 2000); } catch { }
                        string timedOutOutput = streamsCompleted ? outputTask.GetAwaiter().GetResult() : "";
                        string timedOutError = streamsCompleted ? errorTask.GetAwaiter().GetResult() : "Output capture did not finish after the command timed out.";
                        return new CommandResult(-1, timedOutOutput, timedOutError, true);
                    }

                    string output = outputTask.GetAwaiter().GetResult();
                    string error = errorTask.GetAwaiter().GetResult();
                    return new CommandResult(process.ExitCode, output, error, false);
                }
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, "", ex.Message, false);
            }
        }
    }
}
