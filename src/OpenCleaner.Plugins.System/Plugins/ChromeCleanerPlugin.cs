using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace OpenCleaner.Plugins.System.Plugins;

public class ChromeCleanerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<ChromeCleanerPlugin> _logger;

    public string Id => "browser.chrome";
    public string Name => "Nettoyeur Chrome/Edge";
    public string Description => "Nettoie cache, cookies et historique Chrome/Edge/Brave";
    public PluginCategory Category => PluginCategory.Browser;
    public RiskLevel MaxRiskLevel => RiskLevel.Recommended;

    public bool IsAvailable => true;
    public bool RequiresAdmin => false;

    public ChromeCleanerPlugin(IFileGuardian fileGuardian, ILogger<ChromeCleanerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanableItem>();

        // Chemins des profils Chrome/Edge/Brave
        var browserPaths = new[]
        {
            (Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default"), Name: "Chrome"),
            (Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default"), Name: "Edge"),
            (Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\User Data\Default"), Name: "Brave")
        };

        int current = 0;
        foreach (var browser in browserPaths)
        {
            if (!Directory.Exists(browser.Path))
            {
                current++;
                continue;
            }

            // Vérifie si le navigateur tourne
            var processName = browser.Name.ToLower() switch
            {
                "chrome" => "chrome",
                "edge" => "msedge",
                "brave" => "brave",
                _ => browser.Name.ToLower()
            };

            bool isRunning = Process.GetProcessesByName(processName).Any();
            if (isRunning)
            {
                _logger.LogWarning("{Browser} est en cours d'exécution. Certaines données peuvent être verrouillées.", browser.Name);
            }

            // Cibles par dossier (filtrées selon les options utilisateur)
            var dirTargets = new[]
            {
                (Path: Path.Combine(browser.Path, "Cache"), Pattern: "*.*", MinAge: TimeSpan.FromHours(1), Risk: RiskLevel.Safe, Desc: "Cache", Opt: BrowserCleanOptions.IncludeCache),
                (Path: Path.Combine(browser.Path, @"Code Cache"), Pattern: "*.*", MinAge: TimeSpan.FromHours(1), Risk: RiskLevel.Safe, Desc: "Code Cache JS", Opt: BrowserCleanOptions.IncludeCodeCache),
                (Path: Path.Combine(browser.Path, "GPUCache"), Pattern: "*.*", MinAge: TimeSpan.FromHours(1), Risk: RiskLevel.Safe, Desc: "Cache GPU", Opt: BrowserCleanOptions.IncludeGpuCache),
                (Path: Path.Combine(browser.Path, "Service Worker"), Pattern: "*.*", MinAge: TimeSpan.FromDays(7), Risk: RiskLevel.Recommended, Desc: "Service Workers", Opt: BrowserCleanOptions.IncludeServiceWorkers)
            };

            foreach (var target in dirTargets.Where(t => t.Opt))
            {
                if (!Directory.Exists(target.Path)) continue;

                try
                {
                    var files = Directory.EnumerateFiles(target.Path, target.Pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var info = new FileInfo(file);
                            if (!info.Exists) continue;

                            if (_fileGuardian.IsSystemCriticalPath(file)) continue;

                            var age = DateTime.Now - info.LastAccessTime;
                            if (age < target.MinAge) continue;

                            // Skip si verrouillé ET navigateur tourne
                            if (isRunning && _fileGuardian.IsFileLocked(file)) continue;

                            items.Add(new CleanableItem(
                                Id: Guid.NewGuid().ToString(),
                                Path: file,
                                Size: info.Length,
                                Description: $"{browser.Name} {target.Desc} - {Path.GetFileName(file)}",
                                RiskLevel: target.Risk,
                                Type: ItemType.File,
                                LastAccessTime: info.LastAccessTime,
                                ParentPluginId: Id
                            ));
                        }
                        catch { /* ignore fichier individuel */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur analyse {Target}", target.Path);
                }
            }

            // Cibles fichiers (Cookies, Historique, Favicons) — seulement si navigateur fermé
            var fileTargets = new[]
            {
                (Path: Path.Combine(browser.Path, "Network", "Cookies"), Desc: "Cookies", Opt: BrowserCleanOptions.IncludeCookies, Risk: RiskLevel.Recommended),
                (Path: Path.Combine(browser.Path, "Cookies"), Desc: "Cookies (legacy)", Opt: BrowserCleanOptions.IncludeCookies, Risk: RiskLevel.Recommended),
                (Path: Path.Combine(browser.Path, "History"), Desc: "Historique", Opt: BrowserCleanOptions.IncludeHistory, Risk: RiskLevel.Recommended),
                (Path: Path.Combine(browser.Path, "Favicons"), Desc: "Favicons", Opt: BrowserCleanOptions.IncludeFavicons, Risk: RiskLevel.Safe),
            };

            foreach (var target in fileTargets.Where(t => t.Opt))
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(target.Path)) continue;
                if (isRunning && _fileGuardian.IsFileLocked(target.Path)) continue;
                if (_fileGuardian.IsSystemCriticalPath(target.Path)) continue;

                try
                {
                    var info = new FileInfo(target.Path);
                    items.Add(new CleanableItem(
                        Id: Guid.NewGuid().ToString(),
                        Path: target.Path,
                        Size: info.Length,
                        Description: $"{browser.Name} {target.Desc}",
                        RiskLevel: target.Risk,
                        Type: ItemType.File,
                        LastAccessTime: info.LastAccessTime,
                        ParentPluginId: Id
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur analyse {Target}", target.Path);
                }
            }

            current++;
            progress?.Report((double)current / browserPaths.Length);
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
                if (_fileGuardian.IsSystemCriticalPath(item.Path)) continue;

                var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
                if (result.Success)
                {
                    successCount++;
                    totalSize += item.Size;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur suppression {Path}", item.Path);
            }

            current++;
            progress?.Report((double)current / total);
        }

        return new OperationResult(
            Success: successCount > 0,
            Message: $"Nettoyé {successCount} fichiers navigateur ({totalSize / 1024 / 1024} Mo)",
            TransactionId: Guid.NewGuid()
        );
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
