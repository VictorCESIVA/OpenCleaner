using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Core;

/// <summary>
/// Analyse les plugins disponibles en parallèle (fournis par l'UI), conserve leurs erreurs,
/// puis nettoie sans confirmation uniquement les fichiers explicitement Safe.
/// Les plugins concrets sont construits par la couche UI pour éviter la dépendance cyclique Core → Plugins.
/// </summary>
public sealed class SmartCleanService
{
    private readonly IBackupManager _backupManager;

    public SmartCleanService(IBackupManager backupManager)
    {
        _backupManager = backupManager;
    }

    // ─────────────────────────────────────────────────────────────────
    //  ANALYSE RAPIDE (max 5 s)
    // ─────────────────────────────────────────────────────────────────

    public async Task<SmartScanResult[]> QuickScanAsync(
        ICleanerPlugin[] plugins,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        externalCt.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeout.Token);
        var ct = linked.Token;

        // Lance tous les scans en parallèle
        var tasks = plugins.Where(p => p.IsAvailable)
            .DistinctBy(p => p.Id).Select(async p =>
        {
            try
            {
                var items = await p.AnalyzeAsync(ct: ct).WaitAsync(ct);
                var safeItems = items.Where(i => IsAutomaticItem(i, p.Id)).ToList();
                return new SmartScanResult(p, safeItems, SumSizes(safeItems));
            }
            catch (OperationCanceledException) when (externalCt.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return new SmartScanResult(p, [], 0, timeout.IsCancellationRequested
                    ? "Délai d'analyse de 5 secondes dépassé" : "Analyse annulée par le module");
            }
            catch (Exception ex)
            {
                return new SmartScanResult(p, [], 0, $"Analyse impossible : {ex.Message}");
            }
        });

        var results = await Task.WhenAll(tasks);
        externalCt.ThrowIfCancellationRequested();

        return [.. results.OrderByDescending(r => r.TotalSize)];
    }

    // ─────────────────────────────────────────────────────────────────
    //  NETTOYAGE AUTOMATIQUE (fichiers Safe seulement)
    // ─────────────────────────────────────────────────────────────────

    public async Task<SmartCleanResult> ExecuteSmartCleanAsync(
        SmartScanResult[] scanResults,
        IProgress<(int current, int total, string plugin)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scanResults);
        long totalFreed   = 0;
        int  totalCleaned = 0;
        var  errors       = new List<string>();

        int done  = 0;
        int total = scanResults.Length;

        foreach (var scan in scanResults)
        {
            ct.ThrowIfCancellationRequested();

            if (scan.Error != null)
            {
                errors.Add(scan.Plugin.Name + ": " + scan.Error);
                done++;
                progress?.Report((done, total, scan.Plugin.Name));
                continue;
            }

            // Les options manuelles Recommended ne participent jamais au mode rapide.
            var safeItems = scan.Items
                .Where(i => scan.Plugin.IsAvailable && scan.Plugin.Category != PluginCategory.Registry && IsAutomaticItem(i, scan.Plugin.Id))
                .DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (safeItems.Count == 0) { done++; progress?.Report((done, total, scan.Plugin.Name)); continue; }

            progress?.Report((done, total, scan.Plugin.Name));

            try
            {
                var result = await scan.Plugin.CleanAsync(safeItems, _backupManager, ct: ct);
                totalFreed = AddSaturated(totalFreed, Math.Max(0, result.BytesRecovered ?? 0));
                totalCleaned += Math.Clamp(result.ItemsProcessed ?? 0, 0, safeItems.Count);
                if (!result.Success)
                {
                    errors.Add(scan.Plugin.Name + ": " + result.Message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add(scan.Plugin.Name + ": " + ex.Message);
            }

            done++;
            progress?.Report((done, total, scan.Plugin.Name));
        }

        return new SmartCleanResult(totalCleaned, totalFreed, errors);
    }

    private static bool IsAutomaticItem(CleanableItem item, string pluginId) =>
        item.Size >= 0 && item.RiskLevel == RiskLevel.Safe && item.Type == ItemType.File &&
        (item.ParentPluginId == null || item.ParentPluginId == pluginId);

    private static long SumSizes(IEnumerable<CleanableItem> items) => items.Aggregate(0L, (sum, item) => AddSaturated(sum, item.Size));
    private static long AddSaturated(long value, long increment) => increment > long.MaxValue - value ? long.MaxValue : value + increment;
}

// ─── Records résultat ────────────────────────────────────────────────────────

public sealed record SmartScanResult(
    ICleanerPlugin      Plugin,
    List<CleanableItem> Items,
    long                TotalSize,
    string?             Error = null);

public sealed record SmartCleanResult(
    int          ItemsCleaned,
    long         BytesFreed,
    List<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
