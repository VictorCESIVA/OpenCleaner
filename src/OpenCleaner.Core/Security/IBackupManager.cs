namespace OpenCleaner.Core.Security;

public interface IBackupManager
{
    Task<BackupInfo> CreateBackupAsync(string originalPath, CancellationToken ct = default);
    Task<bool> RestoreAsync(string backupId, CancellationToken ct = default);
    Task CleanupOldBackupsAsync(TimeSpan maxAge, CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> GetExistingBackupsAsync();
}

public record BackupInfo(
    string BackupId,
    string OriginalPath,
    string BackupPath,
    DateTime CreatedAt,
    long Size,
    string ContentHash
);
