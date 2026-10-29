using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Security;
using System.Runtime.InteropServices;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class ToolsTests
{
    [Fact]
    public void SystemInfoReportsWindowsAndDisks()
    {
        SystemHealthInfo info = SystemInfoService.Get();
        Assert.False(string.IsNullOrWhiteSpace(info.OsName));
        Assert.False(string.IsNullOrWhiteSpace(info.CpuName));
        Assert.True(info.LogicalProcessors > 0);
        Assert.All(info.Disks, disk => Assert.False(string.IsNullOrWhiteSpace(disk.Name)));
    }

    [Fact]
    public void RecycleBinQueryReturnsNonNegativeValues()
    {
        RecycleBinInfo info = RecycleBinService.Query();
        Assert.True(info.ItemCount >= 0);
        Assert.True(info.SizeBytes >= 0);
    }

    [Fact]
    public void InstalledProgramsHaveNames()
    {
        IReadOnlyList<InstalledProgram> programs = InstalledProgramService.GetInstalledPrograms();
        Assert.All(programs, program => Assert.False(string.IsNullOrWhiteSpace(program.Name)));
    }

    [Fact]
    public void StartupItemsHaveUniqueIdentifiers()
    {
        IReadOnlyList<StartupItem> items = StartupItemService.GetItems();
        Assert.Equal(items.Count, items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
    }

    [Fact]
    public void WingetAvailabilityProbeDoesNotThrow() => _ = WingetService.IsAvailable();

    [Fact]
    public async Task SecureDeleteRemovesFile()
    {
        string file = Path.Combine(Path.GetTempPath(), "OpenCleaner-Shred-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(file, "effacement securise");
        try
        {
            int deleted = await SecureDeleteService.ShredAsync([file], passes: 2);
            Assert.Equal(1, deleted);
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task SecureDeleteRejectsMultipleHardLinksWithoutModifyingEitherName()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Shred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "original.txt");
        string link = Path.Combine(directory, "link.txt");
        try
        {
            await File.WriteAllTextAsync(file, "contenu original");
            Assert.True(CreateHardLink(link, file, IntPtr.Zero), $"CreateHardLink failed: {Marshal.GetLastWin32Error()}");
            await Assert.ThrowsAsync<IOException>(() => SecureDeleteService.ShredAsync([file], passes: 1));
            Assert.Equal("contenu original", await File.ReadAllTextAsync(file));
            Assert.Equal("contenu original", await File.ReadAllTextAsync(link));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SecureDeleteDoesNotModifyLockedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(Path.GetTempPath(), "OpenCleaner-Shred-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(file, "contenu original");
        try
        {
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
                await Assert.ThrowsAsync<IOException>(() => SecureDeleteService.ShredAsync([file], passes: 1));
            Assert.Equal("contenu original", await File.ReadAllTextAsync(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task OverwriteHandlePreventsPathReplacementUntilDeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Shred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "original.txt");
        string moved = Path.Combine(directory, "moved.txt");
        try
        {
            await File.WriteAllTextAsync(file, "original");
            await using (var handle = new FileDeletionHandle(file, forOverwrite: true))
            {
                Assert.Throws<IOException>(() => File.Move(file, moved));
                Assert.True(CreateHardLink(moved, file, IntPtr.Zero));
                Assert.Throws<IOException>(() => handle.EnsureSingleLink());
            }
            Assert.Equal("original", await File.ReadAllTextAsync(file));
            Assert.Equal("original", await File.ReadAllTextAsync(moved));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void HardLinkInspectorCountsPhysicalNames()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "original.txt");
        string link = Path.Combine(directory, "alias.txt");
        try
        {
            File.WriteAllText(file, "contenu");
            Assert.True(CreateHardLink(link, file, IntPtr.Zero),
                $"CreateHardLink failed: {Marshal.GetLastWin32Error()}");

            Assert.Equal(2, HardLinkInspector.CountHardLinks(file));
            Assert.Equal(2, HardLinkInspector.CountHardLinks(link));
            Assert.False(HardLinkInspector.HasSingleLink(file));

            File.Delete(link);
            Assert.Equal(1, HardLinkInspector.CountHardLinks(file));
            Assert.True(HardLinkInspector.HasSingleLink(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HardLinkInspectorReturnsUnknownForMissingFile() =>
        Assert.Equal(-1, HardLinkInspector.CountHardLinks(
            Path.Combine(Path.GetTempPath(), "OpenCleaner-Absent-" + Guid.NewGuid().ToString("N"))));

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
}
