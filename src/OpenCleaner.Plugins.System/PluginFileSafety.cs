using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Plugins.System;

/// <summary>Shared scan boundaries. Inaccessible entries and NTFS junctions are never followed.</summary>
internal static class PluginFileSafety
{
    internal static bool IsRegularPath(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var fullPath = Path.GetFullPath(path);
            for (string? current = fullPath; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(current, Path.GetPathRoot(current), StringComparison.OrdinalIgnoreCase)) break;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root, string pattern, bool recursive,
        CancellationToken ct, int maxDepth = int.MaxValue)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(root) || !IsRegularPath(root)) yield break;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var entry))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRegularPath(entry.Path)) continue;
            foreach (var file in GetEntries(entry.Path, pattern, directories: false))
            {
                ct.ThrowIfCancellationRequested();
                if (IsRegularPath(file)) yield return file;
            }
            if (!recursive || entry.Depth >= maxDepth) continue;
            foreach (var directory in GetEntries(entry.Path, "*", directories: true))
            {
                ct.ThrowIfCancellationRequested();
                if (IsRegularPath(directory)) pending.Push((directory, entry.Depth + 1));
            }
        }
    }

    internal static string[] GetEntries(string root, string pattern, bool directories)
    {
        try
        {
            if (!IsRegularPath(root)) return [];
            return directories ? Directory.GetDirectories(root, pattern) : Directory.GetFiles(root, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}

/// <summary>A selection is valid only for the scan that produced it and unchanged regular files.</summary>
internal sealed class ScannedFileSelection
{
    private sealed record Entry(CleanableItem Item, DateTime LastWriteUtc);
    private readonly object _sync = new();
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal void Reset()
    {
        lock (_sync) _entries = new(StringComparer.Ordinal);
    }

    internal void Publish(IEnumerable<CleanableItem> items)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            try
            {
                var info = new FileInfo(item.Path);
                if (item.Type == ItemType.File && PluginFileSafety.IsRegularPath(item.Path) && info.Exists && info.Length == item.Size)
                    entries[item.Id] = new Entry(item, info.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        lock (_sync) _entries = entries;
    }

    internal bool IsValid(CleanableItem item)
    {
        Entry? entry;
        lock (_sync) _entries.TryGetValue(item.Id, out entry);
        if (entry == null || entry.Item != item || !PluginFileSafety.IsRegularPath(item.Path)) return false;
        try
        {
            var info = new FileInfo(item.Path);
            return info.Exists && info.Length == item.Size && info.LastWriteTimeUtc == entry.LastWriteUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal async Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IFileGuardian guardian,
        ILogger logger, IProgress<double>? progress, CancellationToken ct,
        Func<CleanableItem, CancellationToken, Task<bool>>? validate = null)
    {
        var list = items.DistinctBy(item => item.Id).ToList();
        int success = 0;
        long bytes = 0;
        for (int index = 0; index < list.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var item = list[index];
            try
            {
                if (!IsValid(item) || guardian.IsSystemCriticalPath(item.Path) ||
                    (validate != null && !await validate(item, ct)))
                {
                    logger.LogWarning("Sélection absente, modifiée ou protégée : {Path}", item.Path);
                }
                else
                {
                    // Never degrade to unprotected deletion when creating a backup fails.
                    var result = await guardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
                    if (result.Success)
                    {
                        success++;
                        bytes += result.BytesRecovered ?? 0;
                    }
                    else logger.LogWarning("Suppression refusée : {Path} ({Reason})", item.Path, result.Message);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Suppression impossible : {Path}", item.Path);
            }
            progress?.Report((index + 1d) / list.Count);
        }
        progress?.Report(1);
        return new OperationResult(success == list.Count,
            $"{success}/{list.Count} fichiers supprimés ({SizeFormatter.Format(bytes)} d'espace net récupéré, sauvegardes conservées)",
            Guid.NewGuid(), ItemsProcessed: success, BytesRecovered: bytes);
    }
}

public abstract class SafeFileCleanerPlugin : ICleanerPlugin
{
    protected sealed record ScanTarget(string Path, string Description, RiskLevel Risk = RiskLevel.Safe,
        string Pattern = "*", TimeSpan? MinAge = null, bool Recursive = true);
    private readonly ScannedFileSelection _selection = new();
    protected readonly IFileGuardian FileGuardian;
    protected readonly ILogger Logger;

    protected SafeFileCleanerPlugin(IFileGuardian fileGuardian, ILogger logger)
    {
        FileGuardian = fileGuardian;
        Logger = logger;
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract PluginCategory Category { get; }
    public abstract RiskLevel MaxRiskLevel { get; }
    public virtual bool IsAvailable => OperatingSystem.IsWindows();
    public virtual bool RequiresAdmin => false;
    protected abstract IEnumerable<ScanTarget> GetTargets();
    protected virtual bool CanClean() => true;

    public Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _selection.Reset();
        return Task.Run<IReadOnlyList<CleanableItem>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var targets = GetTargets().ToList();
            var items = new List<CleanableItem>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                foreach (var file in PluginFileSafety.EnumerateFiles(target.Path, target.Pattern, target.Recursive, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (!visited.Add(file) || FileGuardian.IsSystemCriticalPath(file) || FileGuardian.IsFileLocked(file)) continue;
                        var info = new FileInfo(file);
                        // Last-access updates can be disabled on NTFS; require both write/access to be old.
                        var lastUse = info.LastWriteTimeUtc > info.LastAccessTimeUtc ? info.LastWriteTimeUtc : info.LastAccessTimeUtc;
                        if (!info.Exists || (target.MinAge != null && DateTime.UtcNow - lastUse < target.MinAge)) continue;
                        items.Add(new CleanableItem(Guid.NewGuid().ToString(), file, info.Length,
                            $"{target.Description} — {info.Name}", target.Risk, ItemType.File, info.LastAccessTime, Id));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.LogDebug(ex, "Fichier inaccessible : {Path}", file);
                    }
                }
                progress?.Report((index + 1d) / targets.Count);
            }
            ct.ThrowIfCancellationRequested();
            _selection.Publish(items);
            progress?.Report(1);
            return items;
        }, ct);
    }

    public Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IBackupManager backupManager,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanClean()) return Task.FromResult(new OperationResult(false,
            "Fermez les applications concernées puis relancez l'analyse avant de nettoyer.", Guid.NewGuid(),
            ItemsProcessed: 0, BytesRecovered: 0));
        return _selection.CleanAsync(items, FileGuardian, Logger, progress, ct);
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}
