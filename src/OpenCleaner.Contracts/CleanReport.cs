namespace OpenCleaner.Contracts;

public sealed record CleanReport(
    Guid Id,
    DateTime StartTime,
    DateTime EndTime,
    IReadOnlyList<OperationResult> Operations,
    long TotalSpaceRecovered,
    int FilesProcessed);
