using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace OpenCleaner.Plugins.System.Plugins;

[SupportedOSPlatform("windows")]
public class RegistryCleanerPlugin : ICleanerPlugin
{
    private readonly IRegistryGuardian _registryGuardian;
    private readonly ILogger<RegistryCleanerPlugin> _logger;

    public string Id => "registry.safe";
    public string Name => "Nettoyeur de registre (Sécurisé)";
    public string Description => "Nettoie les entrées de registre orphelines et les caches applicatifs (sécurisé uniquement)";
    public PluginCategory Category => PluginCategory.Registry;
    public RiskLevel MaxRiskLevel => RiskLevel.ExpertOnly;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public RegistryCleanerPlugin(IRegistryGuardian registryGuardian, ILogger<RegistryCleanerPlugin> logger)
    {
        _registryGuardian = registryGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        var targets = new[]
        {
            (Root: Registry.CurrentUser, Path: @"Software\Microsoft\Windows\CurrentVersion\Uninstall", Safe: true),
            (Root: Registry.CurrentUser, Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\StreamMRU", Safe: true),
            (Root: Registry.CurrentUser, Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU", Safe: true)
        };

        int current = 0;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var key = target.Root.OpenSubKey(target.Path);
                if (key == null) continue;

                var subKeyNames = key.GetSubKeyNames();
                foreach (var subKeyName in subKeyNames)
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        if (target.Path.Contains("Uninstall"))
                        {
                            var uninstallString = subKey.GetValue("UninstallString") as string;
                            if (!string.IsNullOrEmpty(uninstallString))
                            {
                                var programPath = ExtractPath(uninstallString);
                                if (!string.IsNullOrEmpty(programPath) && !File.Exists(programPath))
                                {
                                    var displayName = subKey.GetValue("DisplayName") as string ?? subKeyName;
                                    items.Add(new CleanableItem(
                                        Id: Guid.NewGuid().ToString(),
                                        Path: $@"{target.Root.Name}\{target.Path}\{subKeyName}",
                                        Size: 0,
                                        Description: $"{displayName} (application désinstallée)",
                                        RiskLevel: RiskLevel.ExpertOnly,
                                        Type: ItemType.RegistryKey,
                                        ParentPluginId: Id
                                    ));
                                }
                            }
                        }
                        else if (target.Path.Contains("MRU"))
                        {
                            items.Add(new CleanableItem(
                                Id: Guid.NewGuid().ToString(),
                                Path: $@"{target.Root.Name}\{target.Path}\{subKeyName}",
                                Size: 0,
                                Description: $"Historique {subKeyName}",
                                RiskLevel: RiskLevel.Recommended,
                                Type: ItemType.RegistryKey,
                                ParentPluginId: Id
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Erreur lecture sous-clé {SubKey}", subKeyName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur accès {Path}", target.Path);
            }

            current++;
            progress?.Report((double)current / targets.Length);
        }

        return items;
    }

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var itemsList = items.ToList();
        int successCount = 0;
        int current = 0;

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_registryGuardian.IsSystemCriticalKey(item.Path))
                {
                    _logger.LogWarning("Clé système bloquée: {Path}", item.Path);
                    continue;
                }

                var result = await _registryGuardian.SafeDeleteKeyAsync(item.Path, backupFirst: true, ct);
                if (result.Success) successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur suppression {Path}", item.Path);
            }

            current++;
            progress?.Report((double)current / itemsList.Count);
        }

        return new OperationResult(
            Success: successCount > 0,
            Message: $"Supprimé {successCount}/{itemsList.Count} clés de registre",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }

    private string? ExtractPath(string uninstallString)
    {
        if (uninstallString.StartsWith("\""))
        {
            var endQuote = uninstallString.IndexOf("\"", 1);
            if (endQuote > 0) return uninstallString[1..endQuote];
        }

        var spaceIndex = uninstallString.IndexOf(' ');
        if (spaceIndex > 0) return uninstallString[..spaceIndex];

        return uninstallString;
    }
}
