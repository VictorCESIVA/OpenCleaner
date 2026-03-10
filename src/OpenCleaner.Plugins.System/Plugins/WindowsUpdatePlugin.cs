using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;

namespace OpenCleaner.Plugins.System.Plugins;

public class WindowsUpdatePlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<WindowsUpdatePlugin> _logger;

    public string Id => "system.windowsupdate";
    public string Name => "Windows Update";
    public string Description => "Nettoie les téléchargements de mise à jour Windows (SoftwareDistribution)";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.Safe;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public WindowsUpdatePlugin(IFileGuardian fileGuardian, ILogger<WindowsUpdatePlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        var wuPaths = new[]
        {
            (Path: @"C:\Windows\SoftwareDistribution\Download", MinAge: 3, Desc: "Téléchargements Windows Update"),
            (Path: @"C:\Windows\SoftwareDistribution\PostRebootEventCache.V2", MinAge: 7, Desc: "Cache événements post-redémarrage"),
            (Path: @"C:\Windows\SoftwareDistribution\DataStore\Logs", MinAge: 30, Desc: "Logs DataStore")
        };

        foreach (var target in wuPaths)
        {
            if (!Directory.Exists(target.Path)) continue;

            try
            {
                if (_fileGuardian.IsSystemCriticalPath(target.Path))
                {
                    _logger.LogWarning("Chemin critique ignoré: {Path}", target.Path);
                    continue;
                }

                var files = Directory.EnumerateFiles(target.Path, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;

                        var age = DateTime.Now - info.LastAccessTime;
                        if (age.Days < target.MinAge) continue;

                        items.Add(new CleanableItem(
                            Id: Guid.NewGuid().ToString(),
                            Path: file,
                            Size: info.Length,
                            Description: $"{target.Desc} - {Path.GetFileName(file)}",
                            RiskLevel: RiskLevel.Safe,
                            Type: ItemType.File,
                            LastAccessTime: info.LastAccessTime,
                            ParentPluginId: Id
                        ));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur accès {Path}", target.Path);
            }
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
        int success = 0;
        long totalSize = 0;

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await _fileGuardian.SafeDeleteAsync(item.Path, true, ct);
                if (result.Success)
                {
                    success++;
                    totalSize += item.Size;
                }
            }
            catch { }

            progress?.Report((double)success / itemsList.Count * 100);
        }

        var gb = totalSize / 1024.0 / 1024.0 / 1024.0;
        return new OperationResult(
            Success: success > 0,
            Message: gb > 1
                ? $"🎉 Windows Update nettoyé : {gb:F1} Go libérés !"
                : $"Windows Update nettoyé : {totalSize / 1024 / 1024} Mo libérés",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
