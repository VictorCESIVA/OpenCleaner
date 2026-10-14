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
    public bool ScanVsCode { get; set; } = true;
    public bool ScanDiscord { get; set; } = true;
    public bool ScanSpotify { get; set; } = true;
    public bool ScanJetBrains { get; set; } = true;
    protected override bool CanClean() =>
        (!ScanVsCode || !SteamCleanerPlugin.IsRunning("Code")) &&
        (!ScanDiscord || !SteamCleanerPlugin.IsRunning("Discord")) &&
        (!ScanSpotify || !SteamCleanerPlugin.IsRunning("Spotify"));
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var age = TimeSpan.FromDays(7);
        if (ScanNpmCache)
        {
            yield return new ScanTarget(Path.Combine(local, "npm-cache", "_cacache"), "Cache npm", MinAge: age);
            yield return new ScanTarget(Path.Combine(roaming, "npm-cache", "_cacache"), "Cache npm", MinAge: age);
            yield return new ScanTarget(Path.Combine(local, "npm-cache", "_logs"), "Journal npm", MinAge: age);
        }
        if (ScanYarnCache) yield return new ScanTarget(Path.Combine(local, "Yarn", "Cache"), "Cache Yarn", MinAge: age);
        if (ScanVsCode && !SteamCleanerPlugin.IsRunning("Code"))
            foreach (var directory in new[] { "logs", "CachedExtensionVSIXs", "Cache", "CachedData" })
                yield return new ScanTarget(Path.Combine(roaming, "Code", directory), "Cache VS Code", MinAge: age);
        if (ScanDiscord && !SteamCleanerPlugin.IsRunning("Discord"))
            foreach (var directory in new[] { "Cache", "Code Cache", "GPUCache" })
                yield return new ScanTarget(Path.Combine(roaming, "discord", directory), "Cache Discord", MinAge: age);
        if (ScanSpotify && !SteamCleanerPlugin.IsRunning("Spotify"))
            yield return new ScanTarget(Path.Combine(local, "Spotify", "Browser", "Cache"), "Cache Spotify", MinAge: age);
        if (ScanJetBrains)
            foreach (var ide in PluginFileSafety.GetEntries(Path.Combine(local, "JetBrains"), "*", directories: true))
                yield return new ScanTarget(Path.Combine(ide, "log"), "Journaux JetBrains", MinAge: TimeSpan.FromDays(30));
    }
}
