using OpenCleaner.Core.Tools;
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
}
