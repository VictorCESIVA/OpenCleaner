using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Security.Cryptography;
using OpenCleaner.Core;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>
/// Plugin de détection de doublons par hash SHA-256.
/// Scanne Documents, Bureau, Téléchargements et Images.
/// Seuls les fichiers de plus de 1 Mo sont analysés pour éviter le bruit.
/// </summary>
public class DuplicateFinderPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<DuplicateFinderPlugin> _logger;

    public string Id          => "system.duplicates";
    public string Name        => "Détecteur de doublons";
    public string Description => "Trouve les fichiers en double dans vos dossiers personnels (>1 Mo)";
    public PluginCategory Category    => PluginCategory.System;
    public RiskLevel MaxRiskLevel     => RiskLevel.Safe;
    public bool IsAvailable           => true;
    public bool RequiresAdmin         => false;

    /// <summary>Taille minimum d'un fichier pour être analysé (1 Mo).</summary>
    private const long MinSizeBytes = 1 * 1024 * 1024;

    /// <summary>Dossiers utilisateur scannés.</summary>
    private static readonly Environment.SpecialFolder[] ScanFolders =
    [
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolder.UserProfile,   // contient Downloads
        Environment.SpecialFolder.MyPictures,
        Environment.SpecialFolder.MyVideos,
        Environment.SpecialFolder.MyMusic,
    ];

    public DuplicateFinderPlugin(IFileGuardian fileGuardian, ILogger<DuplicateFinderPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public event Action<string>? OnFileHashing;

    // ─────────────────────────────────────────────────────────────────────
    //  ANALYZE
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        // 1. Collecte tous les fichiers éligibles
        var allFiles = CollectFiles();
        int total = allFiles.Count;

        if (total == 0)
        {
            progress?.Report(1.0);
            return items;
        }

        // 2. Passe préliminaire : Grouper par taille (ceux ayant une taille unique n'ont aucun doublon possible)
        var sizeGroups = allFiles
            .GroupBy(f =>
            {
                try { return new FileInfo(f).Length; }
                catch { return -1L; }
            })
            .Where(g => g.Key > 0 && g.Count() > 1)
            .ToList();

        var suspectedFiles = sizeGroups.SelectMany(g => g).ToList();
        var totalToHash = suspectedFiles.Count;

        if (totalToHash == 0)
        {
            progress?.Report(1.0);
            return items;
        }

        // 3. Passe de validation : Hash complet uniquement sur les suspects
        var hashMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int done = 0;

        foreach (var file in suspectedFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                OnFileHashing?.Invoke(file);
                var hash = await HashHelper.ComputeHashAsync(file, ct);
                if (!hashMap.TryGetValue(hash, out var paths))
                {
                    paths = [];
                    hashMap[hash] = paths;
                }
                paths.Add(file);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible de hasher {File}", file);
            }

            done++;
            progress?.Report((double)done / totalToHash);
        }

        // 4. Seuls les groupes avec >= 2 fichiers sont des doublons
        foreach (var kvp in hashMap)
        {
            if (kvp.Value.Count < 2) continue;

            var group = kvp.Value;
            // Trier : le plus récent d'abord = "original", les autres = copies
            group.Sort((a, b) =>
            {
                var ta = new FileInfo(a).LastWriteTime;
                var tb = new FileInfo(b).LastWriteTime;
                return tb.CompareTo(ta); // décroissant
            });

            // Le premier est l'original, les suivants sont des copies
            var originalPath = group[0];
            var originalInfo = new FileInfo(originalPath);
            var originalName = Path.GetFileName(originalPath);

            for (int i = 1; i < group.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var copyPath = group[i];
                var copyInfo = new FileInfo(copyPath);

                items.Add(new CleanableItem(
                    Id: Guid.NewGuid().ToString(),
                    Path: copyPath,
                    Size: copyInfo.Length,
                    Description: $"Doublon de « {originalName} » ({copyPath})",
                    RiskLevel: RiskLevel.Safe,
                    Type: ItemType.File,
                    LastAccessTime: copyInfo.LastAccessTime,
                    ParentPluginId: Id
                ));
            }
        }

        _logger.LogInformation("Analyse doublons : {Count} copies trouvées", items.Count);
        return items;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CLEAN
    // ─────────────────────────────────────────────────────────────────────

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var list    = items.ToList();
        int total   = list.Count;
        int current = 0;
        int success = 0;
        long freed  = 0;

        foreach (var item in list)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_fileGuardian.IsSystemCriticalPath(item.Path)) continue;

                var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
                if (result.Success)
                {
                    success++;
                    freed += item.Size;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur suppression doublon {Path}", item.Path);
            }

            current++;
            progress?.Report((double)current / total);
        }

        var mb = freed / 1_048_576.0;
        return new OperationResult(
            Success: success > 0,
            Message: $"{success}/{total} doublons supprimés ({mb:0.#} Mo libérés)",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
        => Task.FromResult(0L);

    // ─────────────────────────────────────────────────────────────────────
    //  UTILITAIRES
    // ─────────────────────────────────────────────────────────────────────

    private List<string> CollectFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var dirs = ScanFolders
            .Select(f => Environment.GetFolderPath(f))
            .Where(p => !string.IsNullOrEmpty(p))
            .Concat([downloadsPath])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

        foreach (var dir in dirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (_fileGuardian.IsSystemCriticalPath(file)) continue;
                        var info = new FileInfo(file);
                        if (info.Length >= MinSizeBytes)
                            files.Add(file);
                    }
                    catch { /* ignore fichier individuel inaccessible */ }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible de scanner {Dir}", dir);
            }
        }

        return [.. files];
    }


}
