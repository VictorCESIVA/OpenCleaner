using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Transactions;

public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private readonly IBackupManager _backupManager;
    private readonly ILogger<TransactionManager> _logger;
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _transactionLocks = new();
    private readonly ConcurrentDictionary<Guid, Transaction> _inMemoryTransactions = new();

    public TransactionManager(IBackupManager backupManager, ILogger<TransactionManager> logger)
    {
        _backupManager = backupManager;
        _logger = logger;

        string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCleaner",
            "transactions.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath};Foreign Keys=True";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string createTransactionsTable = @"
            CREATE TABLE IF NOT EXISTS Transactions (
                Id TEXT PRIMARY KEY,
                State INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );";

        string createOperationsTable = @"
            CREATE TABLE IF NOT EXISTS Operations (
                Id TEXT PRIMARY KEY,
                TransactionId TEXT NOT NULL,
                Success INTEGER NOT NULL,
                Message TEXT NOT NULL,
                OriginalPath TEXT,
                BackupPath TEXT,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (TransactionId) REFERENCES Transactions(Id) ON DELETE CASCADE
            );";

        using (var cmd = new SqliteCommand(createTransactionsTable, connection))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqliteCommand(createOperationsTable, connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    public Guid BeginTransaction()
    {
        var transaction = new Transaction();
        _inMemoryTransactions[transaction.Id] = transaction;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        const string insertSql = @"
            INSERT INTO Transactions (Id, State, CreatedAt)
            VALUES (@Id, @State, @CreatedAt);";

        using var cmd = new SqliteCommand(insertSql, connection);
        cmd.Parameters.AddWithValue("@Id", transaction.Id.ToString());
        cmd.Parameters.AddWithValue("@State", (int)transaction.State);
        cmd.Parameters.AddWithValue("@CreatedAt", transaction.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        _logger.LogInformation("Transaction started: {TransactionId}", transaction.Id);
        return transaction.Id;
    }

    public async Task StageOperationAsync(Guid transactionId, OperationResult operation)
    {
        if (!_inMemoryTransactions.TryGetValue(transactionId, out Transaction? transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} not found");
        }

        if (transaction.State != TransactionState.Pending)
        {
            throw new InvalidOperationException($"Transaction {transactionId} is not in Pending state");
        }

        transaction.Operations.Add(operation);

        await Task.Run(() =>
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            const string insertSql = @"
                INSERT INTO Operations (Id, TransactionId, Success, Message, OriginalPath, BackupPath, Timestamp)
                VALUES (@Id, @TransactionId, @Success, @Message, @OriginalPath, @BackupPath, @Timestamp);";

            using var cmd = new SqliteCommand(insertSql, connection);
            cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@TransactionId", transactionId.ToString());
            cmd.Parameters.AddWithValue("@Success", operation.Success ? 1 : 0);
            cmd.Parameters.AddWithValue("@Message", operation.Message);
            cmd.Parameters.AddWithValue("@OriginalPath", operation.OriginalFile?.Path ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BackupPath", operation.BackupPath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        _logger.LogDebug("Operation staged in transaction {TransactionId}: {Message}", transactionId, operation.Message);
    }

    public async Task CommitAsync(Guid transactionId, CancellationToken ct = default)
    {
        SemaphoreSlim semaphore = _transactionLocks.GetOrAdd(transactionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);

        try
        {
            if (!_inMemoryTransactions.TryGetValue(transactionId, out Transaction? transaction))
            {
                throw new InvalidOperationException($"Transaction {transactionId} not found");
            }

            if (transaction.State != TransactionState.Pending)
            {
                throw new InvalidOperationException($"Transaction {transactionId} is not in Pending state");
            }

            // Vérifie que toutes les opérations sont en succès
            bool allSuccess = transaction.Operations.All(o => o.Success);

            if (!allSuccess)
            {
                _logger.LogWarning("Transaction {TransactionId} has failed operations, rolling back", transactionId);
                // Appel interne pour éviter un deadlock (le verrou est déjà acquis)
                await DoRollbackAsync(transactionId, transaction, ct);
                throw new InvalidOperationException("Transaction failed, rolled back");
            }

            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                const string updateSql = @"
                    UPDATE Transactions SET State = @State WHERE Id = @Id;";

                using var cmd = new SqliteCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@State", (int)TransactionState.Committed);
                cmd.Parameters.AddWithValue("@Id", transactionId.ToString());
                cmd.ExecuteNonQuery();
            }, ct);

            transaction.MarkCommitted();
            _logger.LogInformation("Transaction committed: {TransactionId}", transactionId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RollbackAsync(Guid transactionId, CancellationToken ct = default)
    {
        SemaphoreSlim semaphore = _transactionLocks.GetOrAdd(transactionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);

        try
        {
            if (!_inMemoryTransactions.TryGetValue(transactionId, out Transaction? transaction))
            {
                throw new InvalidOperationException($"Transaction {transactionId} not found");
            }

            await DoRollbackAsync(transactionId, transaction, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // Logique de rollback sans acquisition du verrou — appelable depuis CommitAsync ou RollbackAsync.
    private async Task DoRollbackAsync(Guid transactionId, Transaction transaction, CancellationToken ct)
    {
        _logger.LogInformation("Rolling back transaction {TransactionId}", transactionId);

        // Restaure les backups
        foreach (OperationResult operation in transaction.Operations.Where(o => !string.IsNullOrEmpty(o.BackupPath)))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!string.IsNullOrEmpty(operation.BackupPath))
                {
                    // Extrait le backupId du chemin
                    string? backupId = ExtractBackupIdFromPath(operation.BackupPath);
                    if (!string.IsNullOrEmpty(backupId))
                    {
                        bool restored = await _backupManager.RestoreAsync(backupId, ct);
                        if (restored)
                        {
                            _logger.LogInformation("Restored backup {BackupId} for transaction {TransactionId}", backupId, transactionId);
                        }
                        else
                        {
                            _logger.LogError("Failed to restore backup {BackupId} for transaction {TransactionId}", backupId, transactionId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring backup for operation in transaction {TransactionId}", transactionId);
                // Continue malgré l'erreur (meilleur effort)
            }
        }

        await Task.Run(() =>
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            const string updateSql = @"
                UPDATE Transactions SET State = @State WHERE Id = @Id;";

            using var cmd = new SqliteCommand(updateSql, connection);
            cmd.Parameters.AddWithValue("@State", (int)TransactionState.RolledBack);
            cmd.Parameters.AddWithValue("@Id", transactionId.ToString());
            cmd.ExecuteNonQuery();
        }, ct);

        transaction.MarkRolledBack();
        _logger.LogInformation("Transaction rolled back: {TransactionId}", transactionId);
    }

    public async Task<TransactionState> GetTransactionStateAsync(Guid transactionId)
    {
        if (_inMemoryTransactions.TryGetValue(transactionId, out Transaction? memoryTransaction))
        {
            return memoryTransaction.State;
        }

        return await Task.Run(() =>
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            const string selectSql = @"
                SELECT State FROM Transactions WHERE Id = @Id;";

            using var cmd = new SqliteCommand(selectSql, connection);
            cmd.Parameters.AddWithValue("@Id", transactionId.ToString());

            object? result = cmd.ExecuteScalar();
            if (result == null)
            {
                throw new InvalidOperationException($"Transaction {transactionId} not found");
            }

            return (TransactionState)Convert.ToInt32(result);
        });
    }

    public async Task<IReadOnlyList<OperationResult>> GetTransactionHistoryAsync(Guid transactionId)
    {
        if (_inMemoryTransactions.TryGetValue(transactionId, out Transaction? memoryTransaction))
        {
            return memoryTransaction.Operations.AsReadOnly();
        }

        var operations = new List<OperationResult>();

        await Task.Run(() =>
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            const string selectSql = @"
                SELECT Success, Message, OriginalPath, BackupPath
                FROM Operations
                WHERE TransactionId = @TransactionId
                ORDER BY Timestamp;";

            using var cmd = new SqliteCommand(selectSql, connection);
            cmd.Parameters.AddWithValue("@TransactionId", transactionId.ToString());

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bool success = reader.GetInt32(0) == 1;
                string message = reader.GetString(1);
                string? originalPath = reader.IsDBNull(2) ? null : reader.GetString(2);
                string? backupPath = reader.IsDBNull(3) ? null : reader.GetString(3);

                FileEntry? originalFile = null;
                if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
                {
                    var fileInfo = new FileInfo(originalPath);
                    originalFile = new FileEntry(
                        Path: originalPath,
                        Size: fileInfo.Length,
                        LastAccessTime: fileInfo.LastAccessTime,
                        ContentHash: ""); // Hash non stocké en DB
                }

                operations.Add(new OperationResult(
                    Success: success,
                    Message: message,
                    TransactionId: transactionId,
                    OriginalFile: originalFile,
                    BackupPath: backupPath));
            }
        });

        return operations.AsReadOnly();
    }

    private static string? ExtractBackupIdFromPath(string backupPath)
    {
        // Extrait le GUID du chemin de backup
        // Format: ...\Backups\yyyy-MM-dd\{guid}\data\filename
        string[] parts = backupPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i + 1] == "data" && Guid.TryParse(parts[i], out _))
            {
                return parts[i];
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim semaphore in _transactionLocks.Values)
        {
            semaphore.Dispose();
        }
        _transactionLocks.Clear();
    }
}
