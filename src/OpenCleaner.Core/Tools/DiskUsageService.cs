namespace OpenCleaner.Core.Tools;

/// <summary>Un dossier ou un fichier mesuré, avec les totaux de son sous-arbre.</summary>
public sealed class DiskUsageNode
{
    public DiskUsageNode(string name, string fullPath, bool isFile)
        : this(name, fullPath, isFile, size: 0)
    {
    }

    /// <summary>Crée une entrée agrégée (résidu, total synthétique) avec une taille explicite.</summary>
    public DiskUsageNode(string name, string fullPath, bool isFile, long size, bool isProtected = false)
    {
        Name = name;
        FullPath = fullPath;
        IsFile = isFile;
        Size = size;
        IsProtected = isProtected;
        if (isFile) DirectFileBytes = size;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFile { get; }

    /// <summary>Taille du fichier, ou du dossier avec tout son contenu.</summary>
    public long Size { get; set; }

    /// <summary>Octets des fichiers directement contenus (hors sous-dossiers).</summary>
    public long DirectFileBytes { get; set; }

    /// <summary>Nombre de fichiers du sous-arbre.</summary>
    public int FileCount { get; set; }

    /// <summary>Nombre de dossiers du sous-arbre, dossier courant inclus.</summary>
    public int DirectoryCount { get; set; }

    /// <summary>Fichiers directs trop petits pour être listés individuellement.</summary>
    public int OtherFileCount { get; set; }

    public int SkippedEntries { get; set; }
    public bool IsProtected { get; set; }
    public DateTime LastWriteTime { get; set; }
    public List<DiskUsageNode> Directories { get; } = [];
    public List<DiskUsageNode> Files { get; } = [];
}

public sealed record ExtensionUsage(string Extension, long Size, int Count);

public sealed record DiskUsageResult(
    DiskUsageNode Root,
    long TotalSize,
    int TotalFiles,
    int TotalDirectories,
    int SkippedEntries,
    IReadOnlyList<DiskUsageNode> LargestFiles,
    IReadOnlyList<ExtensionUsage> Extensions);

public sealed record DiskUsageOptions
{
    /// <summary>Taille minimale pour qu'un fichier apparaisse individuellement.</summary>
    public long MinFileSizeForListing { get; init; } = 1024 * 1024;
    public int MaxLargestFiles { get; init; } = 200;
    public int MaxFilesPerDirectory { get; init; } = 256;
    public int MaxExtensions { get; init; } = 12;
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
    /// <summary>Marque en lecture seule les chemins protégés par le gardien (aucune suppression ici).</summary>
    public Func<string, bool>? IsProtected { get; init; }
}

/// <summary>
/// Parcourt une arborescence une seule fois et construit les totaux par dossier,
/// les plus gros fichiers et la répartition par extension. Lecture seule, jonctions ignorées.
/// </summary>
public sealed class DiskUsageService
{
    private static readonly EnumerationOptions SafeOptions = new()
    {
        RecurseSubdirectories = false,
        // Les liens sont comptés puis ignorés manuellement : rien n'est jamais suivi.
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    public DiskUsageResult Scan(
        string root,
        DiskUsageOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new DiskUsageOptions();
        ct.ThrowIfCancellationRequested();

        string fullRoot = Path.GetFullPath(root);
        var rootInfo = new DirectoryInfo(fullRoot);
        if (!rootInfo.Exists) throw new DirectoryNotFoundException($"Dossier introuvable : {fullRoot}");
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Le dossier analysé est un lien ou une jonction.");

        var state = new ScanState();
        var extensions = new ExtensionsAggregator();
        var topFiles = new TopFileCollector(Math.Max(1, options.MaxLargestFiles), tie: false);
        var rootNode = new DiskUsageNode(fullRoot, fullRoot, isFile: false)
        {
            LastWriteTime = TryGetLastWrite(rootInfo),
            IsProtected = options.IsProtected?.Invoke(fullRoot) ?? false
        };

        AddDirectFiles(rootInfo, rootNode, options, extensions, topFiles, state, ct);

        List<DirectoryInfo> directories;
        try
        {
            directories = rootInfo.EnumerateDirectories("*", SafeOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            directories = [];
            state.AddSkipped();
        }

        var children = new DiskUsageNode?[directories.Count];
        int completed = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism)
        };
        Parallel.For(0, directories.Count, parallelOptions, index =>
        {
            ct.ThrowIfCancellationRequested();
            var directory = directories[index];
            try
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    state.AddSkipped();
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.AddSkipped();
                return;
            }

            var localExtensions = new ExtensionsAggregator();
            var localTop = new TopFileCollector(Math.Max(1, options.MaxLargestFiles), tie: false);
            children[index] = BuildDirectory(directory, options, state, localExtensions, localTop, ct);
            lock (state.Sync)
            {
                extensions.Merge(localExtensions);
                topFiles.Merge(localTop);
            }
            int done = Interlocked.Increment(ref completed);
            progress?.Report((double)done / directories.Count);
        });

        foreach (var child in children)
        {
            if (child == null) continue;
            rootNode.Directories.Add(child);
            rootNode.Size += child.Size;
            rootNode.FileCount += child.FileCount;
            rootNode.DirectoryCount += child.DirectoryCount;
            rootNode.SkippedEntries += child.SkippedEntries;
        }
        rootNode.Size += rootNode.DirectFileBytes;
        rootNode.DirectoryCount += 1;
        rootNode.SkippedEntries = state.Skipped;
        SortDescending(rootNode.Directories);
        SortDescending(rootNode.Files);
        progress?.Report(1);

        return new DiskUsageResult(
            rootNode,
            rootNode.Size,
            rootNode.FileCount,
            rootNode.DirectoryCount,
            rootNode.SkippedEntries,
            topFiles.Snapshot(),
            extensions.Top(options.MaxExtensions));
    }

    private static DiskUsageNode BuildDirectory(DirectoryInfo directory, DiskUsageOptions options,
        ScanState state, ExtensionsAggregator extensions, TopFileCollector topFiles, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = new DiskUsageNode(directory.Name, directory.FullName, isFile: false)
        {
            LastWriteTime = TryGetLastWrite(directory),
            IsProtected = options.IsProtected?.Invoke(directory.FullName) ?? false
        };

        AddDirectFiles(directory, node, options, extensions, topFiles, state, ct);

        List<DirectoryInfo> subdirectories;
        try
        {
            subdirectories = directory.EnumerateDirectories("*", SafeOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            subdirectories = [];
            state.AddSkipped();
        }

        node.DirectoryCount = 1;
        foreach (var child in subdirectories)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    state.AddSkipped();
                    node.SkippedEntries++;
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.AddSkipped();
                node.SkippedEntries++;
                continue;
            }

            var childNode = BuildDirectory(child, options, state, extensions, topFiles, ct);
            node.Directories.Add(childNode);
            node.Size += childNode.Size;
            node.FileCount += childNode.FileCount;
            node.DirectoryCount += childNode.DirectoryCount;
            node.SkippedEntries += childNode.SkippedEntries;
        }

        node.Size += node.DirectFileBytes;
        SortDescending(node.Directories);
        SortDescending(node.Files);
        return node;
    }

    private static void AddDirectFiles(DirectoryInfo directory, DiskUsageNode node, DiskUsageOptions options,
        ExtensionsAggregator extensions, TopFileCollector topFiles, ScanState state, CancellationToken ct)
    {
        var listed = new TopFileCollector(Math.Max(1, options.MaxFilesPerDirectory), tie: true);
        long directBytes = 0;
        int directFiles = 0;
        int checkedFiles = 0;
        try
        {
            foreach (var file in directory.EnumerateFiles("*", SafeOptions))
            {
                if ((++checkedFiles & 0xFF) == 0) ct.ThrowIfCancellationRequested();
                try
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        state.AddSkipped();
                        node.SkippedEntries++;
                        continue;
                    }

                    long length = file.Length;
                    if (length < 0) continue;
                    directBytes += length;
                    directFiles++;
                    extensions.Add(Path.GetExtension(file.Name), length);
                    if (length >= options.MinFileSizeForListing)
                    {
                        var entry = new DiskUsageNode(file.Name, file.FullName, isFile: true)
                        {
                            Size = length,
                            DirectFileBytes = length,
                            FileCount = 1,
                            LastWriteTime = TryGetLastWrite(file)
                        };
                        listed.Add(entry);
                        topFiles.Add(entry);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    state.AddSkipped();
                    node.SkippedEntries++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.AddSkipped();
        }

        ct.ThrowIfCancellationRequested();
        node.DirectFileBytes = directBytes;
        node.FileCount = directFiles;
        node.Files.AddRange(listed.Snapshot());
        node.OtherFileCount = Math.Max(0, directFiles - node.Files.Count);
    }

    private static void SortDescending(List<DiskUsageNode> nodes) =>
        nodes.Sort((left, right) => right.Size.CompareTo(left.Size));

    private static DateTime TryGetLastWrite(FileSystemInfo info)
    {
        try { return info.LastWriteTimeUtc; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private sealed class ScanState
    {
        private int _skipped;
        public object Sync { get; } = new();
        public void AddSkipped() => Interlocked.Increment(ref _skipped);
        public int Skipped => Volatile.Read(ref _skipped);
    }

    private sealed class ExtensionsAggregator
    {
        private readonly Dictionary<string, (long Size, int Count)> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string extension, long size)
        {
            string key = string.IsNullOrEmpty(extension) ? "(aucune)" : extension.ToLowerInvariant();
            _values.TryGetValue(key, out var current);
            _values[key] = (current.Size + size, current.Count + 1);
        }

        public void Merge(ExtensionsAggregator other)
        {
            foreach (var (key, value) in other._values)
            {
                _values.TryGetValue(key, out var current);
                _values[key] = (current.Size + value.Size, current.Count + value.Count);
            }
        }

        public IReadOnlyList<ExtensionUsage> Top(int count) =>
            _values.Select(pair => new ExtensionUsage(pair.Key, pair.Value.Size, pair.Value.Count))
                .OrderByDescending(usage => usage.Size)
                .ThenBy(usage => usage.Extension, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, count))
                .ToList();
    }

    /// <summary>Conserve les N plus gros fichiers sans trier des millions d'entrées.</summary>
    private sealed class TopFileCollector
    {
        private readonly int _capacity;
        private readonly bool _tie;
        private readonly List<DiskUsageNode> _items;

        public TopFileCollector(int capacity, bool tie)
        {
            _capacity = capacity;
            _tie = tie;
            _items = new List<DiskUsageNode>(Math.Min(capacity, 64));
        }

        public void Add(DiskUsageNode node)
        {
            if (_items.Count < _capacity)
            {
                int index = _items.BinarySearch(node, DescendingComparer.Instance);
                if (index < 0) index = ~index;
                _items.Insert(index, node);
                return;
            }

            var smallest = _items[^1];
            if (node.Size < smallest.Size) return;
            if (node.Size == smallest.Size && _tie) return;
            _items.RemoveAt(_items.Count - 1);
            int position = _items.BinarySearch(node, DescendingComparer.Instance);
            if (position < 0) position = ~position;
            _items.Insert(position, node);
        }

        public void Merge(TopFileCollector other)
        {
            foreach (var node in other._items) Add(node);
        }

        public IReadOnlyList<DiskUsageNode> Snapshot() => _items.ToList();

        private sealed class DescendingComparer : IComparer<DiskUsageNode>
        {
            public static DescendingComparer Instance { get; } = new();

            public int Compare(DiskUsageNode? left, DiskUsageNode? right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left == null) return 1;
                if (right == null) return -1;
                return right.Size.CompareTo(left.Size);
            }
        }
    }
}
