using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Transactions;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class TransactionRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_root, "transactions.db");
    private TransactionManager Manager(IBackupManager backups) => new(backups, NullLogger<TransactionManager>.Instance, Database);
    public TransactionRegressionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RollbackWorksAfterRestartWithDeletedFileMetadata()
    {
        string source = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(source, "restore this");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        Guid id;
        OperationResult deletion = await new FileGuardian(backups, NullLogger<FileGuardian>.Instance).SafeDeleteAsync(source);
        Assert.True(deletion.Success);
        using (var manager = Manager(backups))
        {
            id = manager.BeginTransaction();
            await manager.StageOperationAsync(id, deletion);
        }
        using (var restarted = Manager(backups))
        {
            var operation = Assert.Single(await restarted.GetTransactionHistoryAsync(id));
            Assert.Equal(string.Empty, operation.OriginalFile!.Path);
            Assert.Equal(deletion.OriginalFile!.ContentHash, operation.OriginalFile.ContentHash);
            Assert.Equal(deletion.OriginalFile.Size, operation.OriginalFile.Size);
            await restarted.RollbackAsync(id);
            Assert.Equal(TransactionState.RolledBack, await restarted.GetTransactionStateAsync(id));
        }
        Assert.Equal("restore this", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task GuardianStagesDeletionAndScopeCommits()
    {
        string source = Path.Combine(_root, "committed.txt");
        await File.WriteAllTextAsync(source, "keep this");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        using var transactions = new TransactionManager(backups, NullLogger<TransactionManager>.Instance, Database);
        var guardian = new FileGuardian(backups, NullLogger<FileGuardian>.Instance, transactions);

        Guid transactionId;
        await using (var scope = new CleanupTransactionScope(transactions))
        {
            transactionId = scope.TransactionId;
            Assert.True((await guardian.SafeDeleteAsync(source)).Success);
            await scope.CommitAsync();
        }

        Assert.Equal(TransactionState.Committed, await transactions.GetTransactionStateAsync(transactionId));
        Assert.Single(await transactions.GetTransactionHistoryAsync(transactionId));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task GuardianRollbackRestoresDeletedFile()
    {
        string source = Path.Combine(_root, "rolled-back.txt");
        await File.WriteAllTextAsync(source, "restore this");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        using var transactions = new TransactionManager(backups, NullLogger<TransactionManager>.Instance, Database);
        var guardian = new FileGuardian(backups, NullLogger<FileGuardian>.Instance, transactions);

        Guid transactionId;
        await using (var scope = new CleanupTransactionScope(transactions))
        {
            transactionId = scope.TransactionId;
            Assert.True((await guardian.SafeDeleteAsync(source)).Success);
            await scope.RollbackAsync();
        }

        Assert.Equal(TransactionState.RolledBack, await transactions.GetTransactionStateAsync(transactionId));
        Assert.Equal("restore this", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task JournalFailurePreventsDeletion()
    {
        string source = Path.Combine(_root, "journal-failure.txt");
        await File.WriteAllTextAsync(source, "keep this");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        using var transactions = Manager(backups);
        using (var connection = new SqliteConnection($"Data Source={Database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER deny_operation BEFORE INSERT ON Operations BEGIN SELECT RAISE(ABORT, 'journal failed'); END;";
            command.ExecuteNonQuery();
        }
        await using (var scope = new CleanupTransactionScope(transactions))
        {
            var guardian = new FileGuardian(backups, NullLogger<FileGuardian>.Instance, transactions);
            await Assert.ThrowsAsync<SqliteException>(() => guardian.SafeDeleteAsync(source));
        }
        Assert.Equal("keep this", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task PendingCleanupSurvivesPurgeAndRecoveryDoesNotOverwriteRecreatedFile()
    {
        string source = Path.Combine(_root, "recreated.txt");
        await File.WriteAllTextAsync(source, "old");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        Guid id;
        using (var manager = Manager(backups))
        {
            id = manager.BeginTransaction();
            var deleted = await new FileGuardian(backups, NullLogger<FileGuardian>.Instance).SafeDeleteAsync(source);
            Assert.True(deleted.Success);
            await manager.StageOperationAsync(id, deleted);
        }
        await File.WriteAllTextAsync(source, "new");
        using var restarted = Manager(backups);
        Assert.Contains(id, await restarted.GetUnfinishedTransactionsAsync());
        Assert.Equal(0, await restarted.PurgeOldTransactionsAsync(TimeSpan.FromTicks(1)));
        await restarted.RollbackAsync(id);
        Assert.Equal("new", await File.ReadAllTextAsync(source));
        Assert.Empty(await restarted.GetUnfinishedTransactionsAsync());
    }

    [Fact]
    public async Task ScopePropagatesRollbackFailure()
    {
        var manager = new Mock<ITransactionManager>();
        manager.Setup(m => m.BeginTransaction()).Returns(Guid.NewGuid());
        manager.Setup(m => m.RollbackAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("restore failed"));
        var scope = new CleanupTransactionScope(manager.Object);
        await Assert.ThrowsAsync<IOException>(async () => await scope.DisposeAsync());
    }

    [Fact]
    public async Task JournalStoresOnlyBackupIdentifier()
    {
        string source = Path.Combine(_root, "journal.txt");
        await File.WriteAllTextAsync(source, "journal");
        using var backups = new BackupManager(NullLogger<BackupManager>.Instance, Path.Combine(_root, "backups"));
        using var transactions = new TransactionManager(backups, NullLogger<TransactionManager>.Instance, Database);
        var guardian = new FileGuardian(backups, NullLogger<FileGuardian>.Instance, transactions);
        Guid transactionId;
        await using (var scope = new CleanupTransactionScope(transactions))
        {
            transactionId = scope.TransactionId;
            Assert.True((await guardian.SafeDeleteAsync(source)).Success);
            await scope.CommitAsync();
        }

        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT OriginalPath, BackupPath, OriginalLastAccess, BackupId FROM Operations WHERE TransactionId = @Id;";
        command.Parameters.AddWithValue("@Id", transactionId.ToString());
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.False(reader.IsDBNull(3));
        Assert.Equal(32, reader.GetString(3).Length);
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task PurgeRemovesOldTransactions()
    {
        using var manager = Manager(Mock.Of<IBackupManager>());
        Guid id = manager.BeginTransaction();
        await manager.CommitAsync(id);
        using (var connection = new SqliteConnection($"Data Source={Database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Transactions SET CreatedAt = @CreatedAt WHERE Id = @Id;";
            command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-90).ToString("O"));
            command.Parameters.AddWithValue("@Id", id.ToString());
            command.ExecuteNonQuery();
        }
        Assert.Equal(1, await manager.PurgeOldTransactionsAsync(TimeSpan.FromDays(30)));
        using var restarted = Manager(Mock.Of<IBackupManager>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.GetTransactionStateAsync(id));
    }

    [Fact]
    public async Task FailedRestoreLeavesTruthfulFailedState()
    {
        string backupId = Guid.NewGuid().ToString("N");
        var backups = new Mock<IBackupManager>();
        backups.Setup(b => b.RestoreIfMissingAsync(backupId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        using var manager = Manager(backups.Object);
        Guid id = manager.BeginTransaction();
        await manager.StageOperationAsync(id, SuccessfulOperation(backupId));
        await Assert.ThrowsAsync<IOException>(() => manager.RollbackAsync(id));
        Assert.Equal(TransactionState.Failed, await manager.GetTransactionStateAsync(id));
        using var restarted = Manager(backups.Object);
        Assert.Equal(TransactionState.Failed, await restarted.GetTransactionStateAsync(id));
    }

    [Fact]
    public async Task RollbackSkipsFailedOperationsAndRestoresSuccessfulOnesInReverseOrder()
    {
        string first = Guid.NewGuid().ToString("N");
        string second = Guid.NewGuid().ToString("N");
        string failed = Guid.NewGuid().ToString("N");
        var restored = new List<string>();
        var backups = new Mock<IBackupManager>();
        backups.Setup(b => b.RestoreIfMissingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).Callback<string, CancellationToken>((backupId, _) => restored.Add(backupId));
        using var manager = Manager(backups.Object);
        Guid id = manager.BeginTransaction();
        await manager.StageOperationAsync(id, SuccessfulOperation(first));
        await manager.StageOperationAsync(id, SuccessfulOperation(failed) with { Success = false });
        await manager.StageOperationAsync(id, SuccessfulOperation(second));
        await manager.RollbackAsync(id);
        Assert.Equal(new[] { second, first }, restored);
        await manager.RollbackAsync(id);
        Assert.Equal(2, restored.Count);
    }

    [Fact]
    public async Task CancellationDuringRollbackDoesNotMarkItRolledBack()
    {
        using var cancellation = new CancellationTokenSource();
        var backups = new Mock<IBackupManager>();
        backups.Setup(b => b.RestoreIfMissingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).Callback(() => cancellation.Cancel());
        using var manager = Manager(backups.Object);
        Guid id = manager.BeginTransaction();
        await manager.StageOperationAsync(id, SuccessfulOperation(Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RollbackAsync(id, cancellation.Token));
        Assert.Equal(TransactionState.Failed, await manager.GetTransactionStateAsync(id));
    }

    [Fact]
    public async Task ConcurrentStagingPersistsAllOperationsAndHistoryIsASnapshot()
    {
        using var manager = Manager(Mock.Of<IBackupManager>());
        Guid id = manager.BeginTransaction();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => manager.StageOperationAsync(id,
            new OperationResult(true, i.ToString(), Guid.NewGuid(), ItemsProcessed: 1, BytesRecovered: 0))));
        var snapshot = await manager.GetTransactionHistoryAsync(id);
        await manager.StageOperationAsync(id, new(true, "last", id));
        Assert.Equal(20, snapshot.Count);
        await manager.CommitAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StageOperationAsync(id, new(true, "too late", id)));
        using var restarted = Manager(Mock.Of<IBackupManager>());
        Assert.Equal(21, (await restarted.GetTransactionHistoryAsync(id)).Count);
        Assert.Equal(TransactionState.Committed, await restarted.GetTransactionStateAsync(id));
    }

    private OperationResult SuccessfulOperation(string backupId) => new(true, "deleted", Guid.NewGuid(),
        new FileEntry(Path.Combine(_root, "original.txt"), 1, DateTime.UtcNow, "HASH"),
        Path.Combine(_root, "backups", "2026-10-14", backupId, "data", "original.txt"));

    public void Dispose()
    {
        // Close pooled SQLite handles before removing this isolated test directory on Windows.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
