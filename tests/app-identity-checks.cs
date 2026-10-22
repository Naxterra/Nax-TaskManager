#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// App identity: WebView2 and CefSharp helpers belong to the app that started them. Pure fixtures.
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Monitoring;

int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}

ProcessSample P(int pid, int parent, string image, string path, long created) => new()
{
    Pid = pid, ParentPid = parent, ImageName = image, Path = path, CreateTime = created, Description = "", Company = ""
};

const string WebView = @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\155.0\msedgewebview2.exe";
var whatsapp = P(100, 50, "WhatsApp.Root.exe", @"C:\Program Files\WindowsApps\WhatsApp\WhatsApp.Root.exe", 10);
var browser = P(200, 100, "msedgewebview2.exe", WebView, 20);
var renderer = P(300, 200, "msedgewebview2.exe", WebView, 30);
var service = P(400, 4, "svchost.exe", @"C:\Windows\System32\svchost.exe", 5);
var serviceWebView = P(500, 400, "msedgewebview2.exe", WebView, 40);
var reused = P(600, 700, "msedgewebview2.exe", WebView, 50);
var newerParent = P(700, 1, "Late.exe", @"C:\Late.exe", 60);
var cef = P(800, 900, "CefSharp.BrowserSubprocess.exe", @"C:\Program Files\DeepL\CefSharp.BrowserSubprocess.exe", 80);
var deepl = P(900, 1, "DeepL.exe", @"C:\Program Files\DeepL\DeepL.exe", 70);
var steam = P(1000, 17208, "steam.exe", @"E:\Steam\steam.exe", 90);
var steamUi = P(1100, 1000, "steamwebhelper.exe", @"E:\Steam\bin\cef\cef.win64\steamwebhelper.exe", 95);
var steamRenderer = P(1200, 1100, "steamwebhelper.exe", @"E:\Steam\bin\cef\cef.win64\steamwebhelper.exe", 96);
var byPid = new[] { whatsapp, browser, renderer, service, serviceWebView, reused, newerParent, cef, deepl, steam, steamUi, steamRenderer }.ToDictionary(p => p.Pid);

Check("a WebView2 child belongs to its app", AppIdentityRules.Owner(browser, byPid) == whatsapp);
Check("a WebView2 grandchild belongs to the same app", AppIdentityRules.Owner(renderer, byPid) == whatsapp);
Check("the app itself is its own owner", AppIdentityRules.Owner(whatsapp, byPid) == whatsapp);
Check("a WebView2 started by a service stays its own app", AppIdentityRules.Owner(serviceWebView, byPid) == serviceWebView);
Check("a parent that started later is a reused PID", AppIdentityRules.Owner(reused, byPid) == reused);
Check("a CefSharp helper belongs to its app", AppIdentityRules.Owner(cef, byPid) == deepl);
Check("Steam's web helpers belong to Steam", AppIdentityRules.Owner(steamUi, byPid) == steam && AppIdentityRules.Owner(steamRenderer, byPid) == steam);
Check("the history key of a helper is the app's", AppIdentityRules.AppKey(AppIdentityRules.Owner(renderer, byPid)) == AppIdentityRules.AppKey(whatsapp));

var policy = new AppNetworkPolicy { Path = steam.Path, BlockInternet = true, Outgoing = ["+ LAN", "- *"], Helpers = [steamUi.Path] };
AppNetworkPolicy derived = policy.ForHelper(steamUi.Path);
Check("a helper gets the app's switches and rules", derived.Path == steamUi.Path && derived.BlockInternet && derived.Outgoing.SequenceEqual(policy.Outgoing) && derived.HelperOf == steam.Path && derived.Helpers.Count == 0);
Check("helpers alone keep a policy but need no filters", new AppNetworkPolicy { Path = steam.Path, Helpers = [steamUi.Path] } is { IsEmpty: false, HasFilters: false });
var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AppNetworkPolicy>(System.Text.Json.JsonSerializer.Serialize(derived))!;
Check("helpers and owner survive JSON", roundTrip.HelperOf == steam.Path && System.Text.Json.JsonSerializer.Deserialize<AppNetworkPolicy>(System.Text.Json.JsonSerializer.Serialize(policy))!.Helpers.SequenceEqual(policy.Helpers));

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
