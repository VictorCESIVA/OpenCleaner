using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Security;
using System.Security.Cryptography;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>Personal-file duplicates require explicit review; application data is excluded.</summary>
public sealed class DuplicateFinderPlugin : ICleanerPlugin
{
    private sealed record DuplicateEvidence(string OriginalPath, string Hash);
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<DuplicateFinderPlugin> _logger;
    private readonly string[] _roots;
    private readonly ScannedFileSelection _selection = new();
    private Dictionary<string, DuplicateEvidence> _evidence = new(StringComparer.Ordinal);
    private const long DefaultMinSizeBytes = 1024 * 1024;
    private long _minSizeBytes = DefaultMinSizeBytes;

    public long MinFileSizeBytes
    {
        get => _minSizeBytes;
        set => _minSizeBytes = Math.Max(1024L, value);
    }

    public string Id => "system.duplicates";
    public string Name => "Détecteur de doublons";
    public string Description => "Compare les fichiers personnels de plus de 1 Mo ; suppression après vérification";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.ExpertOnly;
    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool RequiresAdmin => false;
    public event Action<string>? OnFileHashing;

    public DuplicateFinderPlugin(IFileGuardian fileGuardian, ILogger<DuplicateFinderPlugin> logger)
        : this(fileGuardian, logger, DefaultRoots()) { }

    internal DuplicateFinderPlugin(IFileGuardian fileGuardian, ILogger<DuplicateFinderPlugin> logger, IEnumerable<string> roots)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
        _roots = roots.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> DefaultRoots()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic })
            yield return Environment.GetFolderPath(folder);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _selection.Reset();
        _evidence = new(StringComparer.Ordinal);
        var candidates = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots)
            foreach (var info in PluginFileSafety.EnumerateFileInfos(root, "*", recursive: true, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (info.Exists && info.Length >= _minSizeBytes &&
                        !CleaningExclusions.IsExcluded(info.FullName) &&
                        !_fileGuardian.IsSystemCriticalPathForScan(info.FullName))
                        candidates.TryAdd(info.FullName, info);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

        var sizeGroups = candidates.Values.GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
        var partialSuspects = new List<FileInfo>();
        foreach (var sizeGroup in sizeGroups)
        {
            var byPartialHash = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
            foreach (var info in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var partialHash = await HashHelper.ComputePartialHashAsync(info.FullName, ct);
                    if (!byPartialHash.TryGetValue(partialHash, out var bucket))
                        byPartialHash[partialHash] = bucket = [];
                    bucket.Add(info);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Pré-comparaison impossible : {Path}", info.FullName);
                }
            }

            foreach (var bucket in byPartialHash.Values)
            {
                if (bucket.Count > 1)
                    partialSuspects.AddRange(bucket);
            }
        }

        var hashes = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
        var hashLock = new object();
        int done = 0;
        int total = partialSuspects.Count;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
        };
        try
        {
            await Parallel.ForEachAsync(partialSuspects, parallelOptions, async (info, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var length = info.Length;
                    var modified = info.LastWriteTimeUtc;
                    OnFileHashing?.Invoke(info.FullName);
                    var hash = await HashHelper.ComputeHashAsync(info.FullName, token).ConfigureAwait(false);
                    info.Refresh();
                    if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != modified) return;
                    lock (hashLock)
                    {
                        if (!hashes.TryGetValue(hash, out var group)) hashes[hash] = group = [];
                        group.Add(info);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Comparaison impossible : {Path}", info.FullName);
                }
                finally
                {
                    int current = Interlocked.Increment(ref done);
                    if (total > 0) progress?.Report(current / (double)total);
                }
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(
                   inner => inner is OperationCanceledException))
        {
            throw new OperationCanceledException(ct);
        }
        progress?.Report(1);

        var items = new List<CleanableItem>();
        var published = new List<(CleanableItem Item, DateTime LastWriteUtc)>();
        foreach (var group in hashes.Where(group => group.Value.Count > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            // Stable order designates a keeper, without pretending to infer which file is the original.
            var ordered = group.Value.OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
            var original = ordered[0].FullName;
            var groupId = Guid.NewGuid().ToString();
            foreach (var copy in ordered.Skip(1))
            {
                copy.Refresh();
                if (!copy.Exists) continue;
                // A second name for the same data frees nothing when removed: never propose it.
                if (!HardLinkInspector.HasSingleLink(copy.FullName))
                {
                    _logger.LogDebug("Doublon ignoré : plusieurs liens physiques ({Path})", copy.FullName);
                    continue;
                }
                var item = new CleanableItem(Guid.NewGuid().ToString(), copy.FullName, copy.Length,
                    $"Identique à « {Path.GetFileName(original)} » — copie conservée : {original}",
                    RiskLevel.ExpertOnly, ItemType.File, copy.LastAccessTime, Id, groupId, original);
                items.Add(item);
                published.Add((item, copy.LastWriteTimeUtc));
                _evidence[item.Id] = new DuplicateEvidence(original, group.Key);
            }
        }
        ct.ThrowIfCancellationRequested();
        _selection.PublishKnown(published);
        progress?.Report(1);
        return items;
    }

    public async Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IBackupManager backupManager,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var keepers = new Dictionary<string, (FileStream Stream, string Hash)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return await _selection.CleanAsync(items, _fileGuardian, _logger, progress, ct, async (item, token) =>
            {
                if (!_evidence.TryGetValue(item.Id, out var evidence) ||
                    !PluginFileSafety.IsRegularPath(evidence.OriginalPath)) return false;
                // Recheck the physical names: a hard link added after the scan frees nothing.
                if (!HardLinkInspector.HasSingleLink(item.Path)) return false;
                if (!keepers.TryGetValue(evidence.OriginalPath, out var keeper))
                {
                    // Keep the surviving copy open without write/delete sharing throughout the cleanup,
                    // and compute its SHA-256 once while locked.
                    var stream = new FileStream(evidence.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                    keeper = (stream, hash);
                    keepers[evidence.OriginalPath] = keeper;
                }
                var copyHash = await HashHelper.ComputeHashAsync(item.Path, token);
                return keeper.Hash.Equals(evidence.Hash, StringComparison.OrdinalIgnoreCase) &&
                    copyHash.Equals(evidence.Hash, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally { foreach (var keeper in keepers.Values) await keeper.Stream.DisposeAsync(); }
    }
}
