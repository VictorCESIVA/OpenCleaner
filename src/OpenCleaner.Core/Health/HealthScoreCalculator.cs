using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Health;

/// <summary>
/// Évalue les mesures de santé en un score explicable (100 points répartis en cinq sections).
/// Fonction pure : aucune lecture disque, aucun registre, aucun accès réseau.
/// </summary>
public static class HealthScoreCalculator
{
    public const int MaxScore = 100;
    public const int StorageMax = 40;
    public const int MemoryMax = 20;
    public const int StartupMax = 15;
    public const int MaintenanceMax = 15;
    public const int CleanlinessMax = 10;

    private const long GigaByte = 1024L * 1024 * 1024;
    private const long MegaByte = 1024L * 1024;

    public static HealthReport Evaluate(HealthMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var sections = new List<HealthSection>
        {
            EvaluateStorage(metrics),
            EvaluateMemory(metrics),
            EvaluateStartup(metrics),
            EvaluateMaintenance(metrics),
            EvaluateCleanliness(metrics)
        };

        int score = sections.Sum(section => section.Score);
        long reclaimable = AddSaturated(AddSaturated(
            Math.Max(0, metrics.TemporaryBytes),
            Math.Max(0, metrics.BrowserCacheBytes)),
            Math.Max(0, metrics.RecycleBinBytes));
        var findings = sections
            .SelectMany(section => section.Findings)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Id, StringComparer.Ordinal)
            .ToList();

        return new HealthReport(score, MaxScore, GradeFor(score), sections, findings, reclaimable);
    }

    public static string GradeFor(int score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Bon",
        >= 55 => "À surveiller",
        _ => "Critique"
    };

    private static HealthSection EvaluateStorage(HealthMetrics metrics)
    {
        var findings = new List<HealthFinding>();
        var measured = metrics.Disks.Where(disk => disk.TotalBytes > 0).ToList();
        if (measured.Count == 0)
        {
            findings.Add(new HealthFinding(
                "storage.unknown", HealthSeverity.Advice, "Stockage non mesuré",
                "Windows n'a pas communiqué la capacité des disques.",
                "Vérifiez l'espace libre dans l'Explorateur de fichiers.", "disk")
            {
                TitleKey = "Health.Storage.Unknown.Title",
                DetailKey = "Health.Storage.Unknown.Detail",
                RecommendationKey = "Health.Storage.Unknown.Recommendation"
            });
            return Section("storage", "Stockage", StorageMax - 8, StorageMax, false, findings);
        }

        int deduction = 0;
        foreach (var disk in measured.OrderBy(disk => FreeRatio(disk)))
        {
            double free = FreeRatio(disk);
            var (severity, points) = free switch
            {
                < 0.03 => (HealthSeverity.Critical, 30),
                < 0.07 => (HealthSeverity.Critical, 24),
                < 0.10 => (HealthSeverity.Warning, 16),
                < 0.15 => (HealthSeverity.Advice, 8),
                < 0.20 => (HealthSeverity.Advice, 4),
                _ => (HealthSeverity.Ok, 0)
            };
            if (points <= 0) continue;
            deduction = Math.Max(deduction, points);
            long freeBytes = Math.Max(0, disk.FreeBytes);
            findings.Add(new HealthFinding(
                "storage.low." + disk.Name.TrimEnd('\\', '/').ToLowerInvariant(),
                severity,
                $"Espace faible sur {disk.Name}",
                $"{SizeFormatter.Format(freeBytes)} libres sur {SizeFormatter.Format(disk.TotalBytes)} " +
                $"({free:P1} disponible).",
                "Repérez les dossiers volumineux avant de supprimer quoi que ce soit.",
                "disk")
            {
                TitleKey = "Health.Storage.Low.Title",
                DetailKey = "Health.Storage.Low.Detail",
                RecommendationKey = "Health.Storage.Low.Recommendation",
                Args = [disk.Name, SizeFormatter.Format(freeBytes), SizeFormatter.Format(disk.TotalBytes),
                    free.ToString("P1")]
            });
        }

        if (findings.Count == 0)
            findings.Add(new HealthFinding(
                "storage.ok", HealthSeverity.Ok, "Espace disque suffisant",
                "Chaque disque mesuré dispose d'au moins 20 % d'espace libre.",
                "Aucune action nécessaire.", "disk")
            {
                TitleKey = "Health.Storage.Ok.Title",
                DetailKey = "Health.Storage.Ok.Detail",
                RecommendationKey = "Health.Storage.Ok.Recommendation"
            });

        return Section("storage", "Stockage", Math.Max(0, StorageMax - deduction), StorageMax,
            true, findings);
    }

    private static HealthSection EvaluateMemory(HealthMetrics metrics)
    {
        var findings = new List<HealthFinding>();
        if (metrics.TotalMemoryBytes == 0)
        {
            findings.Add(new HealthFinding(
                "memory.unknown", HealthSeverity.Advice, "Mémoire non mesurée",
                "Windows n'a pas communiqué la mémoire installée.",
                "Vérifiez la mémoire dans le Gestionnaire des tâches.")
            {
                TitleKey = "Health.Memory.Unknown.Title",
                DetailKey = "Health.Memory.Unknown.Detail",
                RecommendationKey = "Health.Memory.Unknown.Recommendation"
            });
            return Section("memory", "Mémoire", MemoryMax - 6, MemoryMax, false, findings);
        }

        ulong total = metrics.TotalMemoryBytes;
        ulong available = Math.Min(metrics.AvailableMemoryBytes, total);
        double used = total == 0 ? 0 : (double)(total - available) / total;
        var (severity, deduction) = used switch
        {
            > 0.92 => (HealthSeverity.Critical, 16),
            > 0.85 => (HealthSeverity.Warning, 10),
            > 0.75 => (HealthSeverity.Advice, 4),
            _ => (HealthSeverity.Ok, 0)
        };

        if (deduction > 0)
            findings.Add(new HealthFinding(
                "memory.pressure", severity, "Mémoire très sollicitée",
                $"{used:P0} de la mémoire est utilisée ({SizeFormatter.Format((long)(total - available))} " +
                $"sur {SizeFormatter.Format((long)total)}).",
                "Fermez les applications inutilisées et vérifiez les programmes lancés au démarrage.",
                "startup")
            {
                TitleKey = "Health.Memory.Pressure.Title",
                DetailKey = "Health.Memory.Pressure.Detail",
                RecommendationKey = "Health.Memory.Pressure.Recommendation",
                Args = [used.ToString("P0"), SizeFormatter.Format((long)(total - available)),
                    SizeFormatter.Format((long)total)]
            });
        else
            findings.Add(new HealthFinding(
                "memory.ok", HealthSeverity.Ok, "Mémoire disponible",
                $"{used:P0} de la mémoire est utilisée.",
                "Aucune action nécessaire.")
            {
                TitleKey = "Health.Memory.Ok.Title",
                DetailKey = "Health.Memory.Ok.Detail",
                RecommendationKey = "Health.Memory.Ok.Recommendation",
                Args = [used.ToString("P0")]
            });

        return Section("memory", "Mémoire", Math.Max(0, MemoryMax - deduction), MemoryMax,
            true, findings);
    }

    private static HealthSection EvaluateStartup(HealthMetrics metrics)
    {
        var findings = new List<HealthFinding>();
        int enabled = Math.Max(0, metrics.EnabledStartupItems);
        var (severity, deduction) = enabled switch
        {
            >= 35 => (HealthSeverity.Warning, 12),
            >= 25 => (HealthSeverity.Warning, 8),
            >= 15 => (HealthSeverity.Advice, 4),
            _ => (HealthSeverity.Ok, 0)
        };

        if (deduction > 0)
            findings.Add(new HealthFinding(
                "startup.many", severity, "Nombreux programmes au démarrage",
                $"{enabled} éléments s'exécutent à l'ouverture de session. Chacun allonge le démarrage.",
                "Désactivez ceux que vous n'utilisez pas au quotidien ; OpenCleaner peut les réactiver.",
                "startup")
            {
                TitleKey = "Health.Startup.Many.Title",
                DetailKey = "Health.Startup.Many.Detail",
                RecommendationKey = "Health.Startup.Many.Recommendation",
                Args = [enabled]
            });
        else
            findings.Add(new HealthFinding(
                "startup.ok", HealthSeverity.Ok, "Démarrage maîtrisé",
                $"{enabled} élément(s) au démarrage.",
                "Aucune action nécessaire.", "startup")
            {
                TitleKey = "Health.Startup.Ok.Title",
                DetailKey = "Health.Startup.Ok.Detail",
                RecommendationKey = "Health.Startup.Ok.Recommendation",
                Args = [enabled]
            });

        return Section("startup", "Démarrage", Math.Max(0, StartupMax - deduction), StartupMax,
            true, findings);
    }

    private static HealthSection EvaluateMaintenance(HealthMetrics metrics)
    {
        var findings = new List<HealthFinding>();
        int deduction = 0;
        if (metrics.RebootPending)
        {
            deduction += 8;
            findings.Add(new HealthFinding(
                "maintenance.reboot", HealthSeverity.Warning, "Redémarrage en attente",
                "Windows a terminé une installation qui nécessite un redémarrage.",
                "Redémarrez quand votre travail est enregistré pour appliquer les mises à jour.",
                "updates")
            {
                TitleKey = "Health.Maintenance.Reboot.Title",
                DetailKey = "Health.Maintenance.Reboot.Detail",
                RecommendationKey = "Health.Maintenance.Reboot.Recommendation"
            });
        }

        if (metrics.Uptime > TimeSpan.FromDays(30))
        {
            deduction += 4;
            findings.Add(new HealthFinding(
                "maintenance.uptime", HealthSeverity.Advice, "Windows tourne depuis longtemps",
                $"La session dure depuis {FormatUptime(metrics.Uptime)}.",
                "Un redémarrage libère les ressources accumulées par les applications.")
            {
                TitleKey = "Health.Maintenance.Uptime.Title",
                DetailKey = metrics.Uptime.TotalDays < 2
                    ? "Health.Maintenance.Uptime.DaysOne.Detail"
                    : "Health.Maintenance.Uptime.Days.Detail",
                RecommendationKey = "Health.Maintenance.Uptime.Recommendation",
                Args = metrics.Uptime.TotalDays < 2
                    ? [metrics.Uptime.Hours]
                    : [(int)metrics.Uptime.TotalDays, metrics.Uptime.Hours]
            });
        }
        else if (metrics.Uptime > TimeSpan.FromDays(14))
        {
            deduction += 2;
            findings.Add(new HealthFinding(
                "maintenance.uptime", HealthSeverity.Advice, "Session longue",
                $"La session dure depuis {FormatUptime(metrics.Uptime)}.",
                "Un redémarrage occasionnel évite l'accumulation de ressources.")
            {
                TitleKey = "Health.Maintenance.Session.Title",
                DetailKey = "Health.Maintenance.Uptime.Days.Detail",
                RecommendationKey = "Health.Maintenance.Session.Recommendation",
                Args = [(int)metrics.Uptime.TotalDays, metrics.Uptime.Hours]
            });
        }

        if (findings.Count == 0)
            findings.Add(new HealthFinding(
                "maintenance.ok", HealthSeverity.Ok, "Aucune maintenance en attente",
                "Pas de redémarrage requis et session récente.",
                "Aucune action nécessaire.", "updates")
            {
                TitleKey = "Health.Maintenance.Ok.Title",
                DetailKey = "Health.Maintenance.Ok.Detail",
                RecommendationKey = "Health.Maintenance.Ok.Recommendation"
            });

        return Section("maintenance", "Entretien", Math.Max(0, MaintenanceMax - deduction),
            MaintenanceMax, true, findings);
    }

    private static HealthSection EvaluateCleanliness(HealthMetrics metrics)
    {
        var findings = new List<HealthFinding>();
        int deduction = 0;

        long temporary = Math.Max(0, metrics.TemporaryBytes);
        if (temporary > 0)
        {
            int points = temporary switch
            {
                > 10 * GigaByte => 5,
                > 2 * GigaByte => 3,
                > 500 * MegaByte => 1,
                _ => 0
            };
            deduction += points;
            if (points > 0)
                findings.Add(new HealthFinding(
                    "cleanliness.temporary", HealthSeverity.Advice, "Fichiers temporaires accumulés",
                    $"{SizeFormatter.Format(temporary)} dans {Math.Max(0, metrics.TemporaryItems):N0} élément(s).",
                    "Analysez-les puis nettoyez uniquement ce que vous acceptez de perdre.",
                    "cleanTemp", temporary)
                {
                    TitleKey = "Health.Cleanliness.Temporary.Title",
                    DetailKey = "Health.Cleanliness.Temporary.Detail",
                    RecommendationKey = "Health.Cleanliness.Temporary.Recommendation",
                    Args = [SizeFormatter.Format(temporary), Math.Max(0, metrics.TemporaryItems)]
                });
        }

        long browser = Math.Max(0, metrics.BrowserCacheBytes);
        if (browser > GigaByte)
        {
            int points = browser > 5 * GigaByte ? 3 : 1;
            deduction += points;
            findings.Add(new HealthFinding(
                "cleanliness.browser", HealthSeverity.Advice, "Caches de navigateurs volumineux",
                $"{SizeFormatter.Format(browser)} de caches dans {Math.Max(0, metrics.BrowserCacheItems):N0} fichier(s).",
                "Fermez les navigateurs, puis nettoyez leurs caches. Vos favoris et mots de passe sont conservés.",
                "cleanBrowser", browser)
            {
                TitleKey = "Health.Cleanliness.Browser.Title",
                DetailKey = "Health.Cleanliness.Browser.Detail",
                RecommendationKey = "Health.Cleanliness.Browser.Recommendation",
                Args = [SizeFormatter.Format(browser), Math.Max(0, metrics.BrowserCacheItems)]
            });
        }

        long recycle = Math.Max(0, metrics.RecycleBinBytes);
        if (recycle > 0)
        {
            int points = recycle switch
            {
                > 10 * GigaByte => 4,
                > 2 * GigaByte => 2,
                > 500 * MegaByte => 1,
                _ => 0
            };
            deduction += points;
            if (points > 0)
                findings.Add(new HealthFinding(
                    "cleanliness.recycle", HealthSeverity.Advice, "Corbeille volumineuse",
                    $"{SizeFormatter.Format(recycle)} dans {Math.Max(0, metrics.RecycleBinItems):N0} élément(s).",
                    "Vérifiez son contenu dans l'Explorateur : le vidage est définitif.",
                    "recycle", recycle)
                {
                    TitleKey = "Health.Cleanliness.Recycle.Title",
                    DetailKey = "Health.Cleanliness.Recycle.Detail",
                    RecommendationKey = "Health.Cleanliness.Recycle.Recommendation",
                    Args = [SizeFormatter.Format(recycle), Math.Max(0, metrics.RecycleBinItems)]
                });
        }

        if (findings.Count == 0)
            findings.Add(new HealthFinding(
                "cleanliness.ok", HealthSeverity.Ok, "Peu de fichiers temporaires",
                "Les caches mesurés restent modestes.",
                "Aucune action nécessaire.")
            {
                TitleKey = "Health.Cleanliness.Ok.Title",
                DetailKey = "Health.Cleanliness.Ok.Detail",
                RecommendationKey = "Health.Cleanliness.Ok.Recommendation"
            });

        return Section("cleanliness", "Propreté", Math.Max(0, CleanlinessMax - deduction),
            CleanlinessMax, true, findings);
    }

    private static HealthSection Section(string id, string title, int score, int max, bool measured,
        List<HealthFinding> findings) =>
        new(id, title, Math.Clamp(score, 0, max), max, measured,
            findings.OrderByDescending(finding => finding.Severity).ThenBy(f => f.Id, StringComparer.Ordinal)
                .ToList());

    private static double FreeRatio(HealthDisk disk) =>
        disk.TotalBytes <= 0 ? 1 : Math.Clamp((double)disk.FreeBytes / disk.TotalBytes, 0, 1);

    private static long AddSaturated(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    internal static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} {(uptime.TotalDays < 2 ? "jour" : "jours")} et {uptime.Hours} h"
            : $"{uptime.Hours} h {uptime.Minutes} min";
}
