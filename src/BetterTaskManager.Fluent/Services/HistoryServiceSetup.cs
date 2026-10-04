using System.Diagnostics;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Turns background recording on or off. Without elevation this starts a short-lived elevated copy of the app
/// (one UAC prompt), which reports failures through a result file because an exit code cannot carry the reason.
/// </summary>
internal static class HistoryServiceSetup
{
    public const string InstallArgument = "--install-history-service";
    public const string UninstallArgument = "--uninstall-history-service";

    /// <summary>The service build that ships next to the app.</summary>
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, HistoryServiceControl.BundledFolderName);

    /// <summary>Elevated helper mode. Writes the failure reason, if any, to <paramref name="resultFile"/>.</summary>
    public static int RunHelper(string argument, string resultFile)
    {
        CommandResult result = Apply(argument == InstallArgument);
        if (!result.Succeeded)
        {
            try
            {
                File.WriteAllText(resultFile, result.FailureSummary());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return result.Succeeded ? 0 : Math.Max(1, result.ExitCode);
    }

    private static CommandResult Apply(bool enable) =>
        enable ? HistoryServiceControl.Install(BundledFolder) : HistoryServiceControl.Uninstall();

    /// <summary>Returns null on success, otherwise a message for the user.</summary>
    public static async Task<string?> SetEnabledAsync(bool enable, bool elevated)
    {
        if (elevated)
        {
            CommandResult result = await Task.Run(() => Apply(enable));
            return result.Succeeded ? null : result.FailureSummary();
        }

        string resultFile = Path.Combine(Path.GetTempPath(), $"NaxTaskManager-service-{Guid.NewGuid():N}.txt");
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(enable ? InstallArgument : UninstallArgument);
        startInfo.ArgumentList.Add(resultFile);
        try
        {
            using Process helper = Process.Start(startInfo)!;
            await helper.WaitForExitAsync();
            if (helper.ExitCode == 0) return null;
            string reason = File.Exists(resultFile) ? await File.ReadAllTextAsync(resultFile) : Loc.F("Common_ExitCode", helper.ExitCode);
            return reason;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Loc.Get("Uac_CancelledNothing");
        }
        finally
        {
            try
            {
                File.Delete(resultFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
