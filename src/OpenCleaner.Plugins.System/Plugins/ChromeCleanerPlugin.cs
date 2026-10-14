using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class ChromeCleanerPlugin : SafeFileCleanerPlugin
{
    public ChromeCleanerPlugin(IFileGuardian fileGuardian, ILogger<ChromeCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "browser.chrome";
    public override string Name => "Chrome, Edge et Brave";
    public override string Description => "Caches des profils ; historique et cookies seulement sur demande, navigateur fermé";
    public override PluginCategory Category => PluginCategory.Browser;
    public override RiskLevel MaxRiskLevel => RiskLevel.Recommended;
    protected override bool CanClean() => !SteamCleanerPlugin.IsRunning("chrome") &&
        !SteamCleanerPlugin.IsRunning("msedge") && !SteamCleanerPlugin.IsRunning("brave");
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browsers = new[]
        {
            (Path: Path.Combine(local, "Google", "Chrome", "User Data"), Name: "Chrome", Process: "chrome"),
            (Path: Path.Combine(local, "Microsoft", "Edge", "User Data"), Name: "Edge", Process: "msedge"),
            (Path: Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), Name: "Brave", Process: "brave")
        };
        foreach (var browser in browsers)
        {
            if (SteamCleanerPlugin.IsRunning(browser.Process)) continue;
            var profiles = PluginFileSafety.GetEntries(browser.Path, "*", directories: true)
                .Where(p => Path.GetFileName(p).Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(p).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));
            foreach (var profile in profiles)
            {
                var desc = $"{browser.Name} ({Path.GetFileName(profile)})";
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
    }
}
