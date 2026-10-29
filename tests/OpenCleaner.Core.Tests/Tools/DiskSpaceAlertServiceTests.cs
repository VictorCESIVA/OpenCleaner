using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class DiskSpaceAlertServiceTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private static SystemDiskInfo Disk(string name, long total, long free) => new(name, "NTFS", total, free);

    [Fact]
    public void HealthyDisksDoNotRaiseAnAlert()
    {
        Assert.Null(DiskSpaceAlertService.FindLowSpace(
            [Disk("C:\\", 500 * Gigabyte, 100 * Gigabyte)]));
        Assert.Null(DiskSpaceAlertService.FindLowSpace([]));
    }

    [Fact]
    public void ExactlyTenPercentIsNotAnAlert()
    {
        Assert.Null(DiskSpaceAlertService.FindLowSpace(
            [Disk("C:\\", 500 * Gigabyte, 50 * Gigabyte)]));
    }

    [Fact]
    public void LowDiskIsReportedWithItsMeasurements()
    {
        DiskSpaceAlert alert = Assert.IsType<DiskSpaceAlert>(DiskSpaceAlertService.FindLowSpace(
            [Disk("C:\\", 500 * Gigabyte, 42 * Gigabyte)]));

        Assert.Equal("C:\\", alert.Drive);
        Assert.Equal(42 * Gigabyte, alert.FreeBytes);
        Assert.Equal(500 * Gigabyte, alert.TotalBytes);
        Assert.InRange(alert.FreePercent, 8.3, 8.5);
    }

    [Fact]
    public void WorstDiskIsSelected()
    {
        DiskSpaceAlert alert = Assert.IsType<DiskSpaceAlert>(DiskSpaceAlertService.FindLowSpace(
        [
            Disk("C:\\", 500 * Gigabyte, 45 * Gigabyte),
            Disk("D:\\", 1000 * Gigabyte, 40 * Gigabyte),
            Disk("E:\\", 100 * Gigabyte, 50 * Gigabyte)
        ]));

        Assert.Equal("D:\\", alert.Drive);
    }

    [Fact]
    public void UnknownAndNegativeValuesAreHandled()
    {
        Assert.Null(DiskSpaceAlertService.FindLowSpace([Disk("Z:\\", 0, 0)]));

        DiskSpaceAlert alert = Assert.IsType<DiskSpaceAlert>(DiskSpaceAlertService.FindLowSpace(
            [Disk("C:\\", 100 * Gigabyte, -5)]));
        Assert.Equal(0, alert.FreeBytes);
        Assert.Equal(0, alert.FreeRatio);
    }

    [Fact]
    public void NullCollectionIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => DiskSpaceAlertService.FindLowSpace(null!));

    [Fact]
    public void CustomThresholdChangesTheAlert()
    {
        var disks = new[] { Disk("C:\\", 500 * Gigabyte, 60 * Gigabyte) }; // 12 %

        Assert.Null(DiskSpaceAlertService.FindLowSpace(disks, threshold: 0.10));
        Assert.NotNull(DiskSpaceAlertService.FindLowSpace(disks, threshold: 0.15));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void InvalidCustomThresholdIsRejected(double threshold) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiskSpaceAlertService.FindLowSpace([Disk("C:\\", 100 * Gigabyte, 5 * Gigabyte)], threshold));
}
