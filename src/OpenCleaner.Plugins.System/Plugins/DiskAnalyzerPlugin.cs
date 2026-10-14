using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class DiskAnalyzerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<DiskAnalyzerPlugin> _logger;
    public string Id => "disk.analyzer";
    public string Name => "Analyseur d'espace disque";
    public string Description => "Taille accessible des enfants d'un dossier, sans suivre les jonctions";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.Safe;
    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool RequiresAdmin => false;
    public string ScanRoot { get; set; } = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    public DiskAnalyzerPlugin(IFileGuardian fileGuardian, ILogger<DiskAnalyzerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
    }

    public Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var root = ScanRoot; // A drill-down change cannot alter the in-flight scan.
        return Task.Run<IReadOnlyList<CleanableItem>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var results = new List<CleanableItem>();
            if (!PluginFileSafety.IsRegularPath(root)) { progress?.Report(1); return results; }
            foreach (var file in PluginFileSafety.EnumerateFiles(root, "*", recursive: false, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 0)
                        results.Add(MakeItem(file, info.Length, ItemType.File, info.LastWriteTime));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            var directories = PluginFileSafety.GetEntries(root, "*", directories: true);
            for (int index = 0; index < directories.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var directory = directories[index];
                if (PluginFileSafety.IsRegularPath(directory) && !_fileGuardian.IsSystemCriticalPath(directory))
                {
                    long size = 0;
                    foreach (var file in PluginFileSafety.EnumerateFiles(directory, "*", recursive: true, ct))
                    {
                        try { size += new FileInfo(file).Length; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                    if (size > 0) results.Add(MakeItem(directory, size, ItemType.Directory, Directory.GetLastWriteTime(directory)));
                }
                progress?.Report((index + 1d) / directories.Length);
            }
            results.Sort((a, b) => b.Size.CompareTo(a.Size));
            progress?.Report(1);
            _logger.LogInformation("Analyse disque : {Count} enfants accessibles", results.Count);
            return results;
        }, ct);
    }

    private CleanableItem MakeItem(string path, long size, ItemType type, DateTime modified)
        => new(Guid.NewGuid().ToString(), path, size, $"{SizeFormatter.Format(size)} — {path}",
            RiskLevel.Safe, type, modified, Id);

    public static List<CleanableItem> FilterBySize(IReadOnlyList<CleanableItem> items, long minBytes)
        => items.Where(i => i.Size >= Math.Max(0, minBytes)).ToList();

    public Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IBackupManager backupManager,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationResult(false, "L'analyseur disque est en lecture seule.",
            Guid.NewGuid(), ItemsProcessed: 0, BytesRecovered: 0));
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}
