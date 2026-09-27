using System.Diagnostics;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;

namespace BetterTaskManager.Fluent;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;
    public static MonitorHost Monitor { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;

    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            throw;
        }

        UnhandledException += (_, args) => CrashLog.Write(args.Exception);
        Settings = AppSettings.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Monitor = new MonitorHost(Settings);
            Window = new MainWindow();
            Window.Closed += (_, _) =>
            {
                Settings.Save();
                Monitor.Dispose();
            };
            Window.Activate();
            Monitor.Start(Window.DispatcherQueue);
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            throw;
        }
    }

    /// <summary>Starts an elevated copy that waits for this one to exit, then closes this window.</summary>
    public static bool RestartElevated()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;

        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add(Program.WaitForProcessArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC prompt cancelled.
        }

        Program.ReleaseSingleInstance();
        Window.Close();
        return true;
    }
}
