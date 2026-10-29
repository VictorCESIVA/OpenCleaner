using System.Text.Json;
using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Transactions;

namespace OpenCleaner.Core.Health;

public sealed record HealthExportDisk(string Name, long TotalBytes, long FreeBytes);

public sealed record HealthExportMetrics(
    IReadOnlyList<HealthExportDisk> Disks,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    double UptimeHours,
    long RecycleBinBytes,
    int RecycleBinItems,
    int EnabledStartupItems,
    int DisabledStartupItems,
    long TemporaryBytes,
    int TemporaryItems,
    long BrowserCacheBytes,
    int BrowserCacheItems,
    bool RebootPending);

public sealed record HealthExportSection(string Id, string Title, int Score, int MaxScore, bool Measured);

public sealed record HealthExportFinding(
    string Id,
    string Severity,
    string Title,
    string Detail,
    string Recommendation,
    string? Action,
    long? ReclaimableBytes);

public sealed record HealthExportHistory(int Cleanups, int Files, long Bytes, DateTime? LastCleanupUtc);

public sealed record HealthExportTrendPoint(DateTime Date, long FreeBytes);

public sealed record HealthExportTrend(
    string Drive,
    long? LatestFreeBytes,
    long? DeltaBytes,
    IReadOnlyList<HealthExportTrendPoint> Points);

public sealed record HealthExportDocument(
    int FormatVersion,
    DateTime ExportedAtUtc,
    string Application,
    int Score,
    int MaxScore,
    string Grade,
    long ReclaimableBytes,
    IReadOnlyList<HealthExportSection> Sections,
    IReadOnlyList<HealthExportFinding> Findings,
    HealthExportMetrics Metrics,
    HealthExportHistory? History,
    HealthExportTrend? Trend);

/// <summary>
/// Sérialise un diagnostic en JSON lisible. Aucun chemin de fichier, nom d'utilisateur
/// ou contenu personnel n'y figure : volumes, mesures et constats uniquement.
/// </summary>
public static class HealthExportService
{
    public const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static HealthExportDocument Create(HealthReport report, HealthMetrics metrics,
        CleanupHistorySummary? history = null, SpaceTrendResult? trend = null,
        DateTime? exportedAtUtc = null, string application = "OpenCleaner")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(metrics);

        return new HealthExportDocument(
            CurrentFormatVersion,
            (exportedAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            application,
            report.Score,
            report.MaxScore,
            report.Grade,
            report.ReclaimableBytes,
            report.Sections
                .Select(section => new HealthExportSection(section.Id, section.Title, section.Score,
                    section.MaxScore, section.Measured))
                .ToList(),
            report.Findings
                .Select(finding => new HealthExportFinding(finding.Id, finding.Severity.ToString(),
                    finding.Title, finding.Detail, finding.Recommendation, finding.Action,
                    finding.ReclaimableBytes))
                .ToList(),
            new HealthExportMetrics(
                metrics.Disks.Select(disk => new HealthExportDisk(disk.Name, disk.TotalBytes, disk.FreeBytes))
                    .ToList(),
                metrics.TotalMemoryBytes,
                metrics.AvailableMemoryBytes,
                Math.Round(metrics.Uptime.TotalHours, 2),
                metrics.RecycleBinBytes,
                metrics.RecycleBinItems,
                metrics.EnabledStartupItems,
                metrics.DisabledStartupItems,
                metrics.TemporaryBytes,
                metrics.TemporaryItems,
                metrics.BrowserCacheBytes,
                metrics.BrowserCacheItems,
                metrics.RebootPending),
            history == null
                ? null
                : new HealthExportHistory(history.Cleanups, history.Files, history.Bytes, history.LastCleanupUtc),
            trend == null
                ? null
                : new HealthExportTrend(trend.Drive, trend.LatestFreeBytes, trend.DeltaBytes,
                    trend.Points.Select(point => new HealthExportTrendPoint(point.Date, point.FreeBytes)).ToList()));
    }

    public static string ToJson(HealthExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }
}
