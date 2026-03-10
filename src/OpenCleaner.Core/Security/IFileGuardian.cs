using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

public interface IFileGuardian
{
    Task<OperationResult> SafeDeleteAsync(string path, bool createBackup = true, CancellationToken ct = default);
    Task<OperationResult> SafeCleanDirectoryAsync(string dir, string searchPattern = "*", bool recursive = false, CancellationToken ct = default);
    bool IsSystemCriticalPath(string path);
    bool IsFileLocked(string path);
}
