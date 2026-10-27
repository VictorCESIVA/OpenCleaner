using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class OfficeCleanerPlugin : SafeFileCleanerPlugin
{
    public OfficeCleanerPlugin(IFileGuardian fileGuardian, ILogger<OfficeCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "applications.office";
    public override string Name => "Office et Thunderbird";
    public override string Description => "Journaux Office et caches Thunderbird ; documents de récupération conservés";
    public override PluginCategory Category => PluginCategory.Applications;
    public override RiskLevel MaxRiskLevel => RiskLevel.Safe;
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var office = Path.Combine(local, "Microsoft", "Office", "16.0");
        // AutoRecover, roaming .xlsb files and OfficeFileCache may hold the only copy of user work.
        yield return new ScanTarget(Path.Combine(office, "Telemetry"), "Journal Office",
            Pattern: "*.log", MinAge: TimeSpan.FromDays(30));

        var age = TimeSpan.FromDays(7);
        foreach (string root in new[]
                 {
                     Path.Combine(local, "Thunderbird", "Profiles"),
                     Path.Combine(roaming, "Thunderbird", "Profiles")
                 })
        {
            foreach (string profile in PluginFileSafety.GetEntries(root, "*", directories: true))
            {
                yield return new ScanTarget(Path.Combine(profile, "cache2", "entries"),
                    "Cache Thunderbird", MinAge: age);
                yield return new ScanTarget(Path.Combine(profile, "startupCache"),
                    "Cache de démarrage Thunderbird", MinAge: age);
                yield return new ScanTarget(Path.Combine(profile, "thumbnails"),
                    "Miniatures Thunderbird", MinAge: age);
            }
        }
    }
}
