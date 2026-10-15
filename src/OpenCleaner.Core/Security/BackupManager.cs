using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

public sealed class BackupManager : IBackupManager, IDisposable
{
    private const int CopyBufferSize = 64 * 1024;
    private readonly ILogger<BackupManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _backupsRoot;
    public string BackupsRoot => _backupsRoot;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupManager(ILogger<BackupManager> logger) : this(logger, null) { }

    public BackupManager(ILogger<BackupManager> logger, string? backupsRootOverride)
    {
        _logger = logger;
        _backupsRoot = PathSafety.Normalize(backupsRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCleaner", "Backups"));
    }

    public async Task<BackupInfo> CreateBackupAsync(string originalPath, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await CreateBackupCoreAsync(originalPath, ct); }
        finally { _gate.Release(); }
    }

    private async Task<BackupInfo> CreateBackupCoreAsync(string originalPath, CancellationToken ct)
    {
        originalPath = PathSafety.Normalize(originalPath);
        if (PathSafety.HasReparsePoint(originalPath) || PathSafety.HasReparsePoint(_backupsRoot))
            throw new IOException("Backup paths cannot contain symbolic links or junctions");
        ct.ThrowIfCancellationRequested();

        string backupId = Guid.NewGuid().ToString("N");
        string backupDirectory = Path.Combine(_backupsRoot, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), backupId);
        string dataDirectory = Path.Combine(backupDirectory, "data");
        string fileName = Path.GetFileName(originalPath);
        string backupFilePath = Path.Combine(dataDirectory, fileName);
        Directory.CreateDirectory(dataDirectory);
        try
        {
            // A single stable source handle prevents writes/deletion during the copy. Hash the
            // saved bytes in-stream while copying, avoiding a second read pass over the backup file.
            long fileSize = 0;
            string contentHash;
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
                try
                {
                    await using var source = new FileStream(originalPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var destination = new FileStream(backupFilePath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                        hasher.AppendData(buffer, 0, read);
                        fileSize += read;
                    }
                    await destination.FlushAsync(ct);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                contentHash = Convert.ToHexString(hasher.GetHashAndReset());
            }

            var metadata = new BackupMetadata
            {
                OriginalPath = originalPath,
                OriginalHash = contentHash,
                CreationDate = DateTime.UtcNow,
                FileSize = fileSize,
                FileName = fileName
            };
            await File.WriteAllTextAsync(Path.Combine(backupDirectory, "metadata.json"),
                JsonSerializer.Serialize(metadata, JsonOptions), ct);
            _logger.LogInformation("Backup created: {BackupId} for {OriginalPath}", backupId, originalPath);
            return ToInfo(backupId, metadata, backupFilePath);
        }
        catch
        {
            try { DeleteValidatedDirectory(backupDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(ex, "Could not remove incomplete backup {BackupId}", backupId); }
            throw;
        }
    }

    public async Task<bool> RestoreAsync(string backupId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        string? stagedFile = null;
        try
        {
            string? directory = ResolveBackupDirectory(backupId);
            if (directory == null) return false;
            BackupMetadata metadata = ReadMetadata(directory);
            string originalPath = PathSafety.Normalize(metadata.OriginalPath);
            if (PathSafety.IsCritical(originalPath)) return false;
            string backupFile = GetBackupFile(directory, metadata);

            await using var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous);
            string hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(source, ct));
            if (source.Length != metadata.FileSize || !hash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Corrupt backup {BackupId}: restore aborted", backupId);
                return false;
            }
            source.Position = 0;
            string originalDirectory = Path.GetDirectoryName(originalPath)!;
            Directory.CreateDirectory(originalDirectory);
            stagedFile = Path.Combine(originalDirectory, $".opencleaner-restore-{Guid.NewGuid():N}.tmp");
            await using (var destination = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
            }

            // Preserve an existing newer file before replacing it. Cancellation or a failed copy
            // leaves the current file in place, and replacement happens only after verification.
            if (File.Exists(originalPath)) await CreateBackupCoreAsync(originalPath, ct);
            ct.ThrowIfCancellationRequested();
            if (PathSafety.IsCritical(originalPath) || PathSafety.HasReparsePoint(stagedFile)) return false;
            File.Move(stagedFile, originalPath, overwrite: true);
            stagedFile = null;
            _logger.LogInformation("Restored backup {BackupId} to {OriginalPath}", backupId, originalPath);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to restore backup {BackupId}", backupId);
            return false;
        }
        finally
        {
            if (stagedFile != null)
            {
                try { File.Delete(stagedFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _logger.LogWarning(ex, "Could not remove staged restore file"); }
            }
            _gate.Release();
        }
    }

    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            string? directory = ResolveBackupDirectory(backupId);
            if (directory == null) return false;
            ct.ThrowIfCancellationRequested();
            DeleteValidatedDirectory(directory);
            string dateDirectory = Path.GetDirectoryName(directory)!;
            if (!Directory.EnumerateFileSystemEntries(dateDirectory).Any()) Directory.Delete(dateDirectory);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { _logger.LogWarning(ex, "Failed to delete backup {BackupId}", backupId); return false; }
        finally { _gate.Release(); }
    }

    public async Task CleanupOldBackupsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        await _gate.WaitAsync(ct);
        try
        {
            DateTime cutoff = DateTime.UtcNow - maxAge;
            foreach (string directory in EnumerateBackupDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (ReadMetadata(directory).CreationDate.ToUniversalTime() < cutoff)
                        DeleteValidatedDirectory(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { _logger.LogWarning(ex, "Could not clean old backup {Directory}", directory); }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BackupInfo>> GetAllBackupsAsync()
    {
        if (!Directory.Exists(_backupsRoot))
            return [];

        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var backups = new List<BackupInfo>();
                foreach (string directory in EnumerateBackupDirectories())
                {
                    try
                    {
                        BackupMetadata metadata = ReadMetadata(directory);
                        backups.Add(ToInfo(Path.GetFileName(directory), metadata, GetBackupFile(directory, metadata)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                    { _logger.LogWarning(ex, "Could not read backup {Directory}", directory); }
                }
                backups.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                return (IReadOnlyList<BackupInfo>)backups;
            });
        }
        finally
        {
            try { _gate.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    private IEnumerable<string> EnumerateBackupDirectories()
    {
        if (!Directory.Exists(_backupsRoot) || PathSafety.HasReparsePoint(_backupsRoot)) yield break;
        foreach (string dateDirectory in Directory.EnumerateDirectories(_backupsRoot))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dateDirectory), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) || PathSafety.HasReparsePoint(dateDirectory)) continue;
            foreach (string directory in Directory.EnumerateDirectories(dateDirectory))
            {
                if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _) && !PathSafety.HasReparsePoint(directory))
                    yield return directory;
            }
        }
    }

    private string? ResolveBackupDirectory(string backupId)
    {
        if (!Guid.TryParseExact(backupId, "N", out Guid id) && !Guid.TryParseExact(backupId, "D", out id)) return null;
        string normalizedId = id.ToString("N");
        return EnumerateBackupDirectories().FirstOrDefault(directory =>
            Path.GetFileName(directory).Equals(normalizedId, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(directory, "metadata.json")));
    }

    private static BackupMetadata ReadMetadata(string directory)
    {
        string metadataPath = Path.Combine(directory, "metadata.json");
        if (PathSafety.HasReparsePoint(metadataPath)) throw new IOException("Linked backup metadata blocked");
        BackupMetadata metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath))
            ?? throw new JsonException("Missing backup metadata");
        PathSafety.Normalize(metadata.OriginalPath);
        if (metadata.FileSize < 0 || string.IsNullOrEmpty(metadata.OriginalHash) || metadata.OriginalHash.Length != 64 ||
            !metadata.OriginalHash.All(Uri.IsHexDigit)) throw new JsonException("Invalid backup metadata");
        return metadata;
    }

    private static string GetBackupFile(string directory, BackupMetadata metadata)
    {
        string fileName = metadata.FileName ?? Path.GetFileName(metadata.OriginalPath);
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new JsonException("Invalid backup file name");
        string file = Path.Combine(directory, "data", fileName);
        if (PathSafety.HasReparsePoint(file) || !File.Exists(file)) throw new IOException("Missing or linked backup data");
        return file;
    }

    private void DeleteValidatedDirectory(string directory)
    {
        string normalized = PathSafety.Normalize(directory);
        if (!PathSafety.IsWithin(normalized, _backupsRoot) || normalized.Equals(_backupsRoot, StringComparison.OrdinalIgnoreCase) ||
            PathSafety.HasReparsePoint(normalized)) throw new IOException("Unsafe backup deletion blocked");
        if (!Directory.Exists(normalized)) return;
        // Inspect each entry before descent. No recursive enumeration follows a junction.
        var pending = new Stack<string>();
        pending.Push(normalized);
        while (pending.TryPop(out string? current))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                FileAttributes attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup entry blocked");
                if ((attrs & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        Directory.Delete(normalized, recursive: true);
    }

    private static BackupInfo ToInfo(string id, BackupMetadata metadata, string file) =>
        new(id, metadata.OriginalPath, file, metadata.CreationDate, metadata.FileSize, metadata.OriginalHash);

    public void Dispose() => _gate.Dispose();

    private sealed class BackupMetadata
    {
        public required string OriginalPath { get; init; }
        public required string OriginalHash { get; init; }
        public required DateTime CreationDate { get; init; }
        public required long FileSize { get; init; }
        public string? FileName { get; init; }
    }
}
