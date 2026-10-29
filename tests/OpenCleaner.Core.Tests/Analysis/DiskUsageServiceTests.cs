using System.Diagnostics;
using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Analysis;

public sealed class DiskUsageServiceTests : IDisposable
{
    private readonly string _root;

    public DiskUsageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenCleaner-DiskUsage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private sealed class ProgressRecorder : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public void ScanReportsTreeTotalsAndLargestFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        WriteFile("a.txt", 100);
        WriteFile("big.bin", 2048);
        WriteFile(Path.Combine("sub", "inner.txt"), 300);
        WriteFile(Path.Combine("sub", "deep", "d.bin"), 400);

        DiskUsageResult result = Scan(new DiskUsageOptions
        {
            MinFileSizeForListing = 1,
            MaxParallelism = 1,
            MaxLargestFiles = 10,
            MaxFilesPerDirectory = 10
        });

        Assert.Equal(2848, result.TotalSize);
        Assert.Equal(4, result.TotalFiles);
        Assert.Equal(4, result.TotalDirectories);
        Assert.Equal(0, result.SkippedEntries);

        DiskUsageNode root = result.Root;
        Assert.Equal(2, root.Directories.Count);
        Assert.Equal("sub", root.Directories[0].Name);
        Assert.Equal(700, root.Directories[0].Size);
        Assert.Single(root.Directories[0].Directories);
        Assert.Equal("empty", root.Directories[1].Name);
        Assert.Equal(0, root.Directories[1].Size);
        Assert.Equal(new[] { "big.bin", "a.txt" }, root.Files.Select(file => file.Name));
        Assert.Equal(2148, root.DirectFileBytes);

        Assert.Equal(new[] { "big.bin", "d.bin", "inner.txt", "a.txt" },
            result.LargestFiles.Select(file => file.Name));
        Assert.Equal(2048, result.LargestFiles[0].Size);

        ExtensionUsage binary = Assert.Single(result.Extensions, usage => usage.Extension == ".bin");
        Assert.Equal(2448, binary.Size);
        Assert.Equal(2, binary.Count);
        ExtensionUsage text = Assert.Single(result.Extensions, usage => usage.Extension == ".txt");
        Assert.Equal(400, text.Size);
        Assert.Equal(2, text.Count);
    }

    [Fact]
    public void SmallFilesAreAggregatedButNotListedIndividually()
    {
        WriteFile("small.txt", 10);
        WriteFile("big.bin", 1500);

        DiskUsageResult result = Scan(new DiskUsageOptions
        {
            MinFileSizeForListing = 1000,
            MaxParallelism = 1
        });

        Assert.Equal(2, result.Root.FileCount);
        Assert.Equal(1510, result.Root.DirectFileBytes);
        Assert.Equal("big.bin", Assert.Single(result.Root.Files).Name);
        Assert.Equal(1, result.Root.OtherFileCount);
    }

    [Fact]
    public void ListedFilesAndLargestFilesAreBounded()
    {
        for (int index = 0; index < 10; index++) WriteFile($"f{index}.dat", 2048);

        DiskUsageResult result = Scan(new DiskUsageOptions
        {
            MinFileSizeForListing = 1,
            MaxFilesPerDirectory = 4,
            MaxLargestFiles = 5,
            MaxParallelism = 1
        });

        Assert.Equal(4, result.Root.Files.Count);
        Assert.Equal(6, result.Root.OtherFileCount);
        Assert.Equal(5, result.LargestFiles.Count);
        Assert.Equal(10, result.TotalFiles);
    }

    [Fact]
    public void EmptyDirectoryProducesAnEmptyResult()
    {
        DiskUsageResult result = Scan(new DiskUsageOptions { MaxParallelism = 1 });

        Assert.Equal(0, result.TotalSize);
        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(1, result.TotalDirectories);
        Assert.Empty(result.LargestFiles);
        Assert.Empty(result.Extensions);
        Assert.Equal(Path.GetFullPath(_root), result.Root.FullPath);
    }

    [Fact]
    public void CancellationIsObservedBeforeAnyWork()
    {
        WriteFile("a.txt", 10);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new DiskUsageService().Scan(_root, new DiskUsageOptions(), ct: source.Token));
    }

    [Fact]
    public void MissingDirectoryIsRejected()
    {
        string missing = Path.Combine(_root, "absent");
        Assert.Throws<DirectoryNotFoundException>(() => new DiskUsageService().Scan(missing));
    }

    [Fact]
    public void ProtectedPathsAreMarkedWithoutBlockingTheRead()
    {
        Directory.CreateDirectory(Path.Combine(_root, "prive"));
        WriteFile(Path.Combine("prive", "gros.bin"), 512);

        DiskUsageResult result = Scan(new DiskUsageOptions
        {
            MinFileSizeForListing = 1,
            MaxParallelism = 1,
            IsProtected = path => path.EndsWith("prive", StringComparison.OrdinalIgnoreCase)
        });

        Assert.False(result.Root.IsProtected);
        Assert.True(Assert.Single(result.Root.Directories).IsProtected);
        Assert.Equal(512, result.TotalSize);
    }

    [Fact]
    public void JunctionContentIsNeverCounted()
    {
        if (!OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(Path.GetTempPath(), "OpenCleaner-Outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string junction = Path.Combine(_root, "lien");
        try
        {
            File.WriteAllBytes(Path.Combine(outside, "gros.bin"), new byte[5000]);
            CreateJunction(junction, outside);

            DiskUsageResult result = Scan(new DiskUsageOptions { MaxParallelism = 1 });

            Assert.Equal(0, result.TotalSize);
            Assert.Empty(result.Root.Directories);
            Assert.True(result.SkippedEntries >= 1);
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ProgressEndsAtOne()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        WriteFile(Path.Combine("a", "x.bin"), 10);
        WriteFile(Path.Combine("b", "y.bin"), 20);
        var progress = new ProgressRecorder();

        DiskUsageResult result = new DiskUsageService().Scan(_root,
            new DiskUsageOptions { MaxParallelism = 1 }, progress);

        Assert.Equal(30, result.TotalSize);
        Assert.NotEmpty(progress.Values);
        Assert.Equal(1, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void ParallelAndSerialScansAgree()
    {
        for (int directory = 0; directory < 4; directory++)
        {
            string path = Path.Combine(_root, "d" + directory);
            Directory.CreateDirectory(path);
            for (int file = 0; file < 5; file++) WriteFile(Path.Combine(path, $"f{file}.bin"), 100 + file);
        }

        var service = new DiskUsageService();
        DiskUsageResult serial = service.Scan(_root,
            new DiskUsageOptions { MinFileSizeForListing = 1, MaxParallelism = 1 });
        DiskUsageResult parallel = service.Scan(_root,
            new DiskUsageOptions { MinFileSizeForListing = 1, MaxParallelism = 4 });

        Assert.Equal(serial.TotalSize, parallel.TotalSize);
        Assert.Equal(serial.TotalFiles, parallel.TotalFiles);
        Assert.Equal(serial.TotalDirectories, parallel.TotalDirectories);
    }

    private DiskUsageResult Scan(DiskUsageOptions options) =>
        new DiskUsageService().Scan(_root, options);

    private void WriteFile(string relativePath, int size)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
