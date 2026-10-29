namespace OpenCleaner.Core.Transactions;

public sealed class CleanupTransactionScope : IAsyncDisposable
{
    private static readonly AsyncLocal<CleanupTransactionScope?> Current = new();
    private readonly ITransactionManager _manager;
    private readonly Guid _transactionId;
    private bool _completed;
    private readonly bool _createBackup;

    public static Guid? CurrentTransactionId => Current.Value is { _completed: false } scope ? scope._transactionId : null;
    public static bool CreateBackup => Current.Value is not { _completed: false } scope || scope._createBackup;

    public CleanupTransactionScope(ITransactionManager manager, bool createBackup = true)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (CurrentTransactionId != null)
            throw new InvalidOperationException("A cleanup transaction is already active");
        _manager = manager;
        _createBackup = createBackup;
        _transactionId = manager.BeginTransaction(createBackup);
        Current.Value = this;
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
