using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

public interface IRegistryGuardian
{
    Task<OperationResult> SafeDeleteKeyAsync(string keyPath, bool backupFirst = true, CancellationToken ct = default);
    Task<OperationResult> SafeDeleteValueAsync(string keyPath, string valueName, bool backupFirst = true, CancellationToken ct = default);
    Task<string> ExportKeyAsync(string keyPath, string backupDirectory, CancellationToken ct = default);
    bool IsSystemCriticalKey(string keyPath);
}
