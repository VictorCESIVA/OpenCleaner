using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class OfficeCleanerPlugin : SafeFileCleanerPlugin
{
    public OfficeCleanerPlugin(IFileGuardian fileGuardian, ILogger<OfficeCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "applications.office";
    public override string Name => "Journaux Microsoft Office";
    public override string Description => "Journaux anciens ; documents de récupération et cache de synchronisation conservés";
    public override PluginCategory Category => PluginCategory.Applications;
    public override RiskLevel MaxRiskLevel => RiskLevel.Safe;
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        var office = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Office", "16.0");
        // AutoRecover, roaming .xlsb files and OfficeFileCache may hold the only copy of user work.
        yield return new ScanTarget(Path.Combine(office, "Telemetry"), "Journal Office",
            Pattern: "*.log", MinAge: TimeSpan.FromDays(30));
    }
}
