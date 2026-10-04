using System.Globalization;
using System.ServiceProcess;
using BetterTaskManager.Core.History;

namespace BetterTaskManager.HistoryService;

public static class Program
{
    /// <summary>
    /// Runs as the Windows service, or with <c>--console [--db path] [--seconds n]</c> in the foreground for testing.
    /// Console runs use their own ETW session names so they never stop a running service's traces.
    /// </summary>
    public static int Main(string[] args)
    {
        if (!args.Contains("--console"))
        {
            ServiceBase.Run(new HistoryWindowsService());
            return 0;
        }

        string database = Argument(args, "--db") ?? HistoryStore.DefaultPath;
        int seconds = int.TryParse(Argument(args, "--seconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        using var worker = new HistoryWorker(database, "NaxTaskManager-HistoryTest", Console.Error.WriteLine);
        worker.Start();
        Console.WriteLine($"Recording to {database}. " + (seconds > 0 ? $"Stopping after {seconds} s." : "Press Ctrl+C to stop."));
        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        stop.Wait(seconds > 0 ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
