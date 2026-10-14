using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>Windows Update's database and downloads are managed by Windows itself.</summary>
public sealed class WindowsUpdatePlugin : SafeFileCleanerPlugin
{
    public WindowsUpdatePlugin(IFileGuardian fileGuardian, ILogger<WindowsUpdatePlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "system.windowsupdate";
    public override string Name => "Windows Update";
    public override string Description => "Utilisez Paramètres Windows > Stockage > Fichiers temporaires pour les mises à jour";
    public override PluginCategory Category => PluginCategory.System;
    public override RiskLevel MaxRiskLevel => RiskLevel.ExpertOnly;
    // File ages do not establish that an update is installed or that servicing is inactive.
    public override bool IsAvailable => false;
    public override bool RequiresAdmin => true;
    protected override IEnumerable<ScanTarget> GetTargets() => [];
}
