using System.Text.Json;
using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class SpaceTrendServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _history;
    private readonly DateTime _anchor = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    public SpaceTrendServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Trend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _history = Path.Combine(_directory, "space-history.json");
    }

    private SpaceTrendService Service() => new(_history);

    private static SystemDiskInfo Disk(string name, long free) => new(name, "NTFS", 500L * 1024 * 1024 * 1024, free);

    [Fact]
    public void MissingHistoryStartsWithASinglePointAndCreatesTheFile()
    {
        SpaceTrendResult result = Service().RecordAndGet([Disk("C:\\", 10_000)], _anchor, "C:\\");

        Assert.Single(result.Points);
        Assert.Equal(10_000, result.Points[0].FreeBytes);
        Assert.Equal(10_000, result.LatestFreeBytes);
        Assert.Null(result.DeltaBytes);
        Assert.True(File.Exists(_history));
    }

    [Fact]
    public void SameDayRecordReplacesThePreviousValue()
    {
        var service = Service();
        service.RecordAndGet([Disk("C:\\", 10_000)], _anchor, "C:");
        SpaceTrendResult result = service.RecordAndGet([Disk("C:\\", 8_000)], _anchor.AddHours(3), "C:");

        Assert.Single(result.Points);
        Assert.Equal(8_000, result.Points[0].FreeBytes);
    }

    [Fact]
    public void MultipleDaysAreSortedAndProduceTheDelta()
    {
        var service = Service();
        service.RecordAndGet([Disk("C:\\", 1_000)], _anchor.AddDays(-8), "C:");
        service.RecordAndGet([Disk("C:\\", 800)], _anchor.AddDays(-4), "C:");
        SpaceTrendResult result = service.RecordAndGet([Disk("C:\\", 900)], _anchor, "C:");

        Assert.Equal(3, result.Points.Count);
        Assert.Equal(new[] { 1_000L, 800L, 900L }, result.Points.Select(point => point.FreeBytes));
        Assert.Equal(900, result.LatestFreeBytes);
        Assert.Equal(-100, result.DeltaBytes);
        Assert.True(result.HasEnoughData);
    }

    [Fact]
    public void PeriodExcludesOlderPoints()
    {
        var service = Service();
        service.RecordAndGet([Disk("C:\\", 5_000)], _anchor.AddDays(-40), "C:");
        SpaceTrendResult result = service.RecordAndGet([Disk("C:\\", 4_000)], _anchor, "C:", days: 30);

        Assert.Single(result.Points);
        Assert.Equal(4_000, result.Points[0].FreeBytes);
        Assert.Null(result.DeltaBytes);
    }

    [Fact]
    public void CorruptHistoryIsIgnoredAndReplaced()
    {
        File.WriteAllText(_history, "{ ceci n'est pas du json");

        SpaceTrendResult result = Service().RecordAndGet([Disk("C:\\", 2_000)], _anchor, "C:");

        Assert.Single(result.Points);
        Assert.Equal(2_000, result.Points[0].FreeBytes);
        using var document = JsonDocument.Parse(File.ReadAllText(_history));
        Assert.True(document.RootElement.TryGetProperty("C:", out _));
    }

    [Fact]
    public void DriveNamesAreNormalized()
    {
        var service = Service();
        service.RecordAndGet([Disk("c:\\", 1_000)], _anchor.AddDays(-1), "C:");
        SpaceTrendResult result = service.RecordAndGet([Disk("C:", 900)], _anchor, "c:\\");

        Assert.Equal("C:", result.Drive);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal(-100, result.DeltaBytes);
    }

    [Fact]
    public void OldEntriesArePrunedFromTheFile()
    {
        var service = Service();
        string oldKey = _anchor.AddDays(-200).ToString("yyyy-MM-dd");
        service.RecordAndGet([Disk("C:\\", 1_000)], _anchor.AddDays(-200), "C:");
        service.RecordAndGet([Disk("C:\\", 900)], _anchor, "C:");

        string json = File.ReadAllText(_history);
        Assert.DoesNotContain(oldKey, json);
        Assert.Contains(_anchor.ToString("yyyy-MM-dd"), json);
    }

    [Fact]
    public void PrimaryDriveSelectsItsOwnSeries()
    {
        var service = Service();
        service.RecordAndGet([Disk("C:\\", 10_000)], _anchor, "C:");
        SpaceTrendResult result = service.RecordAndGet([Disk("D:\\", 15_000)], _anchor.AddDays(1), "D:");

        Assert.Equal("D:", result.Drive);
        Assert.Equal(15_000, result.LatestFreeBytes);
        Assert.Single(result.Points);
    }

    [Fact]
    public void UnknownDriveHasNoData()
    {
        SpaceTrendResult result = Service().RecordAndGet([Disk("C:\\", 10_000)], _anchor, "Z:");

        Assert.Empty(result.Points);
        Assert.Null(result.LatestFreeBytes);
        Assert.False(result.HasEnoughData);
    }

    [Fact]
    public void DatesAreBucketedByTheLocalCalendarDay()
    {
        DateTime lateUtc = new(2026, 10, 29, 23, 30, 0, DateTimeKind.Utc);

        SpaceTrendResult result = Service().RecordAndGet([Disk("C:\\", 10_000)], lateUtc, "C:");

        Assert.Equal(lateUtc.ToLocalTime().Date, Assert.Single(result.Points).Date);
    }

    [Fact]
    public void InvalidPeriodIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Service().RecordAndGet([Disk("C:\\", 1)], _anchor, "C:", days: 0));

    [Fact]
    public void EmptyDiskListKeepsTheHistoryReadable()
    {
        SpaceTrendResult result = Service().RecordAndGet([], _anchor, "C:");

        Assert.Empty(result.Points);
        Assert.True(File.Exists(_history));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
