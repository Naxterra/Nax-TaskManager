using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Interface text from Strings/&lt;language&gt;/Resources.resw. Only the app's own wording is translated: process names,
/// window titles, paths, host names and service names stay exactly as Windows reports them.
/// </summary>
internal static class Loc
{
    private static ResourceLoader? loader;

    /// <summary>
    /// Applies the language setting before any window exists. "System" follows the Windows display language;
    /// English is the fallback for languages without a translation.
    /// </summary>
    public static void ApplyLanguage(string setting)
    {
        string tag = setting is "en-US" or "de-DE" ? setting : "";
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Without the override the app follows Windows, which is the default anyway.
        }
        if (tag.Length > 0)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(tag);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }

    public static string Get(string key)
    {
        try
        {
            loader ??= new ResourceLoader();
            string value = loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return key;
        }
    }

    /// <summary>A resource with {0}-style placeholders.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    // Core keeps English wording because it also writes the history database; these map it for display.

    /// <summary>Connection state as reported by Core (TCP states, "Answered"/"No answer" for DNS rows).</summary>
    public static string State(string state) => state switch
    {
        "Established" => Get("State_Established"),
        "Listening" => Get("State_Listening"),
        "Time Wait" => Get("State_TimeWait"),
        "Close Wait" => Get("State_CloseWait"),
        "Closed" => Get("State_Closed"),
        "Closing" => Get("State_Closing"),
        "Syn Sent" => Get("State_SynSent"),
        "Syn Received" => Get("State_SynReceived"),
        "Fin Wait 1" => Get("State_FinWait1"),
        "Fin Wait 2" => Get("State_FinWait2"),
        "Last Ack" => Get("State_LastAck"),
        "Delete TCB" => Get("State_DeleteTcb"),
        "Answered" => Get("State_Answered"),
        "No answer" => Get("State_NoAnswer"),
        _ => state
    };

    /// <summary>Address scope label from <c>IpScopes.Label</c>.</summary>
    public static string Scope(string scope) => scope switch
    {
        "This PC" => Get("Scope_ThisPc"),
        "Link-local" => Get("Scope_LinkLocal"),
        "LAN" => Get("Scope_Lan"),
        "Internet" => Get("Scope_Internet"),
        "Multicast" => Get("Scope_Multicast"),
        _ => scope
    };

    /// <summary>App names that Core composes itself; real process and service names pass through unchanged.</summary>
    public static string AppName(string name)
    {
        const string ServiceHost = "Service Host: ", Exited = "Exited process (PID ";
        if (name == "Operating System") return Get("Name_OperatingSystem");
        if (name.StartsWith(ServiceHost, StringComparison.Ordinal)) return Get("Name_ServiceHost") + name[ServiceHost.Length..];
        if (name.StartsWith(Exited, StringComparison.Ordinal)) return Get("Name_ExitedPrefix") + name[Exited.Length..];
        return name;
    }

    /// <summary>Status of the per-app network measurement (<c>BandwidthMonitor.Status</c> or the service feed).</summary>
    public static string NetworkStatus(string status)
    {
        const string StartFailed = "Could not start the network trace: ", Stopped = "Network trace stopped: ";
        if (status.StartsWith(StartFailed, StringComparison.Ordinal)) return F("Status_StartFailed", status[StartFailed.Length..]);
        if (status.StartsWith(Stopped, StringComparison.Ordinal)) return F("Status_Stopped", status[Stopped.Length..]);
        return status switch
        {
            "Per-app network speed needs administrator rights." => Get("Status_NeedsAdmin"),
            "The network trace was stopped by another program; restarting." => Get("Status_Restarting"),
            "Measured by the Nax-TaskManager History service" => Get("Status_FromService"),
            "Running" => Get("Status_Running"),
            "Not started" => Get("Status_NotStarted"),
            _ => status
        };
    }

    public static string CleanupFailure(BetterTaskManager.Core.Native.CleanupResult result) => result.Failure switch
    {
        Core.Native.CleanupFailure.PrivilegeNotGranted => Get("Cleanup_PrivilegeNotGranted"),
        Core.Native.CleanupFailure.PrivilegeError => F("Cleanup_PrivilegeError", result.Code),
        Core.Native.CleanupFailure.PrivilegeNotHeld => Get("Cleanup_PrivilegeNotHeld"),
        Core.Native.CleanupFailure.AccessDenied => Get("Cleanup_AccessDenied"),
        Core.Native.CleanupFailure.Status => F("Cleanup_Status", result.Code),
        _ => result.Message
    };
}
