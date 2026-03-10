namespace OpenCleaner.Contracts;

public sealed record OperationResult(
    bool Success,
    string Message,
    Guid TransactionId,
    FileEntry? OriginalFile = null,
    string? BackupPath = null);
