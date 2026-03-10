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
            (Path: Path.GetTempPath(), MinAge: TimeSpan.FromHours(24), Risk: RiskLevel.Safe),
            (Path: @"C:\Windows\Temp", MinAge: TimeSpan.FromDays(7), Risk: RiskLevel.Recommended),
            (Path: @"C:\Windows\Prefetch", MinAge: TimeSpan.FromDays(30), Risk: RiskLevel.ExpertOnly)
        };

        int totalLocations = locations.Length;
        int currentLocation = 0;

        foreach (var loc in locations)
        {
            if (!Directory.Exists(loc.Path)) continue;

            try
            {
                var files = Directory.EnumerateFiles(loc.Path, "*.*", SearchOption.TopDirectoryOnly);
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

        var message = $"Supprimé {successCount}/{total} fichiers ({totalSize.BytesToHuman()} libérés)";
        return new OperationResult(
            Success: successCount > 0,
            Message: message,
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}

internal static class ByteExtensions
{
    public static string BytesToHuman(this long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
