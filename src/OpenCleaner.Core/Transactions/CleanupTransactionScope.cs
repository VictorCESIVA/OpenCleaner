namespace OpenCleaner.Core.Transactions;

public sealed class CleanupTransactionScope : IAsyncDisposable
{
    private static readonly AsyncLocal<Guid?> Current = new();
    private readonly ITransactionManager _manager;
    private readonly Guid _transactionId;
    private bool _completed;

    public static Guid? CurrentTransactionId => Current.Value;

    public CleanupTransactionScope(ITransactionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (Current.Value != null)
            throw new InvalidOperationException("A cleanup transaction is already active");
        _manager = manager;
        _transactionId = manager.BeginTransaction();
        Current.Value = _transactionId;
    }

    public Guid TransactionId => _transactionId;

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_completed) throw new InvalidOperationException("The cleanup transaction is already completed");
        await _manager.CommitPartialAsync(_transactionId, ct);
        _completed = true;
        Current.Value = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_completed) throw new InvalidOperationException("The cleanup transaction is already completed");
        await _manager.RollbackAsync(_transactionId, ct);
        _completed = true;
        Current.Value = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_completed) return;
        try { await _manager.RollbackAsync(_transactionId); }
        finally
        {
            _completed = true;
            Current.Value = null;
        }
    }
}
