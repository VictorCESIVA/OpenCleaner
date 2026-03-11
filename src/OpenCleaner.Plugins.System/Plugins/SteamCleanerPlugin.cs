using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;

namespace OpenCleaner.Plugins.System.Plugins;

public class SteamCleanerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<SteamCleanerPlugin> _logger;

    public string Id => "games.steam";
    public string Name => "Nettoyeur Steam";
    public string Description => "Nettoie les caches de téléchargement, workshop et shaders de Steam";
    public PluginCategory Category => PluginCategory.Applications;
    public RiskLevel MaxRiskLevel => RiskLevel.Recommended;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public SteamCleanerPlugin(IFileGuardian fileGuardian, ILogger<SteamCleanerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        // Cherche Steam dans plusieurs emplacements possibles
        var steamPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            @"C:\Steam",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Steam")
        };

        // Ajoute les chemins depuis les disques D: à Z:
        for (char drive = 'D'; drive <= 'Z'; drive++)
        {
            steamPaths.Add($@"{drive}:\Steam");
            steamPaths.Add($@"{drive}:\Program Files (x86)\Steam");
            steamPaths.Add($@"{drive}:\Program Files\Steam");
        }

        _logger.LogInformation("Recherche de Steam dans {Count} emplacements...", steamPaths.Count);

        // Détection de l'installation Steam
        var foundPaths = steamPaths.Where(Directory.Exists).ToList();
        _logger.LogInformation("Chemins Steam trouvés : {Count}", foundPaths.Count);
        foreach (var path in foundPaths)
        {
            _logger.LogInformation("  - {Path}", path);
        }

        if (!foundPaths.Any())
        {
            _logger.LogWarning("Steam n'a pas été trouvé sur ce système");
            return items;
        }

        int currentPath = 0;
        foreach (var steamPath in foundPaths)
        {
            _logger.LogInformation("Analyse de {Path}", steamPath);
            var targets = new[]
            {
                (Path: Path.Combine(steamPath, "appcache", "httpcache"), Safe: true, MinAge: 7),
                (Path: Path.Combine(steamPath, "logs"), Safe: true, MinAge: 30),
                (Path: Path.Combine(steamPath, "appcache", "librarycache"), Safe: true, MinAge: 30),
                (Path: Path.Combine(steamPath, "steamapps", "temp"), Safe: true, MinAge: 1),
                (Path: Path.Combine(steamPath, "shadercache"), Safe: true, MinAge: 7)
            };

            int currentTarget = 0;
            foreach (var target in targets)
            {
                if (!Directory.Exists(target.Path))
                {
                    _logger.LogDebug("Dossier inexistant : {Path}", target.Path);
                    continue;
                }

                _logger.LogInformation("Scan de {Path} (min {MinAge} jours)", target.Path, target.MinAge);

                try
                {
                    var files = Directory.EnumerateFiles(target.Path, "*.*", SearchOption.AllDirectories);
                    int fileCount = 0;
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var info = new FileInfo(file);
                            if (!info.Exists) continue;

                            var age = DateTime.Now - info.LastAccessTime;
                            if (age.Days < target.MinAge) continue;
                            if (_fileGuardian.IsSystemCriticalPath(file)) continue;

                            items.Add(new CleanableItem(
                                Id: Guid.NewGuid().ToString(),
                                Path: file,
                                Size: info.Length,
                                Description: $"Steam {Path.GetFileName(target.Path)} - {Path.GetFileName(file)}",
                                RiskLevel: target.Safe ? RiskLevel.Safe : RiskLevel.Recommended,
                                Type: ItemType.File,
                                LastAccessTime: info.LastAccessTime,
                                ParentPluginId: Id
                            ));
                            fileCount++;
                        }
                        catch { }
                    }

                    _logger.LogInformation("  -> {Count} fichiers trouvés dans {Path}", fileCount, target.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur accès {Path}", target.Path);
                }

                currentTarget++;
            }

            currentPath++;
            progress?.Report((double)currentPath / foundPaths.Count);
        }

        _logger.LogInformation("Analyse Steam terminée : {Count} fichiers trouvés", items.Count);

        return items;
    }

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var itemsList = items.ToList();
        int success = 0;
        long totalSize = 0;

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_fileGuardian.IsSystemCriticalPath(item.Path)) continue;

                var result = await _fileGuardian.SafeDeleteAsync(item.Path, true, ct);
                if (result.Success)
                {
                    success++;
                    totalSize += item.Size;
                }
            }
            catch { }

            progress?.Report((double)success / itemsList.Count);
        }

        return new OperationResult(
            Success: success > 0,
            Message: $"Steam nettoyé : {success} fichiers ({totalSize / 1024 / 1024} Mo)",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
