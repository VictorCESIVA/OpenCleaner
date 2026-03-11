using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>
/// Cartographie l'utilisation du disque en affichant les ENFANTS DIRECTS du dossier courant,
/// chacun avec sa taille récursive totale. Pas de double-comptage.
/// Le drill-down est géré par l'UI via des appels successifs en changeant ScanRoot.
/// </summary>
public class DiskAnalyzerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<DiskAnalyzerPlugin> _logger;

    public string Id          => "disk.analyzer";
    public string Name        => "Analyseur d'espace disque";
    public string Description => "Cartographie l'utilisation du disque et trouve les gros fichiers cachés";
    public PluginCategory Category  => PluginCategory.System;
    public RiskLevel MaxRiskLevel   => RiskLevel.Safe;
    public bool IsAvailable         => true;
    public bool RequiresAdmin       => false;

    /// <summary>Dossier analysé — changé par l'UI pour le drill-down.</summary>
    public string ScanRoot { get; set; } = @"C:\";

    private static readonly HashSet<string> IgnoredDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "System32", "SysWOW64", "WinSxS", "Recovery", "$Recycle.Bin",
        "Config.Msi", "MSOCache", "PerfLogs"
    };

    public DiskAnalyzerPlugin(IFileGuardian fileGuardian, ILogger<DiskAnalyzerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ANALYZE
    //
    //  Principe : pour chaque enfant direct de ScanRoot —
    //   - Fichier      → sa taille réelle
    //   - Sous-dossier → somme récursive de tous les fichiers qu'il contient
    //
    //  Aucune récursion multi-niveaux dans cette méthode :
    //  le double-clic re-invoque le plugin avec ScanRoot = dossier sélectionné.
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
    {
        var results = new List<CleanableItem>();

        await Task.Run(() =>
        {
            if (!Directory.Exists(ScanRoot)) return;

            // ── 1. Fichiers directs du dossier racine ──────────────────────
            try
            {
                var files = Directory.EnumerateFiles(ScanRoot);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists || info.Length == 0) continue;
                        results.Add(MakeItem(file, info.Length, ItemType.File, info.LastWriteTime));
                    }
                    catch { /* inaccessible */ }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Enum files {Root}", ScanRoot); }

            // ── 2. Sous-dossiers directs → taille récursive totale ─────────
            IEnumerable<string> subDirs;
            try   { subDirs = Directory.EnumerateDirectories(ScanRoot); }
            catch { subDirs = []; }

            var subDirList = subDirs.ToList();
            int done = 0;

            foreach (var sub in subDirList)
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(sub);
                if (!string.IsNullOrEmpty(name) && IgnoredDirNames.Contains(name)) { done++; continue; }
                if (_fileGuardian.IsSystemCriticalPath(sub)) { done++; continue; }

                try
                {
                    long size = GetRecursiveSize(sub, ct);
                    if (size > 0)
                        results.Add(MakeItem(sub, size, ItemType.Directory, Directory.GetLastWriteTime(sub)));
                }
                catch { /* inaccessible */ }

                done++;
                progress?.Report((double)done / subDirList.Count);
            }

        }, ct);

        // Tri décroissant
        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        progress?.Report(1.0);
        _logger.LogInformation("Disk scan ({Root}) : {Count} enfants directs", ScanRoot, results.Count);
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Calcule la taille TOTALE d'un dossier (tous niveaux)
    //  sans ajouter chaque sous-niveau dans la liste finale.
    // ─────────────────────────────────────────────────────────────────────

    private static long GetRecursiveSize(string dir, CancellationToken ct)
    {
        long size = 0;
        try
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible    = true,
                AttributesToSkip      = FileAttributes.ReparsePoint // ignore les jonctions NTFS
            };

            foreach (var file in Directory.EnumerateFiles(dir, "*", opts))
            {
                if (ct.IsCancellationRequested) break;
                try { size += new FileInfo(file).Length; }
                catch { /* fichier inaccessible */ }
            }
        }
        catch { /* dossier inaccessible */ }
        return size;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────

    private CleanableItem MakeItem(string path, long size, ItemType type, DateTime modified) =>
        new(Guid.NewGuid().ToString(), path, size,
            $"{SizeFormatter.Format(size)} — {path}",
            RiskLevel.Safe, type, modified, Id);

    /// <summary>Retourne les entrées dépassant la taille minimale.</summary>
    public static List<CleanableItem> FilterBySize(IReadOnlyList<CleanableItem> items, long minBytes)
        => items.Where(i => i.Size >= minBytes).ToList();

    // ─────────────────────────────────────────────────────────────────────
    //  CLEAN — lecture seule
    // ─────────────────────────────────────────────────────────────────────

    public Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
        => Task.FromResult(new OperationResult(false,
            "L'analyseur disque est en lecture seule.",
            Guid.NewGuid()));

    public Task<long> EstimateSizeAsync(CancellationToken ct = default) => Task.FromResult(0L);
}
