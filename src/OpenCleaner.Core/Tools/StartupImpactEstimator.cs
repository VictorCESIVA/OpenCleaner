namespace OpenCleaner.Core.Tools;

/// <summary>Estimation du coût d'un programme au démarrage, fondée sur des indices vérifiables.</summary>
public enum StartupImpact
{
    Unknown,
    Low,
    Medium,
    High
}

/// <summary>
/// Heuristique transparente : taille de l'exécutable et emplacement modifiable par l'utilisateur.
/// Elle ne mesure pas le temps de démarrage réel et ne remplace pas une analyse des traces système.
/// </summary>
public static class StartupImpactEstimator
{
    private const long MegaByte = 1024L * 1024;
    private const long MediumThreshold = 4 * MegaByte;
    private const long HighThreshold = 20 * MegaByte;
    private const long UserWritableHighThreshold = 5 * MegaByte;

    public static StartupImpact Estimate(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return StartupImpact.Unknown;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
            if (!Path.IsPathFullyQualified(expanded)) return StartupImpact.Unknown;
            var info = new FileInfo(expanded);
            if (!info.Exists) return StartupImpact.Unknown;
            return Classify(info.Length, info.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return StartupImpact.Unknown;
        }
    }

    internal static StartupImpact Classify(long sizeBytes, string fullPath)
    {
        if (sizeBytes < 0 || string.IsNullOrWhiteSpace(fullPath)) return StartupImpact.Unknown;

        bool inTemp = fullPath.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) ||
                      fullPath.EndsWith(@"\Temp", StringComparison.OrdinalIgnoreCase) ||
                      fullPath.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase);
        bool userWritable = fullPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) ||
                            fullPath.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase);

        if (inTemp) return StartupImpact.High;
        if (userWritable) return sizeBytes >= UserWritableHighThreshold ? StartupImpact.High : StartupImpact.Medium;
        if (sizeBytes >= HighThreshold) return StartupImpact.High;
        if (sizeBytes >= MediumThreshold) return StartupImpact.Medium;
        return StartupImpact.Low;
    }

    public static string Label(StartupImpact impact) => impact switch
    {
        StartupImpact.High => "élevé",
        StartupImpact.Medium => "moyen",
        StartupImpact.Low => "faible",
        _ => "inconnu"
    };
}
