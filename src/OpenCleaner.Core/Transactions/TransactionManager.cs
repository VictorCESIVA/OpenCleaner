using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Transactions;

public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private readonly IBackupManager _backupManager;
    private readonly ILogger<TransactionManager> _logger;
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, Transaction> _transactions = new();

    public TransactionManager(IBackupManager backupManager, ILogger<TransactionManager> logger)
        : this(backupManager, logger, null) { }

    public TransactionManager(IBackupManager backupManager, ILogger<TransactionManager> logger, string? databasePath)
    {
        _backupManager = backupManager;
        _logger = logger;
        string path = Path.GetFullPath(databasePath ?? DefaultDatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        InitializeDatabase();
    }

    /// <summary>Emplacement partagé du journal local des nettoyages.</summary>
    public static string DefaultDatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCleaner", "transactions.db");

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitializeDatabase()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Transactions (Id TEXT PRIMARY KEY, State INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                CreateBackup INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS Operations (
                Id TEXT PRIMARY KEY, TransactionId TEXT NOT NULL, Success INTEGER NOT NULL, Message TEXT NOT NULL,
                OriginalPath TEXT, BackupPath TEXT, Timestamp TEXT NOT NULL,
                OriginalSize INTEGER, OriginalLastAccess TEXT, OriginalHash TEXT, ItemsProcessed INTEGER,
                BytesRecovered INTEGER, BackupId TEXT,
                FOREIGN KEY (TransactionId) REFERENCES Transactions(Id) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name = 'CreateBackup';";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = "ALTER TABLE Transactions ADD COLUMN CreateBackup INTEGER NOT NULL DEFAULT 1;";
            command.ExecuteNonQuery();
        }
        command.CommandText = "PRAGMA table_info(Operations);";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) columns.Add(reader.GetString(1));
        foreach (var (name, type) in new[] { ("OriginalSize", "INTEGER"), ("OriginalLastAccess", "TEXT"),
                     ("OriginalHash", "TEXT"), ("ItemsProcessed", "INTEGER"), ("BytesRecovered", "INTEGER"),
                     ("BackupId", "TEXT") })
        {
            if (columns.Contains(name)) continue;
            command.CommandText = $"ALTER TABLE Operations ADD COLUMN {name} {type};";
            command.ExecuteNonQuery();
        }

        command.CommandText = """
            SELECT Id, BackupPath FROM Operations
            WHERE BackupPath IS NOT NULL OR OriginalPath IS NOT NULL OR OriginalLastAccess IS NOT NULL;
            """;
        var paths = new List<(string Id, string? BackupPath)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                paths.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        if (paths.Count > 0)
        {
            command.CommandText = """
                UPDATE Operations SET BackupId = @BackupId, BackupPath = NULL,
                    OriginalPath = NULL, OriginalLastAccess = NULL WHERE Id = @Id;
                """;
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@Id";
            command.Parameters.Add(idParameter);
            var backupParameter = command.CreateParameter();
            backupParameter.ParameterName = "@BackupId";
            command.Parameters.Add(backupParameter);
            foreach (var (id, backupPath) in paths)
            {
                idParameter.Value = id;
                backupParameter.Value = (object?)ExtractBackupId(backupPath) ?? DBNull.Value;
                command.ExecuteNonQuery();
            }
        }
    }

    public Guid BeginTransaction(bool createBackup = true)
    {
        var transaction = new Transaction(createBackup);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Transactions (Id, State, CreatedAt, CreateBackup) VALUES (@Id, @State, @CreatedAt, @CreateBackup);";
        command.Parameters.AddWithValue("@Id", transaction.Id.ToString());
        command.Parameters.AddWithValue("@State", (int)transaction.State);
        command.Parameters.AddWithValue("@CreatedAt", transaction.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@CreateBackup", createBackup ? 1 : 0);
        command.ExecuteNonQuery();
        _transactions[transaction.Id] = transaction;
        return transaction.Id;
    }

    public async Task StageOperationAsync(Guid transactionId, OperationResult operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var gate = GetLock(transactionId);
        await gate.WaitAsync();
        try
        {
            Transaction transaction = GetTransaction(transactionId);
            if (transaction.State != TransactionState.Pending)
                throw new InvalidOperationException($"Transaction {transactionId} is not pending");
            string? backupId = ExtractBackupId(operation);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Operations (Id, TransactionId, Success, Message, OriginalPath, BackupPath, Timestamp,
                    OriginalSize, OriginalLastAccess, OriginalHash, ItemsProcessed, BytesRecovered, BackupId)
                VALUES (@Id, @TransactionId, @Success, @Message, @OriginalPath, @BackupPath, @Timestamp,
                    @Size, @LastAccess, @Hash, @Items, @Bytes, @BackupId);
                """;
            command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@TransactionId", transactionId.ToString());
            command.Parameters.AddWithValue("@Success", operation.Success ? 1 : 0);
            command.Parameters.AddWithValue("@Message", operation.Success ? "success" : "failure");
            command.Parameters.AddWithValue("@OriginalPath", DBNull.Value);
            command.Parameters.AddWithValue("@BackupPath", DBNull.Value);
            command.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@Size", (object?)operation.OriginalFile?.Size ?? DBNull.Value);
            command.Parameters.AddWithValue("@LastAccess", DBNull.Value);
            command.Parameters.AddWithValue("@Hash", (object?)operation.OriginalFile?.ContentHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@Items", (object?)operation.ItemsProcessed ?? DBNull.Value);
            command.Parameters.AddWithValue("@Bytes", (object?)operation.BytesRecovered ?? DBNull.Value);
            command.Parameters.AddWithValue("@BackupId", (object?)backupId ?? DBNull.Value);
            command.ExecuteNonQuery();
            FileEntry? original = operation.OriginalFile is null ? null : operation.OriginalFile with
            {
                Path = string.Empty,
                LastAccessTime = default
            };
            transaction.Operations.Add(operation with
            {
                TransactionId = transactionId,
                Message = operation.Success ? "success" : "failure",
                OriginalFile = original,
                BackupPath = null,
                BackupId = backupId
            });
        }
        finally { gate.Release(); }
    }

    public async Task CommitAsync(Guid transactionId, CancellationToken ct = default)
    {
        var gate = GetLock(transactionId);
        await gate.WaitAsync(ct);
        try
        {
            Transaction transaction = GetTransaction(transactionId);
            if (transaction.State != TransactionState.Pending)
                throw new InvalidOperationException($"Transaction {transactionId} is not pending");
            if (transaction.Operations.Any(o => !o.Success))
            {
                await DoRollbackAsync(transaction, ct);
                throw new InvalidOperationException("Transaction contained failed operations");
            }
            ct.ThrowIfCancellationRequested();
            SaveState(transaction, TransactionState.Committed);
        }
        finally { gate.Release(); }
    }

    public async Task CommitPartialAsync(Guid transactionId, CancellationToken ct = default)
    {
        var gate = GetLock(transactionId);
        await gate.WaitAsync(ct);
        try
        {
            Transaction transaction = GetTransaction(transactionId);
            if (transaction.State != TransactionState.Pending)
                throw new InvalidOperationException($"Transaction {transactionId} is not pending");
            ct.ThrowIfCancellationRequested();
            SaveState(transaction, TransactionState.Committed);
        }
        finally { gate.Release(); }
    }

    public Task<int> PurgeOldTransactionsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        ct.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Transactions WHERE CreatedAt < @Cutoff AND State IN (@Committed, @RolledBack);";
        command.Parameters.AddWithValue("@Committed", (int)TransactionState.Committed);
        command.Parameters.AddWithValue("@RolledBack", (int)TransactionState.RolledBack);
        command.Parameters.AddWithValue("@Cutoff",
            (DateTime.UtcNow - maxAge).ToString("O", CultureInfo.InvariantCulture));
        return Task.FromResult(command.ExecuteNonQuery());
    }

    public async Task RollbackAsync(Guid transactionId, CancellationToken ct = default)
    {
        var gate = GetLock(transactionId);
        await gate.WaitAsync(ct);
        try { await DoRollbackAsync(GetTransaction(transactionId), ct); }
        finally { gate.Release(); }
    }

    public Task<IReadOnlyList<Guid>> GetUnfinishedTransactionsAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Transactions WHERE State IN (@Pending, @Failed) ORDER BY CreatedAt";
        command.Parameters.AddWithValue("@Pending", (int)TransactionState.Pending);
        command.Parameters.AddWithValue("@Failed", (int)TransactionState.Failed);
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    private async Task DoRollbackAsync(Transaction transaction, CancellationToken ct)
    {
        if (transaction.State == TransactionState.RolledBack) return;
        if (!transaction.CreateBackup)
        {
            // Permanent deletions remain committed, including after cancellation or a restart.
            ct.ThrowIfCancellationRequested();
            SaveState(transaction, TransactionState.Committed);
            return;
        }
        var failures = new List<string>();
        // Undo in reverse order. Failed operations did not delete anything and must not replace
        // the user's still-existing file with an older backup.
        try
        {
            foreach (OperationResult operation in transaction.Operations.Where(o => o.Success).Reverse())
            {
                ct.ThrowIfCancellationRequested();
                string? id = ExtractBackupId(operation);
                if (id == null)
                {
                    failures.Add(operation.BackupId ?? operation.Message);
                    continue;
                }
                try
                {
                    if (!await _backupManager.RestoreIfMissingAsync(id, ct)) failures.Add(id);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                { _logger.LogError(ex, "Could not restore backup {BackupId}", id); failures.Add(id); }
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            SaveState(transaction, TransactionState.Failed);
            throw;
        }
        if (failures.Count != 0)
        {
            SaveState(transaction, TransactionState.Failed);
            throw new IOException($"Rollback incomplete: {failures.Count} operation(s) could not be restored");
        }
        SaveState(transaction, TransactionState.RolledBack);
    }

    private void SaveState(Transaction transaction, TransactionState state)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Transactions SET State = @State WHERE Id = @Id;";
        command.Parameters.AddWithValue("@State", (int)state);
        command.Parameters.AddWithValue("@Id", transaction.Id.ToString());
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Transaction no longer exists");
        switch (state)
        {
            case TransactionState.Committed: transaction.MarkCommitted(); break;
            case TransactionState.RolledBack: transaction.MarkRolledBack(); break;
            case TransactionState.Failed: transaction.MarkFailed(); break;
        }
    }

    public async Task<TransactionState> GetTransactionStateAsync(Guid transactionId)
    {
        var gate = GetLock(transactionId);
        await gate.WaitAsync();
        try { return GetTransaction(transactionId).State; }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<OperationResult>> GetTransactionHistoryAsync(Guid transactionId)
    {
        var gate = GetLock(transactionId);
        await gate.WaitAsync();
        try { return GetTransaction(transactionId).Operations.ToArray(); }
        finally { gate.Release(); }
    }



    private Transaction GetTransaction(Guid id)
    {
        if (_transactions.TryGetValue(id, out var transaction)) return transaction;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT State, CreatedAt, CreateBackup FROM Transactions WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", id.ToString());
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException($"Transaction {id} not found");
            transaction = new Transaction(id, (TransactionState)reader.GetInt32(0),
                DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetInt32(2) != 0);
        }
        command.CommandText = """
            SELECT Success, Message, OriginalPath, BackupPath, OriginalSize, OriginalLastAccess, OriginalHash,
                ItemsProcessed, BytesRecovered, BackupId
            FROM Operations WHERE TransactionId = @Id ORDER BY rowid;
            """;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                FileEntry? original = reader.IsDBNull(6) ? null :
                    new(string.Empty, reader.IsDBNull(4) ? 0 : reader.GetInt64(4), DateTime.MinValue,
                        reader.GetString(6));
                transaction.Operations.Add(new(reader.GetInt32(0) == 1, reader.GetString(1), id, original,
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }
        _transactions[id] = transaction;
        return transaction;
    }

    private SemaphoreSlim GetLock(Guid id) => _locks.GetOrAdd(id, _ => new(1, 1));
    private static string? ExtractBackupId(OperationResult operation)
    {
        if (!string.IsNullOrEmpty(operation.BackupId) && Guid.TryParseExact(operation.BackupId, "N", out _))
            return operation.BackupId;
        return ExtractBackupId(operation.BackupPath);
    }

    private static string? ExtractBackupId(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string? data = Path.GetDirectoryName(path);
        if (!string.Equals(Path.GetFileName(data), "data", StringComparison.OrdinalIgnoreCase)) return null;
        string? id = Path.GetFileName(Path.GetDirectoryName(data));
        return Guid.TryParseExact(id, "N", out _) ? id : null;
    }

    public void Dispose()
    {
        foreach (var gate in _locks.Values) gate.Dispose();
        _locks.Clear();
    }
}
