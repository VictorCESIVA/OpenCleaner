namespace OpenCleaner.Contracts;

public interface ICleanerPlugin
{
    // Identité
    string Id { get; }                    // ex: "system.temp"
    string Name { get; }                  // ex: "Nettoyeur de fichiers temporaires"
    string Description { get; }           // Description courte
    PluginCategory Category { get; }
    RiskLevel MaxRiskLevel { get; }       // Niveau max que ce plugin touche

    // Analyse (lecture seule, jamais de suppression ici)
    Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default
    );

    // Nettoyage (suppression avec transactions)
    Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,     // Injecté par le Core
        IProgress<double>? progress = null,
        CancellationToken ct = default
    );

    // Estimation de la taille (optionnel, pour UI rapide)
    Task<long> EstimateSizeAsync(CancellationToken ct = default);

    // Vérifications
    bool IsAvailable { get; }             // Plugin applicable sur ce système ?
    bool RequiresAdmin { get; }           // Nécessite élévation ?
}
