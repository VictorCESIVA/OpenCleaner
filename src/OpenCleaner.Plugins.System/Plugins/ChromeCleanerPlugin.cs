using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class ChromeCleanerPlugin : SafeFileCleanerPlugin
{
    private static readonly string[] BrowserProcesses = ["chrome", "msedge", "brave", "firefox", "vivaldi", "opera"];

    public ChromeCleanerPlugin(IFileGuardian fileGuardian, ILogger<ChromeCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "browser.chrome";
    public override string Name => "Navigateurs (Chrome, Edge, Brave, Firefox, Opera, Vivaldi)";
    public override string Description => "Caches des profils ; historique et cookies seulement sur demande, navigateur fermé";
    public override PluginCategory Category => PluginCategory.Browser;
    public override RiskLevel MaxRiskLevel => RiskLevel.Recommended;

    public static bool IsAnyBrowserRunning()
    {
        foreach (var proc in BrowserProcesses)
        {
            if (SteamCleanerPlugin.IsRunning(proc)) return true;
        }
        return false;
    }

    protected override bool CanClean() => !IsAnyBrowserRunning();

    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var chromiumBrowsers = new[]
        {
            (Path: Path.Combine(local, "Google", "Chrome", "User Data"), Name: "Chrome", Process: "chrome"),
            (Path: Path.Combine(local, "Microsoft", "Edge", "User Data"), Name: "Edge", Process: "msedge"),
            (Path: Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), Name: "Brave", Process: "brave"),
            (Path: Path.Combine(local, "Vivaldi", "User Data"), Name: "Vivaldi", Process: "vivaldi")
        };
        foreach (var browser in chromiumBrowsers)
        {
            if (SteamCleanerPlugin.IsRunning(browser.Process)) continue;
            var profiles = PluginFileSafety.GetEntries(browser.Path, "*", directories: true)
                .Where(p => Path.GetFileName(p).Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(p).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));
            foreach (var profile in profiles)
            {
                var desc = $"{browser.Name} ({Path.GetFileName(profile)})";
                foreach (var target in GetChromiumProfileTargets(profile, desc))
                    yield return target;
            }
        }

        // Opera / Opera GX store profile data directly in their root profile directory
        if (!SteamCleanerPlugin.IsRunning("opera"))
        {
            var operaProfiles = new[]
            {
                (Path: Path.Combine(local, "Opera Software", "Opera Stable"), RoamingPath: Path.Combine(roaming, "Opera Software", "Opera Stable"), Name: "Opera"),
                (Path: Path.Combine(local, "Opera Software", "Opera GX Stable"), RoamingPath: Path.Combine(roaming, "Opera Software", "Opera GX Stable"), Name: "Opera GX")
            };
            foreach (var opera in operaProfiles)
            {
                foreach (var target in GetChromiumProfileTargets(opera.Path, opera.Name))
                    yield return target;
                foreach (var target in GetChromiumProfileTargets(opera.RoamingPath, opera.Name))
                    yield return target;
            }
        }

        // Mozilla Firefox (local cache2/startupCache/thumbnails/shader-cache + roaming cookies/favicons)
        if (!SteamCleanerPlugin.IsRunning("firefox"))
        {
            var firefoxLocalProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
            foreach (var profile in PluginFileSafety.GetEntries(firefoxLocalProfiles, "*", directories: true))
            {
                var desc = $"Firefox ({Path.GetFileName(profile)})";
                if (BrowserCleanOptions.IncludeCache)
                {
                    yield return new ScanTarget(Path.Combine(profile, "cache2", "entries"), $"{desc} — cache", MinAge: TimeSpan.FromHours(1));
                    yield return new ScanTarget(Path.Combine(profile, "startupCache"), $"{desc} — cache démarrage", MinAge: TimeSpan.FromHours(1));
                    yield return new ScanTarget(Path.Combine(profile, "thumbnails"), $"{desc} — miniatures", MinAge: TimeSpan.FromHours(1));
                }
                if (BrowserCleanOptions.IncludeGpuCache)
                    yield return new ScanTarget(Path.Combine(profile, "shader-cache"), $"{desc} — cache GPU", MinAge: TimeSpan.FromHours(1));
            }

            var firefoxRoamingProfiles = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
            foreach (var profile in PluginFileSafety.GetEntries(firefoxRoamingProfiles, "*", directories: true))
            {
                var desc = $"Firefox ({Path.GetFileName(profile)})";
                if (BrowserCleanOptions.IncludeCookies)
                    yield return new ScanTarget(profile, $"{desc} — cookies", RiskLevel.Recommended, "cookies.sqlite", Recursive: false);
                if (BrowserCleanOptions.IncludeFavicons)
                    yield return new ScanTarget(profile, $"{desc} — icônes des sites", RiskLevel.Safe, "favicons.sqlite", Recursive: false);
            }
        }
    }

    private static IEnumerable<ScanTarget> GetChromiumProfileTargets(string profile, string desc)
    {
        if (!Directory.Exists(profile)) yield break;
        if (BrowserCleanOptions.IncludeCache)
            yield return new ScanTarget(Path.Combine(profile, "Cache"), $"{desc} — cache", MinAge: TimeSpan.FromHours(1));
        if (BrowserCleanOptions.IncludeCodeCache)
            yield return new ScanTarget(Path.Combine(profile, "Code Cache"), $"{desc} — cache JS", MinAge: TimeSpan.FromHours(1));
        if (BrowserCleanOptions.IncludeGpuCache)
            yield return new ScanTarget(Path.Combine(profile, "GPUCache"), $"{desc} — cache GPU", MinAge: TimeSpan.FromHours(1));
        if (BrowserCleanOptions.IncludeServiceWorkers)
            yield return new ScanTarget(Path.Combine(profile, "Service Worker", "CacheStorage"),
                $"{desc} — cache hors connexion", RiskLevel.Recommended, MinAge: TimeSpan.FromDays(7));
        if (BrowserCleanOptions.IncludeCookies)
        {
            yield return new ScanTarget(profile, $"{desc} — cookies", RiskLevel.Recommended, "Cookies", Recursive: false);
            yield return new ScanTarget(Path.Combine(profile, "Network"), $"{desc} — cookies", RiskLevel.Recommended, "Cookies", Recursive: false);
        }
        if (BrowserCleanOptions.IncludeHistory)
            yield return new ScanTarget(profile, $"{desc} — historique", RiskLevel.Recommended, "History", Recursive: false);
        if (BrowserCleanOptions.IncludeFavicons)
            yield return new ScanTarget(profile, $"{desc} — icônes des sites", Pattern: "Favicons", Recursive: false);
    }
}
