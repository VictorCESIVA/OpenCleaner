using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Transactions;

public interface ITransactionManager
{
    Guid BeginTransaction();
    Task StageOperationAsync(Guid transactionId, OperationResult operation);
    Task CommitAsync(Guid transactionId, CancellationToken ct = default);
    Task RollbackAsync(Guid transactionId, CancellationToken ct = default);
    Task<TransactionState> GetTransactionStateAsync(Guid transactionId);
    Task<IReadOnlyList<OperationResult>> GetTransactionHistoryAsync(Guid transactionId);
}

public enum TransactionState { Pending, Committed, RolledBack, Failed }

public class Transaction
{
    public Guid Id { get; }
    public TransactionState State { get; private set; }
    public DateTime CreatedAt { get; }
    public List<OperationResult> Operations { get; } = [];

    public Transaction()
    {
        Id = Guid.NewGuid();
        State = TransactionState.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkCommitted() => State = TransactionState.Committed;
    public void MarkRolledBack() => State = TransactionState.RolledBack;
    public void MarkFailed() => State = TransactionState.Failed;
}
