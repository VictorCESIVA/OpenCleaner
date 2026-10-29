namespace OpenCleaner.Core.Health;

/// <summary>Gravité d'un constat de santé, du plus anodin au plus urgent.</summary>
public enum HealthSeverity
{
    Ok = 0,
    Advice = 1,
    Warning = 2,
    Critical = 3
}

public sealed record HealthDisk(string Name, long TotalBytes, long FreeBytes);

/// <summary>Mesures brutes collectées une fois, puis évaluées sans effet de bord.</summary>
public sealed record HealthMetrics
{
    public IReadOnlyList<HealthDisk> Disks { get; init; } = [];
    public ulong TotalMemoryBytes { get; init; }
    public ulong AvailableMemoryBytes { get; init; }
    public TimeSpan Uptime { get; init; }
    public long RecycleBinBytes { get; init; }
    public int RecycleBinItems { get; init; }
    public int EnabledStartupItems { get; init; }
    public int DisabledStartupItems { get; init; }
    public long TemporaryBytes { get; init; }
    public int TemporaryItems { get; init; }
    public long BrowserCacheBytes { get; init; }
    public int BrowserCacheItems { get; init; }
    public bool RebootPending { get; init; }
}

/// <summary>Tailles des caches, mesurées par les plugins de nettoyage (lecture seule).</summary>
public sealed record CacheSnapshot(
    long TemporaryBytes = 0,
    int TemporaryItems = 0,
    long BrowserCacheBytes = 0,
    int BrowserCacheItems = 0)
{
    public static CacheSnapshot Empty { get; } = new();
}

/// <summary>
/// Un constat lisible par l'utilisateur. <see cref="Action"/> est une clé opaque que
/// l'interface traduit en navigation ; le moteur de diagnostic ne dépend d'aucune vue.
/// </summary>
public sealed record HealthFinding(
    string Id,
    HealthSeverity Severity,
    string Title,
    string Detail,
    string Recommendation,
    string? Action = null,
    long? ReclaimableBytes = null)
{
    /// <summary>Clé de traduction du titre ; l'interface retombe sur <see cref="Title"/> si absente.</summary>
    public string? TitleKey { get; init; }

    /// <summary>Clé de traduction du détail, avec <see cref="Args"/>.</summary>
    public string? DetailKey { get; init; }

    /// <summary>Clé de traduction de la recommandation.</summary>
    public string? RecommendationKey { get; init; }

    /// <summary>Paramètres des clés de traduction.</summary>
    public object?[]? Args { get; init; }
}

public sealed record HealthSection(
    string Id,
    string Title,
    int Score,
    int MaxScore,
    bool Measured,
    IReadOnlyList<HealthFinding> Findings);

public sealed record HealthReport(
    int Score,
    int MaxScore,
    string Grade,
    IReadOnlyList<HealthSection> Sections,
    IReadOnlyList<HealthFinding> Findings,
    long ReclaimableBytes)
{
    public double Ratio => MaxScore > 0 ? (double)Score / MaxScore : 0;
}
