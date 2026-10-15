using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class SystemTempPlugin : SafeFileCleanerPlugin
{
    public SystemTempPlugin(IFileGuardian fileGuardian, ILogger<SystemTempPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "system.temp";
    public override string Name => "Fichiers temporaires";
    public override string Description => "Fichiers temporaires anciens, hors Prefetch";
    public override PluginCategory Category => PluginCategory.System;
    public override RiskLevel MaxRiskLevel => RiskLevel.Recommended;

    protected override IEnumerable<ScanTarget> GetTargets()
    {
        yield return new ScanTarget(Path.GetTempPath(), "Fichier temporaire", MinAge: TimeSpan.FromHours(24));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
            yield return new ScanTarget(Path.Combine(windows, "Temp"), "Temporaire Windows",
                RiskLevel.Recommended, MinAge: TimeSpan.FromDays(7));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            yield return new ScanTarget(Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                "Rapport d'erreur Windows archivé", RiskLevel.Safe, MinAge: TimeSpan.FromDays(7));
            yield return new ScanTarget(Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
                "Rapport d'erreur Windows en attente", RiskLevel.Safe, MinAge: TimeSpan.FromDays(7));
            yield return new ScanTarget(Path.Combine(local, "CrashDumps"),
                "Vidage mémoire d'application (CrashDump)", RiskLevel.Recommended, Pattern: "*.dmp", MinAge: TimeSpan.FromDays(7));
            yield return new ScanTarget(Path.Combine(local, "D3DSCache"),
                "Cache de shaders DirectX", RiskLevel.Safe, MinAge: TimeSpan.FromDays(7));
        }
    }
}
