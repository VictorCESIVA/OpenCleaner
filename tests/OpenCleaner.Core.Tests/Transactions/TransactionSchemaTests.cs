using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Transactions;
using Xunit;

namespace OpenCleaner.Core.Tests.Transactions;

public sealed class TransactionSchemaTests : IDisposable
{
    private readonly string _directory;

    public TransactionSchemaTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    private string Database => Path.Combine(_directory, "transactions.db");

    private TransactionManager CreateManager() =>
        new(Mock.Of<IBackupManager>(), NullLogger<TransactionManager>.Instance, Database);

    [Fact]
    public void LegacyPathMigrationRunsOnceAndIndexesAreCreated()
    {
        string transactionId = Guid.NewGuid().ToString();
        string operationId = Guid.NewGuid().ToString();
        string backupId = Guid.NewGuid().ToString("N");
        string legacyPath = Path.Combine(_directory, "backups", "2024-01-01", backupId, "data", "file.txt");
        CreateLegacyDatabase(transactionId, operationId, legacyPath);

        using (CreateManager()) { }

        Assert.Equal(1L, Scalar("PRAGMA user_version;"));
        Assert.Equal(1L, Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Transactions_State_CreatedAt';"));
        Assert.Equal(1L, Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Operations_TransactionId_Success';"));

        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT BackupId, BackupPath, OriginalPath, OriginalLastAccess FROM Operations WHERE Id = @Id;";
            command.Parameters.AddWithValue("@Id", operationId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(backupId, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
        }

        string lateOperationId = Guid.NewGuid().ToString();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Operations (Id, TransactionId, Success, Message, OriginalPath, BackupPath, Timestamp)
                VALUES (@Id, @TransactionId, 1, 'success', NULL, @BackupPath, @Timestamp);
                """;
            command.Parameters.AddWithValue("@Id", lateOperationId);
            command.Parameters.AddWithValue("@TransactionId", transactionId);
            command.Parameters.AddWithValue("@BackupPath", legacyPath);
            command.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        using (CreateManager()) { }

        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT BackupId, BackupPath FROM Operations WHERE Id = @Id;";
            command.Parameters.AddWithValue("@Id", lateOperationId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Equal(legacyPath, reader.GetString(1));
        }
    }

    private void CreateLegacyDatabase(string transactionId, string operationId, string legacyPath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Transactions (Id TEXT PRIMARY KEY, State INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
            CREATE TABLE Operations (
                Id TEXT PRIMARY KEY, TransactionId TEXT NOT NULL, Success INTEGER NOT NULL,
                Message TEXT NOT NULL, OriginalPath TEXT, BackupPath TEXT, Timestamp TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO Transactions (Id, State, CreatedAt) VALUES (@Id, 0, @CreatedAt);
            INSERT INTO Operations (Id, TransactionId, Success, Message, OriginalPath, BackupPath, Timestamp)
            VALUES (@OperationId, @Id, 1, 'success', @OriginalPath, @BackupPath, @CreatedAt);
            """;
        command.Parameters.AddWithValue("@Id", transactionId);
        command.Parameters.AddWithValue("@OperationId", operationId);
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@OriginalPath", Path.Combine(_directory, "file.txt"));
        command.Parameters.AddWithValue("@BackupPath", legacyPath);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Database
        }.ToString());
        connection.Open();
        return connection;
    }

    private long Scalar(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
