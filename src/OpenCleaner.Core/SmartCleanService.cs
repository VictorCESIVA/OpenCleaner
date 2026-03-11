using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Core;

/// <summary>
/// Lance des plugins en parallèle (fournis par l'UI), sélectionne les 3 meilleurs gains,
/// puis nettoie sans confirmation pour les items Safe/Recommended.
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeout.Token);
        var ct = linked.Token;

        // Lance tous les scans en parallèle
        var tasks = plugins.Select(async p =>
        {
            try
            {
                var items = await p.AnalyzeAsync(ct: ct);
                return new SmartScanResult(p, [.. items], items.Sum(i => i.Size));
            }
            catch
            {
                return new SmartScanResult(p, [], 0);
            }
        });

        var results = await Task.WhenAll(tasks);

        // Top 3 par taille décroissante
        return [.. results.OrderByDescending(r => r.TotalSize).Take(3)];
    }

    // ─────────────────────────────────────────────────────────────────
    //  NETTOYAGE AUTOMATIQUE (Safe + Recommended seulement)
    // ─────────────────────────────────────────────────────────────────

    public async Task<SmartCleanResult> ExecuteSmartCleanAsync(
        SmartScanResult[] scanResults,
        IProgress<(int current, int total, string plugin)>? progress = null,
        CancellationToken ct = default)
    {
        long totalFreed   = 0;
        int  totalCleaned = 0;
        var  errors       = new List<string>();

        int done  = 0;
        int total = scanResults.Length;

        foreach (var scan in scanResults)
        {
            ct.ThrowIfCancellationRequested();

            // Filtre : jamais ExpertOnly en mode Smart Clean
            var safeItems = scan.Items
                .Where(i => i.RiskLevel != RiskLevel.ExpertOnly)
                .ToList();

            if (safeItems.Count == 0) { done++; continue; }

            progress?.Report((done, total, scan.Plugin.Name));

            try
            {
                var result = await scan.Plugin.CleanAsync(safeItems, _backupManager, ct: ct);
                if (result.Success)
                {
                    totalFreed   += safeItems.Sum(i => i.Size);
                    totalCleaned += safeItems.Count;
                }
                else
                {
                    errors.Add(scan.Plugin.Name + ": " + result.Message);
                }
            }
            catch (Exception ex)
            {
                errors.Add(scan.Plugin.Name + ": " + ex.Message);
            }

            done++;
            progress?.Report((done, total, scan.Plugin.Name));
        }

        return new SmartCleanResult(totalCleaned, totalFreed, errors);
    }
}

// ─── Records résultat ────────────────────────────────────────────────────────

public sealed record SmartScanResult(
    ICleanerPlugin      Plugin,
    List<CleanableItem> Items,
    long                TotalSize);

public sealed record SmartCleanResult(
    int          ItemsCleaned,
    long         BytesFreed,
    List<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
