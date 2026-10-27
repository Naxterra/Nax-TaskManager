using System.Diagnostics;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Runs one of the app's helper modes (see <see cref="Program"/>) as administrator, always as a short-lived elevated
/// copy of the app. Without administrator rights Windows shows one UAC prompt; an elevated window starts it without one.
/// </summary>
internal static class ElevatedHelper
{
    /// <summary>Null when the helper succeeded, else a message for the user.</summary>
    public static Task<string?> RunAsync(params string[] arguments) => RunAsync(arguments, withResultFile: false);

    /// <summary>
    /// Like <see cref="RunAsync(string[])"/>; with <paramref name="withResultFile"/> the helper gets a file to write
    /// what failed into, and that text becomes the message.
    /// </summary>
    public static async Task<string?> RunAsync(string[] arguments, bool withResultFile)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        string? resultFile = withResultFile ? Path.Combine(Path.GetTempPath(), "nax-helper-" + Guid.NewGuid().ToString("N") + ".txt") : null;
        if (resultFile is not null) startInfo.ArgumentList.Add(resultFile);
        try
        {
            using Process helper = Process.Start(startInfo)!;
            await helper.WaitForExitAsync();
            if (helper.ExitCode == 0) return null;
            string details = resultFile is not null && File.Exists(resultFile) ? (await File.ReadAllTextAsync(resultFile)).Trim() : "";
            return details.Length > 0 ? details : Loc.F("Common_ExitCode", helper.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Loc.Get("Uac_CancelledNothing");
        }
        finally
        {
            if (resultFile is not null)
            {
                try { File.Delete(resultFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
