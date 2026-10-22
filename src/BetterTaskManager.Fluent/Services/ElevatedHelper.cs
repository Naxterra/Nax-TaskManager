using System.Diagnostics;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Runs one of the app's helper modes (see <see cref="Program"/>) as administrator: directly when this window is
/// elevated, otherwise as a short-lived elevated copy of the app, so Windows shows one UAC prompt.
/// </summary>
internal static class ElevatedHelper
{
    /// <summary>Null when the helper succeeded, else a message for the user.</summary>
    public static async Task<string?> RunAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        try
        {
            using Process helper = Process.Start(startInfo)!;
            await helper.WaitForExitAsync();
            return helper.ExitCode == 0 ? null : Loc.F("Common_ExitCode", helper.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Loc.Get("Uac_CancelledNothing");
        }
    }
}
