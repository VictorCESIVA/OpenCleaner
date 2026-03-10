namespace OpenCleaner.Contracts;

public sealed record CleanableItem(
    string Id,                    // GUID unique
    string Path,                  // Chemin complet
    long Size,                    // Taille en bytes (0 si inconnu)
    string Description,           // Description lisible ("Cache Chrome - 3 jours")
    RiskLevel RiskLevel,
    ItemType Type,
    DateTime? LastAccessTime = null,
    string? ParentPluginId = null
);
