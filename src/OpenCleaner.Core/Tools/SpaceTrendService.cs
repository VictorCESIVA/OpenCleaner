using System.Globalization;
using System.Text.Json;

namespace OpenCleaner.Core.Tools;

public sealed record SpaceTrendPoint(DateTime DateUtc, long FreeBytes);

public sealed record SpaceTrendResult(
    string Drive,
    long? LatestFreeBytes,
    long? DeltaBytes,
    IReadOnlyList<SpaceTrendPoint> Points)
{
    public bool HasEnoughData => Points.Count >= 2;
}

/// <summary>
/// Relevé quotidien de l'espace libre, stocké localement en JSON. Un seul point par jour
/// et par volume ; les données anciennes sont élaguées. Aucune information système n'est envoyée.
/// </summary>
public sealed class SpaceTrendService
{
    private const int RetentionDays = 120;
    private const int MaxSeriesLength = 400;
    private static readonly object Gate = new();

    private readonly string _path;

    public SpaceTrendService(string? historyPath = null) =>
        _path = Path.GetFullPath(historyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCleaner", "space-history.json"));

    public string HistoryPath => _path;

    public SpaceTrendResult RecordAndGet(IEnumerable<SystemDiskInfo> disks, DateTime nowUtc,
        string primaryDrive, int days = 30)
    {
        ArgumentNullException.ThrowIfNull(disks);
        if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days));
        if (string.IsNullOrWhiteSpace(primaryDrive)) primaryDrive = "C:";
        DateTime today = nowUtc.ToUniversalTime().Date;
        string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        lock (Gate)
        {
            Dictionary<string, SortedDictionary<string, long>> history = ReadHistory();
            foreach (var disk in disks)
            {
                if (disk.TotalBytes <= 0) continue;
                string key = NormalizeDrive(disk.Name);
                if (key.Length == 0) continue;
                if (!history.TryGetValue(key, out var series))
                    history[key] = series = new SortedDictionary<string, long>(StringComparer.Ordinal);
                series[todayKey] = Math.Max(0, disk.FreeBytes);
                Prune(series, today);
            }

            WriteHistory(history);
            return BuildResult(history, NormalizeDrive(primaryDrive), days, today);
        }
    }

    private static SpaceTrendResult BuildResult(
        Dictionary<string, SortedDictionary<string, long>> history, string drive, int days, DateTime today)
    {
        if (!history.TryGetValue(drive, out var series) || series.Count == 0)
            return new SpaceTrendResult(drive, null, null, []);

        DateTime cutoff = today.AddDays(-(days - 1));
        var points = new List<SpaceTrendPoint>();
        foreach (var (key, freeBytes) in series)
        {
            if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date))
                continue;
            if (date.Date < cutoff) continue;
            points.Add(new SpaceTrendPoint(date.Date, freeBytes));
        }
        if (points.Count == 0) return new SpaceTrendResult(drive, null, null, []);
        long latest = points[^1].FreeBytes;
        long? delta = points.Count >= 2 ? latest - points[0].FreeBytes : null;
        return new SpaceTrendResult(drive, latest, delta, points);
    }

    private static void Prune(SortedDictionary<string, long> series, DateTime today)
    {
        DateTime cutoff = today.AddDays(-RetentionDays);
        var stale = series.Keys
            .Where(key => DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date) &&
                date.Date < cutoff)
            .ToList();
        foreach (string key in stale) series.Remove(key);

        while (series.Count > MaxSeriesLength && series.Count > 0)
            series.Remove(series.Keys.First());
    }

    private Dictionary<string, SortedDictionary<string, long>> ReadHistory()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            string json = File.ReadAllText(_path);
            var stored = JsonSerializer.Deserialize<Dictionary<string, SortedDictionary<string, long>>>(json);
            if (stored == null) return new(StringComparer.OrdinalIgnoreCase);
            var history = new Dictionary<string, SortedDictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (drive, series) in stored)
            {
                if (string.IsNullOrWhiteSpace(drive) || series == null) continue;
                var filtered = new SortedDictionary<string, long>(StringComparer.Ordinal);
                foreach (var (date, freeBytes) in series)
                    if (date.Length == 10) filtered[date] = Math.Max(0, freeBytes);
                history[NormalizeDrive(drive)] = filtered;
            }
            return history;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WriteHistory(Dictionary<string, SortedDictionary<string, long>> history)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(history);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // L'historique est un confort : une écriture impossible ne doit pas bloquer le diagnostic.
        }
    }

    private static string NormalizeDrive(string drive)
    {
        string value = drive.Trim();
        if (value.Length == 0) return "";
        if (value.Length == 2 && value[1] == ':') return char.ToUpperInvariant(value[0]) + ":";
        string? root = Path.GetPathRoot(value);
        if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
            return char.ToUpperInvariant(root[0]) + ":";
        return value.ToUpperInvariant();
    }
}
