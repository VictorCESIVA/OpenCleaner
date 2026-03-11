using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Diagnostics;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>
/// Plugin ciblant les développeurs : node_modules orphelins, caches npm/yarn,
/// VS Code logs, Discord/Spotify/Slack caches, JetBrains logs.
/// </summary>
public class DevCleanerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<DevCleanerPlugin> _logger;

    public string Id          => "dev.cleaner";
    public string Name        => "Dev Cleaner";
    public string Description => "Nettoie node_modules orphelins, caches npm/yarn, VS Code, Discord, JetBrains…";
    public PluginCategory Category  => PluginCategory.System;
    public RiskLevel MaxRiskLevel   => RiskLevel.Safe;
    public bool IsAvailable         => true;
    public bool RequiresAdmin       => false;

    // Catégories activées (configurables depuis l'UI via sous-classe ou flags)
    public bool ScanNodeModules { get; set; } = true;
    public bool ScanNpmCache    { get; set; } = true;
    public bool ScanYarnCache   { get; set; } = true;
    public bool ScanVsCode      { get; set; } = true;
    public bool ScanDiscord     { get; set; } = true;
    public bool ScanSpotify     { get; set; } = true;
    public bool ScanJetBrains   { get; set; } = true;
    public bool ScanDocker      { get; set; } = true;

    private static readonly string AppData  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LocalApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public DevCleanerPlugin(IFileGuardian fileGuardian, ILogger<DevCleanerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ANALYZE
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
    {
        var items = new List<CleanableItem>();
        var tasks = new List<Func<Task>>();

        if (ScanNodeModules) tasks.Add(() => ScanOrphanNodeModules(items, ct));
        if (ScanNpmCache)    tasks.Add(() => ScanDir(items, Path.Combine(AppData, "npm-cache"),    "Cache npm",    ct));
        if (ScanYarnCache)   tasks.Add(() => ScanDir(items, Path.Combine(LocalApp, "Yarn", "Cache"), "Cache Yarn", ct));
        if (ScanVsCode)      tasks.Add(() => ScanVsCodeFiles(items, ct));
        if (ScanDiscord)     tasks.Add(() => ScanAppCaches(items, "discord", new[] { "Cache", "Code Cache", "GPUCache" }, ct));
        if (ScanSpotify)     tasks.Add(() => ScanAppCaches(items, "Spotify", new[] { "Cache", "Storage" }, ct));
        if (ScanJetBrains)   tasks.Add(() => ScanJetBrainsFiles(items, ct));
        if (ScanDocker)      tasks.Add(() => ScanDockerContexts(items, ct));

        int done = 0;
        foreach (var t in tasks)
        {
            ct.ThrowIfCancellationRequested();
            await t();
            done++;
            progress?.Report((double)done / tasks.Count);
        }

        _logger.LogInformation("Dev Cleaner : {Count} éléments trouvés", items.Count);
        return items;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SCAN HELPERS
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>node_modules dont le parent n'a plus de package.json.</summary>
    private Task ScanOrphanNodeModules(List<CleanableItem> items, CancellationToken ct)
    {
        var searchDirs = new[]
        {
            Path.Combine(UserProfile, "Documents"),
            Path.Combine(UserProfile, "Desktop"),
            Path.Combine(UserProfile, "Projects"),
            Path.Combine(UserProfile, "dev"),
            Path.Combine(UserProfile, "source"),
        }.Where(Directory.Exists);

        foreach (var root in searchDirs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var nmDir in Directory.EnumerateDirectories(root, "node_modules",
                    new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true }))
                {
                    ct.ThrowIfCancellationRequested();
                    var parent = Path.GetDirectoryName(nmDir)!;
                    if (!File.Exists(Path.Combine(parent, "package.json")))
                    {
                        var size = GetDirectorySize(nmDir);
                        items.Add(new CleanableItem(
                            Guid.NewGuid().ToString(), nmDir, size,
                            "node_modules orphelin — package.json absent",
                            RiskLevel.Safe, ItemType.Directory,
                            Directory.GetLastWriteTime(nmDir), Id));
                    }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "node_modules scan {Dir}", root); }
        }

        return Task.CompletedTask;
    }

    private Task ScanDir(List<CleanableItem> items, string dir, string desc, CancellationToken ct)
    {
        if (!Directory.Exists(dir)) return Task.CompletedTask;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (_fileGuardian.IsSystemCriticalPath(file)) continue;
                var info = new FileInfo(file);
                if (!info.Exists) continue;
                items.Add(new CleanableItem(Guid.NewGuid().ToString(), file, info.Length,
                    desc, RiskLevel.Safe, ItemType.File, info.LastAccessTime, Id));
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "ScanDir {Dir}", dir); }
        return Task.CompletedTask;
    }

    private Task ScanVsCodeFiles(List<CleanableItem> items, CancellationToken ct)
    {
        var codeDirs = new[]
        {
            Path.Combine(AppData, "Code", "logs"),
            Path.Combine(AppData, "Code", "CachedExtensionVSIXs"),
            Path.Combine(AppData, "Code", "Cache"),
            Path.Combine(AppData, "Code", "CachedData"),
        };

        foreach (var d in codeDirs)
            ScanDir(items, d, "VS Code cache/logs", ct).GetAwaiter().GetResult();

        return Task.CompletedTask;
    }

    private Task ScanAppCaches(List<CleanableItem> items, string appName, string[] subDirs, CancellationToken ct)
    {
        var appBase = Path.Combine(AppData, appName);
        if (!Directory.Exists(appBase)) return Task.CompletedTask;

        // Vérifie que le processus n'est pas en cours (avertissement non bloquant)
        var running = Process.GetProcessesByName(appName.ToLowerInvariant()).Any();
        if (running)
            _logger.LogWarning("{App} est en cours d'exécution — certains fichiers peuvent être verrouillés", appName);

        foreach (var sub in subDirs)
            ScanDir(items, Path.Combine(appBase, sub), $"{appName} — {sub}", ct).GetAwaiter().GetResult();

        return Task.CompletedTask;
    }

    private Task ScanJetBrainsFiles(List<CleanableItem> items, CancellationToken ct)
    {
        var jbBase = Path.Combine(AppData, "JetBrains");
        if (!Directory.Exists(jbBase)) return Task.CompletedTask;

        foreach (var ideDir in Directory.EnumerateDirectories(jbBase))
        {
            ScanDir(items, Path.Combine(ideDir, "system", "log"), "JetBrains logs", ct).GetAwaiter().GetResult();
            ScanDir(items, Path.Combine(ideDir, "system", "index", "caches"), "JetBrains index cache", ct).GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    private Task ScanDockerContexts(List<CleanableItem> items, CancellationToken ct)
    {
        var dockerContext = Path.Combine(UserProfile, ".docker", "contexts", "meta");
        if (!Directory.Exists(dockerContext)) return Task.CompletedTask;

        foreach (var dir in Directory.EnumerateDirectories(dockerContext))
        {
            ct.ThrowIfCancellationRequested();
            // Garder le contexte "default"
            var meta = Path.Combine(dir, "meta.json");
            if (File.Exists(meta))
            {
                var content = File.ReadAllText(meta);
                if (content.Contains("\"default\"", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var size = GetDirectorySize(dir);
            items.Add(new CleanableItem(Guid.NewGuid().ToString(), dir, size,
                "Docker context non-default", RiskLevel.Safe, ItemType.Directory,
                Directory.GetLastWriteTime(dir), Id));
        }

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CLEAN
    // ─────────────────────────────────────────────────────────────────────

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
    {
        var list = items.ToList();
        int ok = 0; long freed = 0; int i = 0;

        foreach (var item in list)
        {
            ct.ThrowIfCancellationRequested();

            if (item.Type == ItemType.Directory)
            {
                // Supprimer le dossier entier (node_modules, docker context)
                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        Directory.Delete(item.Path, recursive: true);
                        ok++; freed += item.Size;
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Delete dir {Path}", item.Path); }
            }
            else
            {
                var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
                if (result.Success) { ok++; freed += item.Size; }
            }

            progress?.Report((double)++i / list.Count);
        }

        var mb = freed / 1_048_576.0;
        return new OperationResult(ok > 0, $"{ok}/{list.Count} fichiers supprimés ({mb:0.#} Mo)", Guid.NewGuid());
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default) => Task.FromResult(0L);

    // ─────────────────────────────────────────────────────────────────────
    //  UTILITAIRES
    // ─────────────────────────────────────────────────────────────────────

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })
                .Sum();
        }
        catch { return 0; }
    }
}
