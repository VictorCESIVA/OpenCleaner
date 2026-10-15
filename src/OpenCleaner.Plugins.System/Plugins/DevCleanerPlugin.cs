using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class DevCleanerPlugin : SafeFileCleanerPlugin
{
    public DevCleanerPlugin(IFileGuardian fileGuardian, ILogger<DevCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "dev.cleaner";
    public override string Name => "Caches des développeurs";
    public override string Description => "Caches npm, Yarn, VS Code, applications et journaux JetBrains";
    public override PluginCategory Category => PluginCategory.System;
    public override RiskLevel MaxRiskLevel => RiskLevel.Safe;
    // Kept for API compatibility. These contain working configuration/dependencies, not disposable caches.
    public bool ScanNodeModules { get; set; }
    public bool ScanDocker { get; set; }
    public bool ScanNpmCache { get; set; } = true;
    public bool ScanYarnCache { get; set; } = true;
    public bool ScanNuGetCache { get; set; } = true;
    public bool ScanPipCache { get; set; } = true;
    public bool ScanGradleCache { get; set; } = true;
    public bool ScanVsCode { get; set; } = true;
    public bool ScanDiscord { get; set; } = true;
    public bool ScanSpotify { get; set; } = true;
    public bool ScanTeams { get; set; } = true;
    public bool ScanJetBrains { get; set; } = true;
    protected override bool CanClean() =>
        (!ScanVsCode || !SteamCleanerPlugin.IsRunning("Code")) &&
        (!ScanDiscord || !SteamCleanerPlugin.IsRunning("Discord")) &&
        (!ScanSpotify || !SteamCleanerPlugin.IsRunning("Spotify")) &&
        (!ScanTeams || (!SteamCleanerPlugin.IsRunning("ms-teams") && !SteamCleanerPlugin.IsRunning("Teams")));
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var age = TimeSpan.FromDays(7);
        if (ScanNpmCache)
        {
            yield return new ScanTarget(Path.Combine(local, "npm-cache", "_cacache"), "Cache npm", MinAge: age);
            yield return new ScanTarget(Path.Combine(roaming, "npm-cache", "_cacache"), "Cache npm", MinAge: age);
            yield return new ScanTarget(Path.Combine(local, "npm-cache", "_logs"), "Journal npm", MinAge: age);
        }
        if (ScanYarnCache) yield return new ScanTarget(Path.Combine(local, "Yarn", "Cache"), "Cache Yarn", MinAge: age);
        if (ScanNuGetCache) yield return new ScanTarget(Path.Combine(local, "NuGet", "v3-cache"), "Cache HTTP NuGet", MinAge: TimeSpan.FromDays(14));
        if (ScanPipCache)
        {
            yield return new ScanTarget(Path.Combine(local, "pip", "cache", "http"), "Cache HTTP pip", MinAge: TimeSpan.FromDays(14));
            yield return new ScanTarget(Path.Combine(local, "pip", "cache", "http-v2"), "Cache HTTP pip", MinAge: TimeSpan.FromDays(14));
        }
        if (ScanGradleCache && !string.IsNullOrWhiteSpace(profile))
        {
            yield return new ScanTarget(Path.Combine(profile, ".gradle", "daemon"), "Journal démon Gradle", Pattern: "*.out.log", MinAge: TimeSpan.FromDays(14));
            yield return new ScanTarget(Path.Combine(profile, ".gradle", "caches", "build-cache-1"), "Cache de build Gradle", MinAge: TimeSpan.FromDays(30));
        }
        if (ScanVsCode && !SteamCleanerPlugin.IsRunning("Code"))
            foreach (var directory in new[] { "logs", "CachedExtensionVSIXs", "Cache", "CachedData" })
                yield return new ScanTarget(Path.Combine(roaming, "Code", directory), "Cache VS Code", MinAge: age);
        if (ScanDiscord && !SteamCleanerPlugin.IsRunning("Discord"))
            foreach (var directory in new[] { "Cache", "Code Cache", "GPUCache" })
                yield return new ScanTarget(Path.Combine(roaming, "discord", directory), "Cache Discord", MinAge: age);
        if (ScanSpotify && !SteamCleanerPlugin.IsRunning("Spotify"))
            yield return new ScanTarget(Path.Combine(local, "Spotify", "Browser", "Cache"), "Cache Spotify", MinAge: age);
        if (ScanTeams && !SteamCleanerPlugin.IsRunning("ms-teams") && !SteamCleanerPlugin.IsRunning("Teams"))
        {
            foreach (var directory in new[] { "Cache", "Code Cache", "GPUCache" })
                yield return new ScanTarget(Path.Combine(roaming, "Microsoft", "Teams", directory), "Cache Teams", MinAge: age);
            yield return new ScanTarget(Path.Combine(local, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "Logs"),
                "Journaux Teams", Pattern: "*.log", MinAge: age);
        }
        if (ScanJetBrains)
            foreach (var ide in PluginFileSafety.GetEntries(Path.Combine(local, "JetBrains"), "*", directories: true))
                yield return new ScanTarget(Path.Combine(ide, "log"), "Journaux JetBrains", MinAge: TimeSpan.FromDays(30));
    }
}
