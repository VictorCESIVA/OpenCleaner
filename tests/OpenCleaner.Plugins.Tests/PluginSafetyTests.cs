using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System;
using OpenCleaner.Plugins.System.Plugins;
using Xunit;

namespace OpenCleaner.Plugins.Tests;

public sealed class PluginSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleanerPluginTests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    private readonly Mock<IFileGuardian> _guardian = new();
    private readonly IBackupManager _backup = Mock.Of<IBackupManager>();

    public PluginSafetyTests()
    {
        Directory.CreateDirectory(_root);
        _guardian.Setup(g => g.SafeDeleteAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, "fixture", Guid.NewGuid(), ItemsProcessed: 1, BytesRecovered: 0));
    }

    private string Write(string name, string content = "cache")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private FixtureCleaner Cleaner() => new(_root, _guardian.Object);

    [Fact]
    public async Task ForgedPathAndForeignIdsNeverReachGuardian()
    {
        Write("cache.txt");
        var cleaner = Cleaner();
        var scanned = Assert.Single(await cleaner.AnalyzeAsync());
        var altered = scanned with { Path = Write("personal.txt", "important") };
        var foreign = scanned with { Id = Guid.NewGuid().ToString() };
        var result = await cleaner.CleanAsync([altered, foreign], _backup);
        Assert.False(result.Success);
        Assert.Equal(0, result.ItemsProcessed);
        _guardian.Verify(g => g.SafeDeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangedFileAndPreviousScanAreRejected()
    {
        var path = Write("cache.txt");
        var cleaner = Cleaner();
        var old = Assert.Single(await cleaner.AnalyzeAsync());
        File.AppendAllText(path, "new content");
        Assert.False((await cleaner.CleanAsync([old], _backup)).Success);
        await cleaner.AnalyzeAsync();
        Assert.False((await cleaner.CleanAsync([old], _backup)).Success);
        _guardian.Verify(g => g.SafeDeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BackupFailureNeverFallsBackToUnprotectedDelete()
    {
        Write("cache.txt");
        var cleaner = Cleaner();
        var item = Assert.Single(await cleaner.AnalyzeAsync());
        _guardian.Setup(g => g.SafeDeleteAsync(item.Path, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, "Backup failed", Guid.NewGuid()));
        var result = await cleaner.CleanAsync([item], _backup);
        Assert.False(result.Success);
        _guardian.Verify(g => g.SafeDeleteAsync(item.Path, true, It.IsAny<CancellationToken>()), Times.Once);
        _guardian.Verify(g => g.SafeDeleteAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GuardianCancellationPropagates()
    {
        Write("cache.txt");
        var cleaner = Cleaner();
        var item = Assert.Single(await cleaner.AnalyzeAsync());
        _guardian.Setup(g => g.SafeDeleteAsync(item.Path, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => cleaner.CleanAsync([item], _backup));
    }

    [Fact]
    public async Task DuplicateInputIsProcessedOnceAndUsesActualReclaimedBytes()
    {
        Write("cache.txt");
        var cleaner = Cleaner();
        var item = Assert.Single(await cleaner.AnalyzeAsync());
        var result = await cleaner.CleanAsync([item, item], _backup);
        Assert.True(result.Success);
        Assert.Equal(1, result.ItemsProcessed);
        Assert.Equal(0, result.BytesRecovered);
        _guardian.Verify(g => g.SafeDeleteAsync(item.Path, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecentlyWrittenFileWithStaleAccessIsExcluded()
    {
        var path = Write("cache.txt");
        File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddDays(-40));
        var cleaner = new FixtureCleaner(_root, _guardian.Object, TimeSpan.FromDays(7));
        Assert.Empty(await cleaner.AnalyzeAsync());
    }

    [Fact]
    public async Task JunctionTraversalAndReplacedRootAreBlocked()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = Path.Combine(_root, "source");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "local.txt"), "local");
        File.WriteAllText(Path.Combine(target, "valuable.txt"), "valuable");
        var link = Path.Combine(source, "linked");
        CreateJunction(link, target);
        Assert.Equal([Path.Combine(source, "local.txt")], PluginFileSafety.EnumerateFiles(source, "*", true, default).ToArray());
        Assert.False(PluginFileSafety.IsRegularPath(Path.Combine(link, "valuable.txt")));
        var cleaner = new FixtureCleaner(source, _guardian.Object);
        var item = Assert.Single(await cleaner.AnalyzeAsync());
        File.Delete(item.Path);
        Directory.Delete(link, recursive: false);
        Directory.Delete(source, recursive: false);
        File.WriteAllText(Path.Combine(target, "local.txt"), "local");
        CreateJunction(source, target);
        Assert.False((await cleaner.CleanAsync([item], _backup)).Success);
        _guardian.Verify(g => g.SafeDeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(link);
    }

    [Fact]
    public async Task PrivacyAlertsAreExpertOnlyIncludingDotEnv()
    {
        Write(".env", "api_key=fixture-value");
        Write("passwords.txt", "fixture-value");
        var scanner = new PrivacyScannerPlugin(_guardian.Object, NullLogger<PrivacyScannerPlugin>.Instance, [_root]);
        var items = await scanner.AnalyzeAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(RiskLevel.ExpertOnly, item.RiskLevel));
        Assert.DoesNotContain(items, item => item.Description.Contains("fixture-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateFinderPreservesKeeperAndRevalidatesItsContents()
    {
        var content = new string('a', 1024 * 1024);
        var keeper = Write("a.txt", content);
        Write("b.txt", content);
        var scanner = new DuplicateFinderPlugin(_guardian.Object, NullLogger<DuplicateFinderPlugin>.Instance, [_root]);
        var item = Assert.Single(await scanner.AnalyzeAsync());
        Assert.Equal(keeper, item.OriginalPath);
        Assert.NotNull(item.DuplicateGroupId);
        Assert.Equal(RiskLevel.ExpertOnly, item.RiskLevel);
        File.WriteAllText(keeper, new string('b', 1024 * 1024));
        Assert.False((await scanner.CleanAsync([item], _backup)).Success);
        _guardian.Verify(g => g.SafeDeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateKeeperCannotBeDeletedDuringCleanup()
    {
        var content = new string('a', 1024 * 1024);
        var keeper = Write("a.txt", content);
        Write("b.txt", content);
        var scanner = new DuplicateFinderPlugin(_guardian.Object, NullLogger<DuplicateFinderPlugin>.Instance, [_root]);
        var item = Assert.Single(await scanner.AnalyzeAsync());
        _guardian.Setup(g => g.SafeDeleteAsync(item.Path, true, It.IsAny<CancellationToken>()))
            .Callback(() => { if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.Delete(keeper)); })
            .ReturnsAsync(new OperationResult(true, "fixture", Guid.NewGuid(), ItemsProcessed: 1, BytesRecovered: 0));
        Assert.True((await scanner.CleanAsync([item], _backup)).Success);
        Assert.True(File.Exists(keeper));
    }

    [Fact]
    public async Task CanceledDuplicateAndDiskScansPropagateCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var duplicate = new DuplicateFinderPlugin(_guardian.Object, NullLogger<DuplicateFinderPlugin>.Instance, [_root]);
        var disk = new DiskAnalyzerPlugin(_guardian.Object, NullLogger<DiskAnalyzerPlugin>.Instance) { ScanRoot = _root };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => duplicate.AnalyzeAsync(ct: source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disk.AnalyzeAsync(ct: source.Token));
    }

    [Fact]
    public async Task DiskCountsDirectChildrenWithoutCountingNestedFoldersTwice()
    {
        Write("direct.txt", "123");
        Write("nested/deeper/file.txt", "12345");
        var disk = new DiskAnalyzerPlugin(_guardian.Object, NullLogger<DiskAnalyzerPlugin>.Instance) { ScanRoot = _root };
        var items = await disk.AnalyzeAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(8, items.Sum(i => i.Size));
        Assert.False((await disk.CleanAsync(items, _backup)).Success);
    }

    [Fact]
    public async Task WindowsUpdateNeverProposesDeletingServicingFiles()
    {
        var plugin = new WindowsUpdatePlugin(_guardian.Object, NullLogger<WindowsUpdatePlugin>.Instance);
        Assert.False(plugin.IsAvailable);
        Assert.Empty(await plugin.AnalyzeAsync());
    }

    [Fact]
    public async Task DuplicateFinderSkipsFullHashWhenPartialHashesDiffer()
    {
        Write("diff1.bin", new string('x', 1024 * 1024));
        Write("diff2.bin", new string('y', 1024 * 1024));
        var hashedFiles = new List<string>();
        var scanner = new DuplicateFinderPlugin(_guardian.Object, NullLogger<DuplicateFinderPlugin>.Instance, [_root]);
        scanner.OnFileHashing += path => hashedFiles.Add(path);
        var items = await scanner.AnalyzeAsync();
        Assert.Empty(items);
        Assert.Empty(hashedFiles);
    }

    public void Dispose()
    {
        foreach (var junction in _junctions.Where(Directory.Exists)) Directory.Delete(junction, recursive: false);
        // Only this uniquely named temporary fixture is removed; no production/user directory is touched.
        var full = Path.GetFullPath(_root);
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(full).StartsWith("OpenCleanerPluginTests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test fixture path");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    private sealed class FixtureCleaner(string root, IFileGuardian guardian, TimeSpan? age = null)
        : SafeFileCleanerPlugin(guardian, NullLogger.Instance)
    {
        public override string Id => "fixture";
        public override string Name => "fixture";
        public override string Description => "fixture";
        public override PluginCategory Category => PluginCategory.System;
        public override RiskLevel MaxRiskLevel => RiskLevel.Safe;
        protected override IEnumerable<ScanTarget> GetTargets() => [new(root, "fixture", MinAge: age)];
    }
}
