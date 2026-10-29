using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

internal enum AppCacheRoot
{
    LocalApplicationData,
    ApplicationData,
    UserProfile
}

/// <summary>Répertoire de cache d'une application, relative à un dossier utilisateur connu.</summary>
internal sealed record AppCacheTarget(
    string AppName,
    AppCacheRoot Root,
    string[] Segments,
    string Description,
    TimeSpan MinAge,
    string? ProcessName = null);

/// <summary>
/// Caches et journaux d'applications tierces (Electron, Store, OneDrive, rapports de plantage).
/// Aucune donnée utilisateur : uniquement des dossiers de cache et de journaux reconstruits par l'application.
/// </summary>
public sealed class AppCacheCleanerPlugin : SafeFileCleanerPlugin
{
    private static readonly TimeSpan CacheAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(30);

    internal static IReadOnlyList<AppCacheTarget> CacheTargets { get; } = BuildTargets();

    public AppCacheCleanerPlugin(IFileGuardian fileGuardian, ILogger<AppCacheCleanerPlugin> logger)
        : base(fileGuardian, logger) { }

    public override string Id => "applications.caches";
    public override string Name => "Caches d'applications";
    public override string Description =>
        "Caches et journaux de Slack, Notion, WhatsApp, Signal, Zoom, Microsoft Store, OneDrive et rapports de plantage";
    public override PluginCategory Category => PluginCategory.Applications;
    public override RiskLevel MaxRiskLevel => RiskLevel.Safe;

    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var roots = new Dictionary<AppCacheRoot, string>
        {
            [AppCacheRoot.LocalApplicationData] =
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            [AppCacheRoot.ApplicationData] =
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            [AppCacheRoot.UserProfile] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var target in CacheTargets)
        {
            if (target.ProcessName != null && SteamCleanerPlugin.IsRunning(target.ProcessName)) continue;
            if (!roots.TryGetValue(target.Root, out string? root) || string.IsNullOrWhiteSpace(root)) continue;
            yield return new ScanTarget(Path.Combine([root, .. target.Segments]), target.Description,
                MinAge: target.MinAge);
        }
    }

    private static IReadOnlyList<AppCacheTarget> BuildTargets()
    {
        var targets = new List<AppCacheTarget>();
        AddElectronCaches(targets, "Slack", "slack");
        AddElectronCaches(targets, "Notion", "Notion");
        AddElectronCaches(targets, "WhatsApp", "WhatsApp");
        AddElectronCaches(targets, "Signal", "Signal");

        targets.Add(new AppCacheTarget("Zoom", AppCacheRoot.ApplicationData, ["Zoom", "logs"],
            "Journaux Zoom", LogAge, "Zoom"));
        targets.Add(new AppCacheTarget("Microsoft Store", AppCacheRoot.LocalApplicationData,
            ["Packages", "Microsoft.WindowsStore_8wekyb3d8bbwe", "LocalCache"], "Cache du Microsoft Store",
            CacheAge));
        targets.Add(new AppCacheTarget("OneDrive", AppCacheRoot.LocalApplicationData,
            ["Microsoft", "OneDrive", "logs"], "Journaux OneDrive", LogAge));
        targets.Add(new AppCacheTarget("OneDrive", AppCacheRoot.LocalApplicationData,
            ["Microsoft", "OneDrive", "setup", "logs"], "Journaux d'installation OneDrive", LogAge));
        targets.Add(new AppCacheTarget("Rapports de plantage", AppCacheRoot.LocalApplicationData,
            ["Crashpad", "reports"], "Rapport de plantage d'application", CacheAge));
        return targets;
    }

    private static void AddElectronCaches(List<AppCacheTarget> targets, string appName, string processName)
    {
        foreach (string directory in new[] { "Cache", "Code Cache", "GPUCache" })
            targets.Add(new AppCacheTarget(appName, AppCacheRoot.ApplicationData, [appName, directory],
                $"Cache {appName}", CacheAge, processName));
        targets.Add(new AppCacheTarget(appName, AppCacheRoot.ApplicationData, [appName, "logs"],
            $"Journaux {appName}", CacheAge, processName));
    }
}
