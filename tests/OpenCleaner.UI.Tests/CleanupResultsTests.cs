using OpenCleaner.Contracts;
using OpenCleaner.UI;
using Xunit;

namespace OpenCleaner.UI.Tests;

public sealed class CleanupResultsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleaner-results-" + Guid.NewGuid());

    public CleanupResultsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PartialCleanupKeepsSkippedAndUnselectedFilesWithTheirSizes()
    {
        var deleted = CreateFile("deleted.tmp", 16);
        var locked = CreateFile("locked.tmp", 32);
        var unselected = CreateFile("unselected.tmp", 64);
        using var handle = new FileStream(locked.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        File.Delete(deleted.Path);

        var remaining = CleanupResults.GetRemainingItems([deleted, locked, unselected]);

        Assert.Equal([locked, unselected], remaining);
        Assert.Equal(96, remaining.Sum(item => item.Size));
    }

    [Fact]
    public void CompleteCleanupLeavesZeroItemsAndBytesEvenIfParentWasDeleted()
    {
        var item = CreateFile("cache.tmp", 1024);
        File.Delete(item.Path);
        Directory.Delete(_root);

        var remaining = CleanupResults.GetRemainingItems([item]);

        Assert.Empty(remaining);
        Assert.Equal(0, remaining.Sum(entry => entry.Size));
    }

    [Fact]
    public void FilesRestoredByRollbackRemainVisible()
    {
        var item = CreateFile("restored.tmp", 128);
        File.Delete(item.Path);
        File.WriteAllBytes(item.Path, new byte[128]);

        Assert.Equal([item], CleanupResults.GetRemainingItems([item]));
    }

    [Fact]
    public void FileChecksDoNotDiscardRegistryResultsOrInaccessiblePaths()
    {
        var registry = new CleanableItem("registry", @"HKEY_CURRENT_USER\Software\Example", 0,
            "Registry", RiskLevel.ExpertOnly, ItemType.RegistryKey);
        var invalid = registry with { Id = "invalid", Path = "invalid\0path", Type = ItemType.File };

        Assert.Equal([registry, invalid], CleanupResults.GetRemainingItems([registry, invalid]));
    }

    private CleanableItem CreateFile(string name, int size)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[size]);
        return new CleanableItem(name, path, size, "Cache", RiskLevel.Safe, ItemType.File);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
