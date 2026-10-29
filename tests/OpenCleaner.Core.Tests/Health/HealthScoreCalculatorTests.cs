using OpenCleaner.Core.Health;
using Xunit;

namespace OpenCleaner.Core.Tests.Health;

public sealed class HealthScoreCalculatorTests
{
    private const long GigaByte = 1024L * 1024 * 1024;
    private const long MegaByte = 1024L * 1024;

    private static HealthMetrics Healthy() => new()
    {
        Disks = [new HealthDisk("C:\\", 500 * GigaByte, 200 * GigaByte)],
        TotalMemoryBytes = 16UL * (ulong)GigaByte,
        AvailableMemoryBytes = 10UL * (ulong)GigaByte,
        Uptime = TimeSpan.FromHours(6),
        EnabledStartupItems = 5,
        TemporaryBytes = 40 * MegaByte,
        TemporaryItems = 12,
        BrowserCacheBytes = 200 * MegaByte,
        BrowserCacheItems = 30,
        RecycleBinBytes = 10 * MegaByte,
        RecycleBinItems = 2
    };

    [Fact]
    public void EveryFindingCarriesLocalizationKeys()
    {
        HealthMetrics metrics = Healthy() with
        {
            Disks = [new HealthDisk("C:\\", 500 * GigaByte, 5 * GigaByte)],
            AvailableMemoryBytes = 1UL * (ulong)GigaByte,
            EnabledStartupItems = 40,
            RebootPending = true,
            Uptime = TimeSpan.FromDays(45),
            TemporaryBytes = 20 * GigaByte,
            TemporaryItems = 15,
            BrowserCacheBytes = 6 * GigaByte,
            BrowserCacheItems = 80,
            RecycleBinBytes = 12 * GigaByte,
            RecycleBinItems = 9
        };

        HealthReport report = HealthScoreCalculator.Evaluate(metrics);

        Assert.All(report.Findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.TitleKey), finding.Id + " sans TitleKey");
            Assert.False(string.IsNullOrWhiteSpace(finding.DetailKey), finding.Id + " sans DetailKey");
            Assert.False(string.IsNullOrWhiteSpace(finding.RecommendationKey),
                finding.Id + " sans RecommendationKey");
        });
        Assert.Equal(5, report.Sections.Select(section => section.Id).Distinct().Count());
    }

    [Fact]
    public void HealthyMachineScoresFullMarks()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy());

        Assert.Equal(HealthScoreCalculator.MaxScore, report.Score);
        Assert.Equal("Excellent", report.Grade);
        Assert.Equal(5, report.Sections.Count);
        Assert.All(report.Sections, section => Assert.Equal(section.MaxScore, section.Score));
        Assert.All(report.Findings, finding => Assert.Equal(HealthSeverity.Ok, finding.Severity));
        Assert.Equal(250 * MegaByte, report.ReclaimableBytes);
    }

    [Fact]
    public void LowDiskSpaceProducesCriticalFindingAndAction()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy() with
        {
            Disks = [new HealthDisk("C:\\", 500 * GigaByte, 5 * GigaByte)]
        });

        HealthSection storage = Assert.Single(report.Sections, section => section.Id == "storage");
        Assert.Equal(HealthScoreCalculator.StorageMax - 30, storage.Score);
        HealthFinding finding = Assert.Single(storage.Findings);
        Assert.Equal(HealthSeverity.Critical, finding.Severity);
        Assert.Equal("disk", finding.Action);
        Assert.Contains("5 Go", finding.Detail);
        Assert.True(report.Score < HealthScoreCalculator.MaxScore);
    }

    [Fact]
    public void MemoryPressureIsDeductedAndExplained()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy() with
        {
            TotalMemoryBytes = 16UL * (ulong)GigaByte,
            AvailableMemoryBytes = 1UL * (ulong)GigaByte
        });

        HealthSection memory = Assert.Single(report.Sections, section => section.Id == "memory");
        Assert.Equal(HealthScoreCalculator.MemoryMax - 16, memory.Score);
        Assert.Contains(memory.Findings, finding => finding.Id == "memory.pressure");
    }

    [Fact]
    public void ManyStartupItemsAreReported()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy() with { EnabledStartupItems = 40 });

        HealthSection startup = Assert.Single(report.Sections, section => section.Id == "startup");
        Assert.Equal(HealthScoreCalculator.StartupMax - 12, startup.Score);
        Assert.Contains(startup.Findings, finding => finding.Action == "startup");
    }

    [Fact]
    public void RebootAndLongUptimeReduceMaintenanceScore()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy() with
        {
            RebootPending = true,
            Uptime = TimeSpan.FromDays(45)
        });

        HealthSection maintenance = Assert.Single(report.Sections, section => section.Id == "maintenance");
        Assert.Equal(HealthScoreCalculator.MaintenanceMax - 12, maintenance.Score);
        Assert.Contains(maintenance.Findings, finding => finding.Id == "maintenance.reboot");
        Assert.Contains(maintenance.Findings, finding => finding.Id == "maintenance.uptime");
    }

    [Fact]
    public void ReclaimableBytesCombineTemporaryBrowserAndRecycleBin()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(Healthy() with
        {
            TemporaryBytes = 12 * GigaByte,
            TemporaryItems = 800,
            BrowserCacheBytes = 6 * GigaByte,
            BrowserCacheItems = 1200,
            RecycleBinBytes = 20 * GigaByte,
            RecycleBinItems = 14
        });

        Assert.Equal(38 * GigaByte, report.ReclaimableBytes);
        HealthSection cleanliness = Assert.Single(report.Sections, section => section.Id == "cleanliness");
        Assert.Equal(0, cleanliness.Score);
        Assert.Contains(cleanliness.Findings, finding => finding.Action == "cleanTemp");
        Assert.Contains(cleanliness.Findings, finding => finding.Action == "cleanBrowser");
        Assert.Contains(cleanliness.Findings, finding => finding.Action == "recycle");
    }

    [Fact]
    public void UnknownStorageAndMemoryAreMarkedUnmeasuredAndDoNotCrash()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(new HealthMetrics());

        Assert.Equal(HealthScoreCalculator.MaxScore, report.MaxScore);
        Assert.InRange(report.Score, 0, HealthScoreCalculator.MaxScore);
        Assert.False(Assert.Single(report.Sections, section => section.Id == "storage").Measured);
        Assert.False(Assert.Single(report.Sections, section => section.Id == "memory").Measured);
        Assert.Contains(report.Findings, finding => finding.Id == "storage.unknown");
        Assert.Contains(report.Findings, finding => finding.Id == "memory.unknown");
    }

    [Fact]
    public void ExtremeMetricsStayWithinBoundsAndGradeCritical()
    {
        HealthReport worst = HealthScoreCalculator.Evaluate(new HealthMetrics
        {
            Disks = [new HealthDisk("C:\\", 1000 * GigaByte, 0)],
            TotalMemoryBytes = 8UL * (ulong)GigaByte,
            AvailableMemoryBytes = 0,
            Uptime = TimeSpan.FromDays(400),
            EnabledStartupItems = 200,
            TemporaryBytes = 900 * GigaByte,
            BrowserCacheBytes = 900 * GigaByte,
            RecycleBinBytes = 900 * GigaByte,
            RebootPending = true
        });

        // Chaque section conserve un plancher : même le pire cas reste lisible (20/100).
        Assert.InRange(worst.Score, 0, 20);
        Assert.Equal("Critique", worst.Grade);
        Assert.Equal(HealthScoreCalculator.MaxScore, worst.MaxScore);
        Assert.All(worst.Sections, section => Assert.InRange(section.Score, 0, section.MaxScore));
        Assert.InRange(HealthScoreCalculator.Evaluate(Healthy()).Score, 0, HealthScoreCalculator.MaxScore);
    }

    [Fact]
    public void FindingsAreOrderedBySeverity()
    {
        HealthReport report = HealthScoreCalculator.Evaluate(new HealthMetrics
        {
            Disks = [new HealthDisk("C:\\", 500 * GigaByte, 5 * GigaByte)],
            TotalMemoryBytes = 16UL * (ulong)GigaByte,
            AvailableMemoryBytes = 1UL * (ulong)GigaByte,
            Uptime = TimeSpan.FromDays(60),
            EnabledStartupItems = 3
        });

        var severities = report.Findings.Select(finding => finding.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(severity => severity), severities);
        Assert.Equal(HealthSeverity.Critical, report.Findings[0].Severity);
    }

    [Theory]
    [InlineData(100, "Excellent")]
    [InlineData(90, "Excellent")]
    [InlineData(75, "Bon")]
    [InlineData(74, "À surveiller")]
    [InlineData(55, "À surveiller")]
    [InlineData(54, "Critique")]
    public void GradesFollowDocumentedThresholds(int score, string expected) =>
        Assert.Equal(expected, HealthScoreCalculator.GradeFor(score));

    [Fact]
    public void CollectionReturnsBoundedMetricsWithoutThrowing()
    {
        HealthMetrics metrics = HealthCheckService.Collect(
            new CacheSnapshot(TemporaryBytes: 3 * GigaByte, TemporaryItems: 42,
                BrowserCacheBytes: 2 * GigaByte, BrowserCacheItems: 7));

        Assert.True(metrics.TemporaryBytes >= 0);
        Assert.Equal(3 * GigaByte, metrics.TemporaryBytes);
        Assert.Equal(7, metrics.BrowserCacheItems);
        Assert.All(metrics.Disks, disk => Assert.False(string.IsNullOrWhiteSpace(disk.Name)));
        HealthReport report = HealthScoreCalculator.Evaluate(metrics);
        Assert.InRange(report.Score, 0, HealthScoreCalculator.MaxScore);
    }
}
