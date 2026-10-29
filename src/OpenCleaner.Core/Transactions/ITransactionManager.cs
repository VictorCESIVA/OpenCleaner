using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Transactions;

public interface ITransactionManager
{
    Guid BeginTransaction(bool createBackup = true);
    Task StageOperationAsync(Guid transactionId, OperationResult operation);
    Task CommitAsync(Guid transactionId, CancellationToken ct = default);
    Task CommitPartialAsync(Guid transactionId, CancellationToken ct = default);
    Task RollbackAsync(Guid transactionId, CancellationToken ct = default);
    Task<TransactionState> GetTransactionStateAsync(Guid transactionId);
    Task<IReadOnlyList<OperationResult>> GetTransactionHistoryAsync(Guid transactionId);
    Task<int> PurgeOldTransactionsAsync(TimeSpan maxAge, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetUnfinishedTransactionsAsync();
}

public enum TransactionState { Pending, Committed, RolledBack, Failed }

public class Transaction
{
    public Guid Id { get; }
    public TransactionState State { get; private set; }
    public DateTime CreatedAt { get; }
    public bool CreateBackup { get; }
    public List<OperationResult> Operations { get; } = [];

    public Transaction(bool createBackup = true)
        : this(Guid.NewGuid(), TransactionState.Pending, DateTime.UtcNow, createBackup)
    {
    }

    internal Transaction(Guid id, TransactionState state, DateTime createdAt, bool createBackup = true)
    {
        Id = id;
        State = state;
        CreatedAt = createdAt;
        CreateBackup = createBackup;
    }

    public void MarkCommitted() => State = TransactionState.Committed;
    public void MarkRolledBack() => State = TransactionState.RolledBack;
    public void MarkFailed() => State = TransactionState.Failed;
}
