namespace OpenCleaner.Contracts;

public interface IBackupManager
{
    Task<BackupInfo> CreateBackupAsync(string originalPath, CancellationToken ct = default);
    Task<bool> RestoreAsync(string backupId, CancellationToken ct = default);
    Task<bool> RestoreIfMissingAsync(string backupId, CancellationToken ct = default);
    Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default);
    Task CleanupOldBackupsAsync(TimeSpan maxAge, CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> GetAllBackupsAsync();
    Task<BackupUpgradeResult> UpgradeLegacyBackupsAsync(CancellationToken ct = default);
    Task<bool> VerifyBackupAsync(BackupInfo backup, CancellationToken ct = default);
    bool IsBackupKeyAvailable { get; }
    bool HasRecoveryCode { get; }
    string QuarantineRoot { get; }
    Task<string?> EnsureRecoveryCodeAsync(CancellationToken ct = default);
    Task<string> RegenerateRecoveryCodeAsync(CancellationToken ct = default);
    Task<bool> RecoverKeyAsync(string recoveryCode, CancellationToken ct = default);
}

public sealed record BackupUpgradeResult(int Migrated, int Quarantined);

public record BackupInfo(
    string BackupId,
    string OriginalPath,
    string BackupPath,
    DateTime CreatedAt,
    long Size,
    string ContentHash
);
