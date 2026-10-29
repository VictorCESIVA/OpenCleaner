using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Transactions;
using Xunit;

namespace OpenCleaner.Core.Tests.Transactions;

public sealed class CleanupHistoryServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _database;

    public CleanupHistoryServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-History-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = Path.Combine(_directory, "transactions.db");
    }

    private TransactionManager CreateManager()
    {
        var backup = new Mock<IBackupManager>();
        backup.Setup(manager => manager.RestoreIfMissingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new TransactionManager(backup.Object, NullLogger<TransactionManager>.Instance, _database);
    }

    private static async Task CommitAsync(TransactionManager manager, long size, int files = 1)
    {
        Guid id = manager.BeginTransaction();
        await manager.StageOperationAsync(id, new OperationResult(true, "success", id,
            new FileEntry(string.Empty, size, DateTime.MinValue, "hash"), ItemsProcessed: files,
            BytesRecovered: 0, BackupId: Guid.NewGuid().ToString("N")));
        await manager.CommitAsync(id);
    }

    [Fact]
    public void MissingDatabaseReturnsEmptySummary()
    {
        var service = new CleanupHistoryService(Path.Combine(_directory, "absent.db"));

        Assert.Equal(CleanupHistorySummary.Empty, service.GetSummary(TimeSpan.FromDays(30)));
    }

    [Fact]
    public void InvalidPeriodIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CleanupHistoryService(_database).GetSummary(TimeSpan.Zero));

    [Fact]
    public async Task SummarizesOnlyCommittedRecentTransactions()
    {
        using (var manager = CreateManager())
        {
            await CommitAsync(manager, 1000, files: 2);
            await CommitAsync(manager, 500, files: 1);

            Guid rolledBack = manager.BeginTransaction();
            await manager.StageOperationAsync(rolledBack, new OperationResult(true, "success", rolledBack,
                new FileEntry(string.Empty, 9999, DateTime.MinValue, "hash"), ItemsProcessed: 9,
                BackupId: Guid.NewGuid().ToString("N")));
            await manager.RollbackAsync(rolledBack);
        }
        InsertRawTransaction(_database, DateTime.UtcNow.AddDays(-10), 5000, 4);

        CleanupHistorySummary summary = new CleanupHistoryService(_database).GetSummary(TimeSpan.FromDays(1));

        Assert.Equal(2, summary.Cleanups);
        Assert.Equal(3, summary.Files);
        Assert.Equal(1500, summary.Bytes);
        Assert.NotNull(summary.LastCleanupUtc);
        Assert.True(summary.LastCleanupUtc > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(2, summary.Recent.Count);
    }

    [Fact]
    public async Task OldTransactionsAreExcludedByPeriod()
    {
        using (var manager = CreateManager()) await CommitAsync(manager, 700, files: 1);
        InsertRawTransaction(_database, DateTime.UtcNow.AddDays(-12), 4000, 3);

        var service = new CleanupHistoryService(_database);

        Assert.Equal(2, service.GetSummary(TimeSpan.FromDays(30)).Cleanups);
        Assert.Equal(1, service.GetSummary(TimeSpan.FromDays(7)).Cleanups);
        Assert.Equal(1, service.GetSummary(TimeSpan.FromHours(1)).Cleanups);
    }

    [Fact]
    public async Task RecentEntriesAreLimitedButTotalsCoverEverything()
    {
        using (var manager = CreateManager())
            for (int index = 0; index < 5; index++) await CommitAsync(manager, 100 + index);

        CleanupHistorySummary summary = new CleanupHistoryService(_database)
            .GetSummary(TimeSpan.FromDays(30), maxEntries: 3);

        Assert.Equal(5, summary.Cleanups);
        Assert.Equal(5, summary.Files);
        Assert.Equal(510, summary.Bytes);
        Assert.Equal(3, summary.Recent.Count);
    }

    [Fact]
    public void LegacyRowsWithoutSizeAreTolerated()
    {
        using (var manager = CreateManager()) { }
        InsertRawTransaction(_database, DateTime.UtcNow.AddDays(-1), size: null, files: 3);

        CleanupHistorySummary summary = new CleanupHistoryService(_database).GetSummary(TimeSpan.FromDays(30));

        Assert.Equal(1, summary.Cleanups);
        Assert.Equal(3, summary.Files);
        Assert.Equal(0, summary.Bytes);
    }

    [Fact]
    public void JournalWithoutCleanupReturnsEmpty()
    {
        using (var manager = CreateManager()) { }

        Assert.False(new CleanupHistoryService(_database).GetSummary(TimeSpan.FromDays(30)).HasData);
    }

    private static void InsertRawTransaction(string database, DateTime createdAtUtc, long? size, int files)
    {
        string id = Guid.NewGuid().ToString();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database
        }.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO Transactions (Id, State, CreatedAt) VALUES (@Id, @State, @CreatedAt);";
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@State", (int)TransactionState.Committed);
            command.Parameters.AddWithValue("@CreatedAt",
                createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Operations (Id, TransactionId, Success, Message, Timestamp, OriginalSize,
                    ItemsProcessed, BytesRecovered)
                VALUES (@Id, @TransactionId, 1, 'success', @Timestamp, @Size, @Items, 0);
                """;
            command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@TransactionId", id);
            command.Parameters.AddWithValue("@Timestamp", createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@Size", (object?)size ?? DBNull.Value);
            command.Parameters.AddWithValue("@Items", files);
            command.ExecuteNonQuery();
        }
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
