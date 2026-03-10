using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;

namespace OpenCleaner.Plugins.System.Plugins;

public class ThumbnailCachePlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<ThumbnailCachePlugin> _logger;

    public string Id => "system.thumbnailcache";
    public string Name => "Cache des miniatures Windows";
    public string Description => "Supprime les fichiers thumbcache_*.db de l'Explorateur Windows";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.Safe;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public ThumbnailCachePlugin(IFileGuardian fileGuardian, ILogger<ThumbnailCachePlugin> logger)
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
        var explorerPath = Path.Combine(localAppData, @"Microsoft\Windows\Explorer");

        if (!Directory.Exists(explorerPath))
        {
            _logger.LogInformation("Dossier Explorer introuvable : {Path}", explorerPath);
            progress?.Report(1.0);
            return items;
        }

        try
        {
            // thumbcache_*.db (miniatures) + iconcache*.db (icônes)
            var patterns = new[]
            {
                (Pattern: "thumbcache_*.db",  Desc: "Cache miniatures Explorer"),
                (Pattern: "iconcache*.db",    Desc: "Cache icônes Explorer"),
            };

            int patternIndex = 0;

            foreach (var entry in patterns)
            {
                ct.ThrowIfCancellationRequested();

                var files = Directory.EnumerateFiles(explorerPath, entry.Pattern, SearchOption.TopDirectoryOnly);
                int fileCount = 0;

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;

                        if (_fileGuardian.IsSystemCriticalPath(file)) continue;

                        // Les fichiers sont régénérés automatiquement par Windows
                        // On les accepte même sans critère d'ancienneté minimale
                        if (_fileGuardian.IsFileLocked(file))
                        {
                            _logger.LogDebug("Fichier verrouillé (Explorer actif ?) : {File}", file);
                            continue;
                        }

                        items.Add(new CleanableItem(
                            Id: Guid.NewGuid().ToString(),
                            Path: file,
                            Size: info.Length,
                            Description: $"{entry.Desc} - {Path.GetFileName(file)}",
                            RiskLevel: RiskLevel.Safe,
                            Type: ItemType.File,
                            LastAccessTime: info.LastAccessTime,
                            ParentPluginId: Id
                        ));

                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Erreur analyse fichier cache {File}", file);
                    }
                }

                _logger.LogInformation("ThumbnailCache - motif '{Pattern}': {Count} fichiers", entry.Pattern, fileCount);

                patternIndex++;
                progress?.Report((double)patternIndex / patterns.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'analyser {Path}", explorerPath);
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
                    _logger.LogWarning("Chemin critique ignoré : {Path}", item.Path);
                    current++;
                    continue;
                }

                // Backup optionnel : les thumbcache se regénèrent automatiquement,
                // mais on conserve createBackup: true par cohérence avec le reste.
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
            Message: $"Cache miniatures nettoyé : {successCount}/{total} fichiers ({mb:F1} Mo libérés)",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
