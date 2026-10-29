using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Transactions;

namespace OpenCleaner.Core.Security;

public sealed class FileGuardian : IFileGuardian
{
    private readonly IBackupManager _backupManager;
    private readonly ILogger<FileGuardian> _logger;
    private readonly ITransactionManager? _transactions;

    public FileGuardian(IBackupManager backupManager, ILogger<FileGuardian> logger,
        ITransactionManager? transactions = null)
    {
        _backupManager = backupManager;
        _logger = logger;
        _transactions = transactions;
    }

    public bool IsSystemCriticalPath(string path) => CleaningExclusions.IsExcluded(path) ||
        PathSafety.IsCritical(path) ||
        (_backupManager is BackupManager manager && PathSafety.IsWithin(PathSafety.Normalize(path), manager.BackupsRoot));

    public bool IsSystemCriticalPathForScan(string path)
    {
        if (CleaningExclusions.IsExcluded(path)) return true;
        if (PathSafety.IsCritical(path, checkReparse: false)) return true;
        if (_backupManager is not BackupManager manager) return false;
        try { return PathSafety.IsWithin(PathSafety.Normalize(path), manager.BackupsRoot); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return true; }
    }

    public bool IsFileLocked(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return true; }
    }

    public async Task<OperationResult> SafeDeleteAsync(string path, bool createBackup = true, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        createBackup = createBackup && CleanupTransactionScope.CreateBackup;
        Guid transactionId = Guid.NewGuid();
        FileEntry? originalFile = null;
        string? backupPath = null;
        OperationResult Failure(string message) => new(false, message, transactionId, originalFile, backupPath, 0, 0);

        if (CleaningExclusions.IsExcluded(path)) return Failure("Path excluded by user policy");
        if (IsSystemCriticalPath(path)) return Failure("System critical or unsafe path blocked");
        path = PathSafety.Normalize(path);

        try
        {
            if (!File.Exists(path)) return Failure("File not found");
            if (IsFileLocked(path)) return Failure("File is locked or inaccessible");

            var fileInfo = new FileInfo(path);
            string contentHash = await HashHelper.ComputeHashAsync(path, ct);
            originalFile = new FileEntry(path, fileInfo.Length, fileInfo.LastAccessTime, contentHash);

            if (createBackup)
            {
                BackupInfo backup;
                try { backup = await _backupManager.CreateBackupAsync(path, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup failed for {Path}, aborting delete", path);
                    return Failure("Backup failed, aborting delete");
                }
                backupPath = backup.BackupPath;
                var backupInfo = new FileInfo(backup.BackupPath);
                bool verified = backup.Size == originalFile.Size &&
                    string.Equals(backup.ContentHash, originalFile.ContentHash, StringComparison.OrdinalIgnoreCase) &&
                    backupInfo.Exists;
                if (verified)
                {
                    verified = _backupManager is BackupManager manager
                        ? await manager.VerifyBackupAsync(backup, ct)
                        : backupInfo.Length == originalFile.Size &&
                          string.Equals(await HashHelper.ComputeHashAsync(backup.BackupPath, ct),
                              originalFile.ContentHash, StringComparison.OrdinalIgnoreCase);
                }
                if (!verified) return Failure("Backup verification failed, aborting delete");
            }

            ct.ThrowIfCancellationRequested();
            if (IsSystemCriticalPath(path)) return Failure("Path became unsafe before delete");

            await using var deletion = new FileDeletionHandle(path);
            if (!PathSafety.IsSamePath(deletion.ResolvedPath, path))
                return Failure("Resolved file target changed, aborting delete");
            string currentHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(deletion.Stream, ct));
            if (deletion.Stream.Length != originalFile.Size ||
                !string.Equals(currentHash, originalFile.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Failure("File changed before delete, aborting delete");

            ct.ThrowIfCancellationRequested();
            if (IsSystemCriticalPath(path)) return Failure("Path became unsafe before delete");
            var result = new OperationResult(true, "Deleted successfully", transactionId, originalFile, backupPath,
                ItemsProcessed: 1, BytesRecovered: backupPath == null ? originalFile.Size : 0);
            await StageAsync(result);
            deletion.Delete();
            _logger.LogInformation("File deleted successfully: {Path}", path);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Delete failed for {Path}", path);
            return Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> SafeCleanDirectoryAsync(string dir, string searchPattern = "*", bool recursive = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Guid transactionId = Guid.NewGuid();
        if (CleaningExclusions.IsExcluded(dir))
            return new(false, "Directory excluded by user policy", transactionId, ItemsProcessed: 0, BytesRecovered: 0);
        if (IsSystemCriticalPath(dir)) return new(false, "System critical or unsafe directory blocked", transactionId, ItemsProcessed: 0, BytesRecovered: 0);
        if (!Directory.Exists(dir)) return new(false, "Directory not found", transactionId, ItemsProcessed: 0, BytesRecovered: 0);

        int cleaned = 0;
        int failures = 0;
        long recovered = 0;
        try
        {
            foreach (string file in EnumerateSafeFiles(dir, searchPattern, recursive, ct))
            {
                ct.ThrowIfCancellationRequested();
                OperationResult result = await SafeDeleteAsync(file, createBackup: true, ct);
                if (result.Success)
                {
                    cleaned += result.ItemsProcessed ?? 0;
                    recovered += result.BytesRecovered ?? 0;
                }
                else failures++;
            }
            return new(failures == 0, $"Cleaned {cleaned} files; {failures} failures", transactionId,
                ItemsProcessed: cleaned, BytesRecovered: recovered);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Directory cleaning failed for {Directory}", dir);
            return new(false, $"Directory cleaning failed: {ex.Message}", transactionId,
                ItemsProcessed: cleaned, BytesRecovered: recovered);
        }
    }

    private async Task StageAsync(OperationResult result)
    {
        if (_transactions == null ||
            CleanupTransactionScope.CurrentTransactionId is not Guid transactionId) return;
        await _transactions.StageOperationAsync(transactionId, result);
    }

    private IEnumerable<string> EnumerateSafeFiles(string directory, string pattern, bool recursive, CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            ct.ThrowIfCancellationRequested();
            if (IsSystemCriticalPath(current)) continue;
            foreach (string file in Directory.EnumerateFiles(current, pattern, options)) yield return file;
            if (!recursive) continue;
            foreach (string child in Directory.EnumerateDirectories(current, "*", options))
                if (!IsSystemCriticalPath(child)) pending.Push(child);
        }
    }
}
