using System.Text.Json;
using OpenCleaner.Core.Health;
using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Transactions;
using Xunit;

namespace OpenCleaner.Core.Tests.Health;

public sealed class HealthExportServiceTests
{
    private const long GigaByte = 1024L * 1024 * 1024;

    private static HealthMetrics Metrics() => new()
    {
        Disks = [new HealthDisk("C:\\", 500 * GigaByte, 42 * GigaByte)],
        TotalMemoryBytes = 16UL * (ulong)GigaByte,
        AvailableMemoryBytes = 2UL * (ulong)GigaByte,
        Uptime = TimeSpan.FromHours(34.5),
        RecycleBinBytes = 3 * GigaByte,
        RecycleBinItems = 18,
        EnabledStartupItems = 27,
        DisabledStartupItems = 2,
        TemporaryBytes = 6 * GigaByte,
        TemporaryItems = 1_400,
        BrowserCacheBytes = 2 * GigaByte,
        BrowserCacheItems = 900,
        RebootPending = true
    };

    private static HealthReport Report() => HealthScoreCalculator.Evaluate(Metrics());

    private static readonly DateTime ExportedAt = new(2026, 10, 29, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void ExportContainsScoreSectionsFindingsAndMetrics()
    {
        HealthExportDocument document = HealthExportService.Create(Report(), Metrics(),
            new CleanupHistorySummary(8, 12_400, 34 * GigaByte, ExportedAt.AddDays(-1), []),
            new SpaceTrendResult("C:", 42 * GigaByte, -10 * GigaByte,
                [new SpaceTrendPoint(ExportedAt.Date.AddDays(-2), 52 * GigaByte),
                 new SpaceTrendPoint(ExportedAt.Date, 42 * GigaByte)]),
            ExportedAt);

        Assert.Equal(HealthExportService.CurrentFormatVersion, document.FormatVersion);
        Assert.Equal(ExportedAt, document.ExportedAtUtc);
        Assert.Equal("OpenCleaner", document.Application);
        Assert.Equal(Report().Score, document.Score);
        Assert.Equal(5, document.Sections.Count);
        Assert.NotEmpty(document.Findings);
        Assert.Equal(34.5, document.Metrics.UptimeHours);
        Assert.True(document.Metrics.RebootPending);
        Assert.Equal(8, document.History?.Cleanups);
        Assert.Equal(-10 * GigaByte, document.Trend?.DeltaBytes);
        Assert.Equal(2, document.Trend?.Points.Count);
    }

    [Fact]
    public void JsonIsValidAndContainsNoUserPath()
    {
        string json = HealthExportService.ToJson(HealthExportService.Create(Report(), Metrics(),
            exportedAtUtc: ExportedAt));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal("C:\\", Assert.Single(document.RootElement
            .GetProperty("Metrics").GetProperty("Disks").EnumerateArray()).GetProperty("Name").GetString());
        Assert.DoesNotContain(@"C:\Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Documents", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptionalHistoryAndTrendAreOmitted()
    {
        HealthExportDocument document = HealthExportService.Create(Report(), Metrics(), exportedAtUtc: ExportedAt);

        Assert.Null(document.History);
        Assert.Null(document.Trend);
        string json = HealthExportService.ToJson(document);
        Assert.Contains("\"History\": null", json);
        Assert.Contains("\"Trend\": null", json);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HealthExportService.Create(null!, Metrics()));
        Assert.Throws<ArgumentNullException>(() =>
            HealthExportService.Create(Report(), null!));
        Assert.Throws<ArgumentNullException>(() => HealthExportService.ToJson(null!));
    }
}
