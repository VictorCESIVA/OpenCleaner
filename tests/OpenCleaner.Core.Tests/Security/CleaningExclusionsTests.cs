using System.Text.Json;
using OpenCleaner.Core.Security;
using Xunit;

namespace OpenCleaner.Core.Tests.Security;

public sealed class CleaningExclusionsTests : IDisposable
{
    private readonly string _root;
    private readonly string _store;

    public CleaningExclusionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenCleaner-Exclusions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = Path.Combine(_root, "exclusions.json");
    }

    [Fact]
    public void ExactFolderAndDescendantsAreExcluded()
    {
        string folder = Path.Combine(_root, "Projets");
        Directory.CreateDirectory(folder);
        using var _ = CleaningExclusions.OverrideForTests([folder]);

        Assert.True(CleaningExclusions.IsExcluded(folder));
        Assert.True(CleaningExclusions.IsExcluded(Path.Combine(folder, "fichier.txt")));
        Assert.True(CleaningExclusions.IsExcluded(Path.Combine(folder, "sous", "dossier", "a.bin")));
        Assert.False(CleaningExclusions.IsExcluded(Path.Combine(_root, "Autre", "fichier.txt")));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndBoundarySafe()
    {
        string folder = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(folder);
        using var _ = CleaningExclusions.OverrideForTests([folder.ToUpperInvariant() + Path.DirectorySeparatorChar]);

        Assert.True(CleaningExclusions.IsExcluded(Path.Combine(folder, "a.txt")));
        Assert.False(CleaningExclusions.IsExcluded(folder + "Backup" + Path.DirectorySeparatorChar + "a.txt"));
    }

    [Fact]
    public void RootedExistingAndMissingPathsBehaveConsistently()
    {
        string folder = Path.Combine(_root, "Absent");
        using var _ = CleaningExclusions.OverrideForTests([folder]);

        Assert.True(CleaningExclusions.IsExcluded(Path.Combine(folder, "futur.txt")));
        Assert.False(CleaningExclusions.IsExcluded("relative\\path.txt"));
        Assert.False(CleaningExclusions.IsExcluded(""));
    }

    [Fact]
    public void OverrideIsRestoredAfterDispose()
    {
        string folder = Path.Combine(_root, "Isole");
        using (CleaningExclusions.OverrideForTests([folder]))
            Assert.True(CleaningExclusions.IsExcluded(Path.Combine(folder, "a.txt")));

        Assert.False(CleaningExclusions.IsExcluded(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public void LoadNormalizesDeduplicatesAndDropsInvalidEntries()
    {
        string folder = Path.Combine(_root, "Charge");
        Directory.CreateDirectory(folder);
        File.WriteAllText(_store, JsonSerializer.Serialize(new[]
        {
            folder, folder.ToUpperInvariant() + Path.DirectorySeparatorChar,
            "relative", "", Path.Combine(folder, "..", "Autre")
        }));

        CleaningExclusions.Load(_store);

        Assert.Contains(folder, CleaningExclusions.Paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("relative", CleaningExclusions.Paths);
        Assert.True(CleaningExclusions.Paths.Count <= 3);
    }

    [Fact]
    public void MissingOrCorruptStoreClearsTheList()
    {
        CleaningExclusions.Load(Path.Combine(_root, "absent.json"));
        Assert.Empty(CleaningExclusions.Paths);

        File.WriteAllText(_store, "{ pas du json");
        CleaningExclusions.Load(_store);
        Assert.Empty(CleaningExclusions.Paths);
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
