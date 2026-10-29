using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Analysis;

public sealed class InstalledSpaceMapperTests
{
    private static DiskUsageNode BuildRoot()
    {
        var root = new DiskUsageNode("C:\\", "C:\\", isFile: false, size: 10_000);
        var programFiles = new DiskUsageNode("Program Files", @"C:\Program Files", isFile: false, size: 5_000);
        programFiles.Directories.Add(new DiskUsageNode("Contoso", @"C:\Program Files\Contoso",
            isFile: false, size: 1_500));
        programFiles.Directories.Add(new DiskUsageNode("Games", @"C:\Program Files\Games",
            isFile: false, size: 3_500));
        root.Directories.Add(programFiles);
        root.Directories.Add(new DiskUsageNode("Users", @"C:\Users", isFile: false, size: 5_000));
        return root;
    }

    private static InstalledProgram Program(string name, string? location, long? estimated = null) =>
        new(name, "1.0", "Éditeur", location, null, estimated, MachineWide: true, Is64Bit: true);

    [Fact]
    public void MeasuredFolderTakesPrecedenceOverRegistryEstimate()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
            [Program("Contoso Suite", @"C:\Program Files\Contoso", estimated: 9_999_999)]);

        InstalledSpaceEntry entry = Assert.Single(entries);
        Assert.Equal(1_500, entry.Size);
        Assert.True(entry.Measured);
        Assert.False(entry.Estimated);
        Assert.Equal("Contoso Suite", entry.Name);
    }

    [Fact]
    public void UnmeasuredFolderFallsBackToRegistryEstimate()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
            [Program("Absent", @"C:\Program Files\Absent", estimated: 900)]);

        InstalledSpaceEntry entry = Assert.Single(entries);
        Assert.Equal(900, entry.Size);
        Assert.False(entry.Measured);
        Assert.True(entry.Estimated);
    }

    [Fact]
    public void ProgramsOutsideRootOrWithoutLocationAreIgnored()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
        [
            Program("Hors périmètre", @"D:\Jeux\Hors", estimated: 5_000),
            Program("Sans emplacement", null, estimated: 5_000),
            Program("Emplacement vide", "  ", estimated: 5_000),
            Program("Contoso", @"C:\Program Files\Contoso")
        ]);

        Assert.Equal("Contoso", Assert.Single(entries).Name);
    }

    [Fact]
    public void MatchingIgnoresCaseAndTrailingSeparator()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
            [Program("Contoso", @"c:\program files\contoso\", estimated: 1)]);

        Assert.Equal(1_500, Assert.Single(entries).Size);
    }

    [Fact]
    public void EntriesAreSortedBySizeAndLimited()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
        [
            Program("Petit", @"C:\Program Files\Contoso"),
            Program("Grand", @"C:\Program Files\Games"),
            Program("Moyen", @"C:\Users", estimated: 4_000)
        ], maxEntries: 2);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Moyen", entries[0].Name);
        Assert.Equal(@"C:\Users", entries[0].Location);
        Assert.Equal(5_000, entries[0].Size);
        Assert.True(entries[0].Measured);
        Assert.Equal("Grand", entries[1].Name);
        Assert.Equal(3_500, entries[1].Size);
    }

    [Fact]
    public void ProgramInstalledAtTheScanRootIsNotAttributedTheWholeTree()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
            [Program("Installé à la racine", @"C:\", estimated: 10_000)]);

        Assert.Empty(entries);
    }

    [Fact]
    public void InvalidOrEmptyEntriesAreSkippedWithoutThrowing()
    {
        var entries = InstalledSpaceMapper.Map(BuildRoot(),
        [
            Program("", @"C:\Program Files\Contoso"),
            Program("Chemin invalide", "C:\\bad\0path"),
            Program("Relatif", @"Program Files\Contoso"),
            Program("Contoso", @"C:\Program Files\Contoso")
        ]);

        Assert.Equal("Contoso", Assert.Single(entries).Name);
        Assert.Empty(InstalledSpaceMapper.Map(BuildRoot(), [], maxEntries: 0));
    }
}
