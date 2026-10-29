using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Scheduler;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Transactions;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class OptionalBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleaner-OptionalBackup-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_root, "transactions.db");
    public OptionalBackupTests() => Directory.CreateDirectory(_root);
    private TransactionManager Manager(IBackupManager backups) => new(backups, NullLogger<TransactionManager>.Instance, Database);
    private FileGuardian Guardian(IBackupManager backups, ITransactionManager transactions) => new(backups, NullLogger<FileGuardian>.Instance, transactions);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChoiceFlowsIntoWorkerAndOnlyRequestedBackupsAreCreated(bool createBackup)
    {
        string source = Path.Combine(_root, "cache.txt");
        await File.WriteAllTextAsync(source, "temporary cache");
        long size = new FileInfo(source).Length;
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        using var transactions = Manager(backups);
        Guid id;
        await using (var scope = new CleanupTransactionScope(transactions, createBackup))
        {
            id = scope.TransactionId;
            var result = await Task.Run(() => Guardian(backups, transactions).SafeDeleteAsync(source));
            Assert.True(result.Success, result.Message);
            Assert.Equal(createBackup ? 0 : size, result.BytesRecovered);
            await scope.CommitAsync();
        }
        Assert.False(File.Exists(source));
        Assert.Equal(createBackup ? 1 : 0, (await backups.GetAllBackupsAsync()).Count);
        Assert.Equal(TransactionState.Committed, await transactions.GetTransactionStateAsync(id));
        Assert.Equal(1, new CleanupHistoryService(Database).GetSummary(TimeSpan.FromDays(1)).Files);
        Assert.Null(CleanupTransactionScope.CurrentTransactionId);
        Assert.True(CleanupTransactionScope.CreateBackup);
    }

    [Fact]
    public async Task CancellationWithoutBackupKeepsDeletedFilesDeletedAndNeverCallsBackupStorage()
    {
        string first = Path.Combine(_root, "first.txt"), second = Path.Combine(_root, "second.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        var backups = new Mock<IBackupManager>(MockBehavior.Strict);
        using var transactions = Manager(backups.Object);
        Guid id;
        await using (var scope = new CleanupTransactionScope(transactions, createBackup: false))
        {
            id = scope.TransactionId;
            var guardian = Guardian(backups.Object, transactions);
            Assert.True((await guardian.SafeDeleteAsync(first)).Success);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guardian.SafeDeleteAsync(second, ct: cancellation.Token));
        }
        Assert.False(File.Exists(first));
        Assert.Equal("second", await File.ReadAllTextAsync(second));
        Assert.Equal(TransactionState.Committed, await transactions.GetTransactionStateAsync(id));
        backups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecoveryAfterRestartDoesNotAttemptToRestorePermanentDeletions()
    {
        string source = Path.Combine(_root, "permanent.txt");
        await File.WriteAllTextAsync(source, "permanent");
        var backups = new Mock<IBackupManager>(MockBehavior.Strict);
        Guid id;
        using (var transactions = Manager(backups.Object))
        {
            id = transactions.BeginTransaction(createBackup: false);
            var result = await Guardian(backups.Object, transactions).SafeDeleteAsync(source, createBackup: false);
            Assert.True(result.Success);
            await transactions.StageOperationAsync(id, result);
        }
        using var restarted = Manager(backups.Object);
        await restarted.RollbackAsync(id);
        Assert.False(File.Exists(source));
        Assert.Equal(TransactionState.Committed, await restarted.GetTransactionStateAsync(id));
        Assert.Empty(await restarted.GetUnfinishedTransactionsAsync());
        Assert.Equal(1, new CleanupHistoryService(Database).GetSummary(TimeSpan.FromDays(1)).Files);
        backups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecursiveCleanupUsesTheSameChoiceForEveryFile()
    {
        string directory = Path.Combine(_root, "cache");
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory, "first.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(directory, "nested", "second.txt"), "two");
        var backups = new Mock<IBackupManager>(MockBehavior.Strict);
        using var transactions = Manager(backups.Object);
        await using (var scope = new CleanupTransactionScope(transactions, createBackup: false))
        {
            var result = await Guardian(backups.Object, transactions).SafeCleanDirectoryAsync(directory, recursive: true);
            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.ItemsProcessed);
            Assert.Equal(6, result.BytesRecovered);
            await scope.CommitAsync();
        }
        Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        backups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefusingBackupDoesNotBypassExclusionsOrSystemProtections()
    {
        string source = Path.Combine(_root, "excluded.txt");
        await File.WriteAllTextAsync(source, "keep");
        using var exclusions = CleaningExclusions.OverrideForTests([_root]);
        var backups = new Mock<IBackupManager>(MockBehavior.Strict);
        using var transactions = Manager(backups.Object);
        await using var scope = new CleanupTransactionScope(transactions, createBackup: false);
        var guardian = Guardian(backups.Object, transactions);
        Assert.False((await guardian.SafeDeleteAsync(source)).Success);
        Assert.True(guardian.IsSystemCriticalPath(@"C:\Windows\System32\kernel32.dll"));
        Assert.Equal("keep", await File.ReadAllTextAsync(source));
        backups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChoicesAreIsolatedBetweenConcurrentOperationsAndResetAfterDispose()
    {
        var transactions = new Mock<ITransactionManager>();
        transactions.Setup(m => m.BeginTransaction(It.IsAny<bool>())).Returns(() => Guid.NewGuid());
        async Task Check(bool backup)
        {
            await using (var scope = new CleanupTransactionScope(transactions.Object, backup))
            {
                await Task.Yield();
                Assert.Equal(backup, await Task.Run(() => CleanupTransactionScope.CreateBackup));
                await scope.CommitAsync();
            }
            Assert.Null(CleanupTransactionScope.CurrentTransactionId);
            Assert.True(CleanupTransactionScope.CreateBackup);
        }
        await Task.WhenAll(Task.Run(() => Check(false)), Task.Run(() => Check(true)));
    }

    [Fact]
    public async Task LegacyJournalDefaultsToBackupAndNewChoiceSurvivesRestart()
    {
        Guid legacy = Guid.NewGuid(), permanent;
        using (var connection = new SqliteConnection($"Data Source={Database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Transactions (Id TEXT PRIMARY KEY, State INTEGER NOT NULL, CreatedAt TEXT NOT NULL); INSERT INTO Transactions VALUES (@Id, 0, @CreatedAt);";
            command.Parameters.AddWithValue("@Id", legacy.ToString());
            command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        var backups = new Mock<IBackupManager>(MockBehavior.Strict);
        using (var transactions = Manager(backups.Object)) permanent = transactions.BeginTransaction(false);
        using var restarted = Manager(backups.Object);
        await restarted.RollbackAsync(legacy);
        await restarted.RollbackAsync(permanent);
        Assert.Equal(TransactionState.RolledBack, await restarted.GetTransactionStateAsync(legacy));
        Assert.Equal(TransactionState.Committed, await restarted.GetTransactionStateAsync(permanent));
        backups.VerifyNoOtherCalls();
    }

    [Fact]
    public void ExistingSchedulesKeepBackupsAndExplicitRefusalRoundTrips()
    {
        Assert.True(JsonSerializer.Deserialize<ScheduleConfig>("{}")!.CreateBackup);
        var config = new ScheduleConfig { CreateBackup = false };
        Assert.False(JsonSerializer.Deserialize<ScheduleConfig>(JsonSerializer.Serialize(config))!.CreateBackup);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
