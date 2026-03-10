using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;

namespace OpenCleaner.Plugins.System.Plugins;

public class OfficeCleanerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<OfficeCleanerPlugin> _logger;

    public string Id => "applications.office";
    public string Name => "Nettoyeur Microsoft Office";
    public string Description => "Nettoie le cache Word/Excel et les fichiers AutoRecover de plus de 7 jours";
    public PluginCategory Category => PluginCategory.Applications;
    public RiskLevel MaxRiskLevel => RiskLevel.Safe;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public OfficeCleanerPlugin(IFileGuardian fileGuardian, ILogger<OfficeCleanerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var officeBase = Path.Combine(localAppData, @"Microsoft\Office\16.0");

        // (chemin, filtre, âge min en jours, niveau de risque, description)
        var targets = new[]
        {
            (
                Path: Path.Combine(officeBase, "OfficeFileCache"),
                Pattern: "*.*",
                MinAgeDays: 3,
                Risk: RiskLevel.Safe,
                Desc: "Cache Office"
            ),
            (
                Path: Path.Combine(officeBase, @"Word\DocumentCache"),
                Pattern: "*.*",
                MinAgeDays: 3,
                Risk: RiskLevel.Safe,
                Desc: "Cache documents Word"
            ),
            (
                Path: Path.Combine(officeBase, @"Excel\DocumentCache"),
                Pattern: "*.*",
                MinAgeDays: 3,
                Risk: RiskLevel.Safe,
                Desc: "Cache documents Excel"
            ),
            (
                Path: Path.Combine(officeBase, @"Word\Startup"),
                Pattern: "AutoRecovery*.asd",
                MinAgeDays: 7,
                Risk: RiskLevel.Safe,
                Desc: "AutoRecovery Word"
            ),
            (
                Path: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Word"),
                Pattern: "AutoRecovery*.asd",
                MinAgeDays: 7,
                Risk: RiskLevel.Safe,
                Desc: "AutoRecovery Word (Roaming)"
            ),
            (
                Path: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Excel"),
                Pattern: "*.xlsb",
                MinAgeDays: 7,
                Risk: RiskLevel.Safe,
                Desc: "AutoRecovery Excel (Roaming)"
            ),
        };

        int totalTargets = targets.Length;
        int current = 0;

        foreach (var target in targets)
        {
            if (!Directory.Exists(target.Path))
            {
                current++;
                progress?.Report((double)current / totalTargets);
                continue;
            }

            try
            {
                var files = Directory.EnumerateFiles(target.Path, target.Pattern, SearchOption.AllDirectories);
                int fileCount = 0;

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;

                        if (_fileGuardian.IsSystemCriticalPath(file)) continue;
                        if (_fileGuardian.IsFileLocked(file)) continue;

                        var age = DateTime.Now - info.LastWriteTime;
                        if (age.Days < target.MinAgeDays) continue;

                        items.Add(new CleanableItem(
                            Id: Guid.NewGuid().ToString(),
                            Path: file,
                            Size: info.Length,
                            Description: $"{target.Desc} - {Path.GetFileName(file)} ({age.Days}j)",
                            RiskLevel: target.Risk,
                            Type: ItemType.File,
                            LastAccessTime: info.LastAccessTime,
                            ParentPluginId: Id
                        ));

                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Erreur analyse fichier Office {File}", file);
                    }
                }

                _logger.LogInformation("Office - Analysé {Path}: {Count} fichiers trouvés", target.Path, fileCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible d'analyser {Path}", target.Path);
            }

            current++;
            progress?.Report((double)current / totalTargets);
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
        int total = itemsList.Count;
        int current = 0;
        long totalSize = 0;
        int successCount = 0;

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_fileGuardian.IsSystemCriticalPath(item.Path))
                {
                    _logger.LogWarning("Tentative suppression chemin critique bloquée: {Path}", item.Path);
                    current++;
                    continue;
                }

                var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
                if (result.Success)
                {
                    successCount++;
                    totalSize += item.Size;
                }
                else
                {
                    _logger.LogWarning("Échec suppression {Path}: {Message}", item.Path, result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception suppression {Path}", item.Path);
            }

            current++;
            progress?.Report((double)current / total);
        }

        var mb = totalSize / 1024.0 / 1024.0;
        return new OperationResult(
            Success: successCount > 0,
            Message: $"Office nettoyé : {successCount}/{total} fichiers supprimés ({mb:F1} Mo libérés)",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
