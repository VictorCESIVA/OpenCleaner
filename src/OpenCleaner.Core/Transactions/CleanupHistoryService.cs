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
            command.CommandText = """
                SELECT t.CreatedAt,
                       COALESCE(SUM(o.ItemsProcessed), 0),
                       COALESCE(SUM(o.OriginalSize), 0)
                FROM Transactions t
                LEFT JOIN Operations o ON o.TransactionId = t.Id AND o.Success = 1
                WHERE t.State = @Committed AND t.CreatedAt >= @Cutoff
                GROUP BY t.Id
                HAVING COALESCE(SUM(o.ItemsProcessed), 0) > 0
                    OR COALESCE(SUM(o.OriginalSize), 0) > 0
                ORDER BY t.CreatedAt DESC;
                """;
            command.Parameters.AddWithValue("@Committed", (int)TransactionState.Committed);
            command.Parameters.AddWithValue("@Cutoff",
                (DateTime.UtcNow - period).ToString("O", CultureInfo.InvariantCulture));

            var entries = new List<CleanupHistoryEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                DateTime timestamp = ParseTimestamp(reader.GetString(0));
                int files = reader.IsDBNull(1) ? 0 : (int)Math.Clamp(reader.GetInt64(1), 0, int.MaxValue);
                long bytes = reader.IsDBNull(2) ? 0 : Math.Max(0, reader.GetInt64(2));
                entries.Add(new CleanupHistoryEntry(timestamp, files, bytes));
            }

            if (entries.Count == 0) return CleanupHistorySummary.Empty;
            int cleanups = entries.Count;
            int totalFiles = entries.Aggregate(0, (sum, entry) =>
                entry.Files > int.MaxValue - sum ? int.MaxValue : sum + entry.Files);
            long totalBytes = entries.Aggregate(0L, (sum, entry) =>
                entry.Bytes > long.MaxValue - sum ? long.MaxValue : sum + entry.Bytes);
            return new CleanupHistorySummary(cleanups, totalFiles, totalBytes, entries[0].TimestampUtc,
                entries.Take(Math.Max(0, maxEntries)).ToList());
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
