using System.Diagnostics;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Native;

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
        if (!result.Succeeded) Core.HelperResultFile.TryWrite(resultFile, result.FailureSummary());
        return result.Succeeded ? 0 : Math.Max(1, result.ExitCode);
    }

    private static CommandResult Apply(bool enable) =>
        enable ? HistoryServiceControl.Install(BundledFolder) : HistoryServiceControl.Uninstall(keepFolder: BundledFolder);

    /// <summary>Returns null on success, otherwise a message for the user.</summary>
    public static async Task<string?> SetEnabledAsync(bool enable, bool elevated)
    {
        if (enable && !ProtectedLocation.IsAdminOnlyDirectory(BundledFolder))
            return Loc.Get("HistoryService_ProtectedFolderRequired");

        if (elevated)
        {
            CommandResult result = await Task.Run(() => Apply(enable));
            return result.Succeeded ? null : result.FailureSummary();
        }

        return await ElevatedHelper.RunAsync([enable ? InstallArgument : UninstallArgument], withResultFile: true);
    }
}
