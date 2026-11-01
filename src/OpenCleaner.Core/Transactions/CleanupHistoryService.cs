using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenCleaner.Core.Transactions;

public sealed record CleanupHistoryEntry(DateTime TimestampUtc, int Files, long Bytes);

public sealed record CleanupHistorySummary(
    int Cleanups,
    int Files,
    long Bytes,
    DateTime? LastCleanupUtc,
    IReadOnlyList<CleanupHistoryEntry> Recent)
{
    public static CleanupHistorySummary Empty { get; } = new(0, 0, 0, null, []);
    public bool HasData => Cleanups > 0;
}

/// <summary>
/// Lecture seule du journal de nettoyage. Aucun chemin de fichier n'est exposé :
/// seuls la date, le nombre de fichiers et le volume d'origine sont agrégés.
/// </summary>
public sealed class CleanupHistoryService
{
    private readonly string _databasePath;

    public CleanupHistoryService(string? databasePath = null) =>
        _databasePath = Path.GetFullPath(databasePath ?? TransactionManager.DefaultDatabasePath);

    public CleanupHistorySummary GetSummary(TimeSpan period, int maxEntries = 6, CancellationToken ct = default)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_databasePath)) return CleanupHistorySummary.Empty;

        try
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = 2
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            const string qualifying = """
                SELECT t.Id, t.CreatedAt,
                       COALESCE(SUM(o.ItemsProcessed), 0) AS Files,
                       COALESCE(SUM(o.OriginalSize), 0) AS Bytes
                FROM Transactions t
                LEFT JOIN Operations o ON o.TransactionId = t.Id AND o.Success = 1
                WHERE t.State = @Committed AND t.CreatedAt >= @Cutoff
                GROUP BY t.Id
                HAVING COALESCE(SUM(o.ItemsProcessed), 0) > 0
                    OR COALESCE(SUM(o.OriginalSize), 0) > 0
                """;
            command.Parameters.AddWithValue("@Committed", (int)TransactionState.Committed);
            command.Parameters.AddWithValue("@Cutoff",
                (DateTime.UtcNow - period).ToString("O", CultureInfo.InvariantCulture));

            command.CommandText = $"""
                WITH Qualifying AS ({qualifying})
                SELECT COUNT(*),
                       COALESCE(SUM(MIN(MAX(Files, 0), 2147483647)), 0),
                       COALESCE(SUM(MAX(Bytes, 0)), 0),
                       MAX(CreatedAt)
                FROM Qualifying;
                """;
            int cleanups;
            long totalFiles;
            long totalBytes;
            string? lastCreatedAt;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return CleanupHistorySummary.Empty;
                ct.ThrowIfCancellationRequested();
                cleanups = (int)Math.Min(reader.GetInt64(0), int.MaxValue);
                totalFiles = reader.GetInt64(1);
                totalBytes = reader.GetInt64(2);
                lastCreatedAt = reader.IsDBNull(3) ? null : reader.GetString(3);
            }
            if (cleanups == 0 || lastCreatedAt == null) return CleanupHistorySummary.Empty;

            command.CommandText = $"""
                WITH Qualifying AS ({qualifying})
                SELECT CreatedAt, Files, Bytes
                FROM Qualifying
                ORDER BY CreatedAt DESC
                LIMIT @Limit;
                """;
            command.Parameters.AddWithValue("@Limit", Math.Max(0, maxEntries));
            var recent = new List<CleanupHistoryEntry>(Math.Min(cleanups, Math.Max(0, maxEntries)));
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    DateTime timestamp = ParseTimestamp(reader.GetString(0));
                    int files = (int)Math.Clamp(reader.GetInt64(1), 0, int.MaxValue);
                    long bytes = Math.Max(0, reader.GetInt64(2));
                    recent.Add(new CleanupHistoryEntry(timestamp, files, bytes));
                }
            }

            return new CleanupHistorySummary(
                cleanups,
                (int)Math.Min(totalFiles, int.MaxValue),
                totalBytes,
                ParseTimestamp(lastCreatedAt),
                recent);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
                                       or InvalidCastException or FormatException)
        {
            return CleanupHistorySummary.Empty;
        }
    }

    private static DateTime ParseTimestamp(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
