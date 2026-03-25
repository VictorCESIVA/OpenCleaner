using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public class SystemTempPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<SystemTempPlugin> _logger;

    public string Id => "system.temp";
    public string Name => "Fichiers temporaires système";
    public string Description => "Nettoie %TEMP%, Windows\\Temp et Prefetch";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.Recommended;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public SystemTempPlugin(IFileGuardian fileGuardian, ILogger<SystemTempPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();
        var locations = new[]
        {
            (Path: Path.GetTempPath(), MinAge: TimeSpan.FromHours(24), Risk: RiskLevel.Safe, Recursive: true),
            (Path: @"C:\Windows\Temp", MinAge: TimeSpan.FromDays(7), Risk: RiskLevel.Recommended, Recursive: true),
            (Path: @"C:\Windows\Prefetch", MinAge: TimeSpan.FromDays(30), Risk: RiskLevel.ExpertOnly, Recursive: false)
        };

        int totalLocations = locations.Length;
        int currentLocation = 0;

        foreach (var loc in locations)
        {
            if (!Directory.Exists(loc.Path)) continue;

            try
            {
                var files = EnumerateFilesSafe(loc.Path, loc.Recursive);
                int fileCount = 0;

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;

                        if (_fileGuardian.IsSystemCriticalPath(file)) continue;
                        if (_fileGuardian.IsFileLocked(file)) continue;

                        var age = DateTime.Now - info.LastAccessTime;
                        if (age < loc.MinAge) continue;

                        items.Add(new CleanableItem(
                            Id: Guid.NewGuid().ToString(),
                            Path: file,
                            Size: info.Length,
                            Description: $"{Path.GetFileName(file)} ({age.Days} jours)",
                            RiskLevel: loc.Risk,
                            Type: ItemType.File,
                            LastAccessTime: info.LastAccessTime,
                            ParentPluginId: Id
                        ));

                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Erreur analyse fichier {File}", file);
                    }
                }

                _logger.LogInformation("Analysé {Path}: {Count} fichiers trouvés", loc.Path, fileCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible d'analyser {Path}", loc.Path);
            }

            currentLocation++;
            progress?.Report((double)currentLocation / totalLocations);
        }

        return items;
    }

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var itemsList = items.ToList();
        int total = itemsList.Count;
        int current = 0;
        long totalSize = 0;
        int successCount = 0;

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_fileGuardian.IsSystemCriticalPath(item.Path))
                {
                    _logger.LogWarning("Tentative suppression chemin critique bloquée: {Path}", item.Path);
                    continue;
                }

                var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);

                if (!result.Success &&
                    result.Message.Contains("Backup failed", StringComparison.OrdinalIgnoreCase))
                {
                    result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: false, ct);
                }

                if (result.Success)
                {
                    successCount++;
                    totalSize += item.Size;
                }
                else
                {
                    _logger.LogWarning("Échec suppression {Path}: {Message}", item.Path, result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception suppression {Path}", item.Path);
            }

            current++;
            progress?.Report((double)current / total);
        }

        int failedCount = total - successCount;
        var message = failedCount == 0
            ? $"Supprimé {successCount}/{total} fichiers ({SizeFormatter.Format(totalSize)} libérés)"
            : $"Supprimé {successCount}/{total} fichiers ({SizeFormatter.Format(totalSize)} libérés) — {failedCount} non supprimé(s) (fichiers verrouillés/droits)";

        if (successCount > 0)
        {
            CleanupEmptyDirectoriesForProcessedItems(itemsList);
        }

        return new OperationResult(
            Success: failedCount == 0 && total > 0,
            Message: message,
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }

    private IEnumerable<string> EnumerateFilesSafe(string rootPath, bool recursive)
    {
        if (!recursive)
        {
            IEnumerable<string> topFiles = [];
            try
            {
                topFiles = Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible d'énumérer {Path}", rootPath);
            }

            foreach (string file in topFiles)
            {
                yield return file;
            }

            yield break;
        }

        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.Count > 0)
        {
            string current = directories.Pop();

            IEnumerable<string> files = [];
            try
            {
                files = Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible d'énumérer les fichiers de {Path}", current);
            }

            foreach (string file in files)
            {
                yield return file;
            }

            IEnumerable<string> subDirectories = [];
            try
            {
                subDirectories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible d'énumérer les dossiers de {Path}", current);
            }

            foreach (string subDirectory in subDirectories)
            {
                try
                {
                    var attrs = File.GetAttributes(subDirectory);
                    if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    {
                        continue;
                    }

                    directories.Push(subDirectory);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Impossible d'inspecter les attributs de {Path}", subDirectory);
                }
            }
        }
    }

    private void CleanupEmptyDirectoriesForProcessedItems(IEnumerable<CleanableItem> items)
    {
        string[] cleanupRoots =
        [
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(@"C:\Windows\Temp"),
            Path.GetFullPath(@"C:\Windows\Prefetch")
        ];

        foreach (string parentDirectory in items
            .Select(i => Path.GetDirectoryName(i.Path))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.GetFullPath(d!))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? current = parentDirectory;

            while (!string.IsNullOrWhiteSpace(current))
            {
                string? root = cleanupRoots.FirstOrDefault(r => current.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                if (root == null)
                {
                    break;
                }

                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                try
                {
                    if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        break;
                    }

                    Directory.Delete(current, recursive: false);
                    current = Path.GetDirectoryName(current);
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
