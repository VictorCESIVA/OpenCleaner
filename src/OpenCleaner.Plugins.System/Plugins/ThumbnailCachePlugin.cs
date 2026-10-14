using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class ThumbnailCachePlugin : SafeFileCleanerPlugin
{
    public ThumbnailCachePlugin(IFileGuardian fileGuardian, ILogger<ThumbnailCachePlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "system.thumbnailcache";
    public override string Name => "Miniatures et icônes";
    public override string Description => "Caches régénérables de l'Explorateur, lorsqu'ils ne sont pas verrouillés";
    public override PluginCategory Category => PluginCategory.System;
    public override RiskLevel MaxRiskLevel => RiskLevel.Safe;
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");
        yield return new ScanTarget(path, "Miniatures Explorer", Pattern: "thumbcache_*.db", Recursive: false);
        yield return new ScanTarget(path, "Icônes Explorer", Pattern: "iconcache*.db", Recursive: false);
    }
}
