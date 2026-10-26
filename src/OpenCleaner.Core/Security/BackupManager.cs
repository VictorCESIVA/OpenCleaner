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
    private readonly BackupKeyStore _keys;
    public string BackupsRoot => _backupsRoot;
    public string QuarantineRoot => Path.Combine(_backupsRoot, "Quarantine");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupManager(ILogger<BackupManager> logger) : this(logger, null, null) { }

    public BackupManager(ILogger<BackupManager> logger, string? backupsRootOverride)
        : this(logger, backupsRootOverride, null) { }

    public BackupManager(ILogger<BackupManager> logger, string? backupsRootOverride, string? keyPathOverride)
    {
        _logger = logger;
        _backupsRoot = PathSafety.Normalize(backupsRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCleaner", "Backups"));
        string defaultKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCleaner", "keys", "backup.key");
        _keys = new BackupKeyStore(
            keyPathOverride ?? (backupsRootOverride == null ? defaultKeyPath : Path.Combine(_backupsRoot, ".key", "backup.key")));
    }

    private byte[] Key
    {
        get
        {
            if (_keys.IsKeyAvailable) return _keys.GetKey(allowCreate: false);
            if (_keys.HasRecoveryEnvelope || HasEncryptedBackups())
                throw new BackupKeyUnavailableException("The backup key is missing; use the recovery code");
            return _keys.GetKey(allowCreate: true);
        }
    }

    public bool IsBackupKeyAvailable => _keys.IsKeyAvailable;
    public bool HasRecoveryCode => _keys.HasRecoveryEnvelope;

    public Task<string?> EnsureRecoveryCodeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_keys.IsKeyAvailable && (_keys.HasRecoveryEnvelope || HasEncryptedBackups()))
            throw new BackupKeyUnavailableException("The backup key is missing; use the recovery code");
        return Task.Run(() => _keys.EnsureRecoveryCode(createKeyIfMissing: true), ct);
    }

    public Task<string> RegenerateRecoveryCodeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_keys.IsKeyAvailable)
            throw new BackupKeyUnavailableException("The backup key is missing; use the recovery code");
        return Task.Run(_keys.RegenerateRecoveryCode, ct);
    }

    public async Task<bool> RecoverKeyAsync(string recoveryCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryCode)) return false;
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() => _keys.RecoverKey(recoveryCode), ct);
        }
        finally { _gate.Release(); }
    }

    private bool HasEncryptedBackups()
    {
        try
        {
            foreach (string directory in EnumerateBackupDirectories())
            {
                string data = Path.Combine(directory, "data");
                if (Directory.Exists(data) &&
                    Directory.EnumerateFiles(data, "*.ocb", SearchOption.TopDirectoryOnly).Any())
                    return true;
                string metadataPath = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    if (document.RootElement.TryGetProperty("Encrypted", out var encrypted) &&
                        encrypted.ValueKind == JsonValueKind.True)
                        return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return true; }
        return false;
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
        byte[] key = Key;

        string backupId = Guid.NewGuid().ToString("N");
        string backupDirectory = Path.Combine(_backupsRoot, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), backupId);
        string dataDirectory = Path.Combine(backupDirectory, "data");
        string fileName = Path.GetFileName(originalPath);
        string backupFilePath = Path.Combine(dataDirectory, fileName + ".ocb");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            if (PathSafety.HasReparsePoint(backupDirectory))
                throw new IOException("Backup directory contains a reparse point");

            await using var source = new FileStream(originalPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(backupFilePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
            (long fileSize, string contentHash) = await BackupCipher.EncryptAsync(key, source, destination, ct);

            var metadata = new BackupMetadata
            {
                OriginalPath = originalPath,
                OriginalHash = contentHash,
                CreationDate = DateTime.UtcNow,
                FileSize = fileSize,
                FileName = fileName,
                Encrypted = true
            };
            metadata = metadata with
            {
                Signature = BackupCipher.ComputeMetadataSignature(key, metadata.OriginalPath, metadata.OriginalHash,
                    metadata.CreationDate, metadata.FileSize, metadata.FileName, metadata.Encrypted)
            };
            await WriteMetadataAsync(backupDirectory, metadata, ct);
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
            if (directory == null)
            {
                _logger.LogWarning("Backup {BackupId} not found", backupId);
                return false;
            }
            BackupMetadata metadata = ReadMetadata(directory);
            string originalPath = PathSafety.Normalize(metadata.OriginalPath);
            if (PathSafety.IsCritical(originalPath) || PathSafety.IsWithin(originalPath, _backupsRoot))
            {
                _logger.LogWarning("Restore target for {BackupId} is protected: {Path}", backupId, originalPath);
                return false;
            }
            string backupFile = GetBackupFile(directory, metadata);
            string originalDirectory = Path.GetDirectoryName(originalPath)!;
            Directory.CreateDirectory(originalDirectory);
            if (PathSafety.HasReparsePoint(originalDirectory))
            {
                _logger.LogWarning("Restore directory for {BackupId} contains a reparse point: {Path}", backupId, originalDirectory);
                return false;
            }
            stagedFile = Path.Combine(originalDirectory, $".opencleaner-restore-{Guid.NewGuid():N}.tmp");

            if (metadata.Encrypted)
            {
                await using var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous);
                await using var destination = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous);
                (long fileSize, string hash) = await BackupCipher.DecryptAsync(Key, source, destination, ct);
                if (fileSize != metadata.FileSize ||
                    !hash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Corrupt backup {BackupId}: restore aborted", backupId);
                    return false;
                }
            }
            else
            {
                await using var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous);
                string hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(source, ct));
                if (source.Length != metadata.FileSize ||
                    !hash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Corrupt backup {BackupId}: restore aborted", backupId);
                    return false;
                }
                source.Position = 0;
                await using var destination = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous);
                await source.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
            }

            await using (var staged = new FileStream(stagedFile, FileMode.Open, FileAccess.Read,
                             FileShare.None, 65536, FileOptions.Asynchronous))
            {
                string stagedHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(staged, ct));
                if (staged.Length != metadata.FileSize ||
                    !stagedHash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Staged restore for {BackupId} failed verification", backupId);
                    return false;
                }
            }

            // Preserve an existing newer file before replacing it. Cancellation or a failed copy
            // leaves the current file in place, and replacement happens only after verification.
            if (File.Exists(originalPath)) await CreateBackupCoreAsync(originalPath, ct);
            ct.ThrowIfCancellationRequested();
            if (PathSafety.IsCritical(originalPath) || PathSafety.HasReparsePoint(originalDirectory) ||
                PathSafety.HasReparsePoint(stagedFile))
            {
                _logger.LogWarning("Restore target for {BackupId} became unsafe: {Path}", backupId, originalPath);
                return false;
            }
            File.Move(stagedFile, originalPath, overwrite: true);
            stagedFile = null;
            _logger.LogInformation("Restored backup {BackupId} to {OriginalPath}", backupId, originalPath);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or CryptographicException or InvalidDataException)
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                    ArgumentException or CryptographicException or InvalidDataException)
                { _logger.LogWarning(ex, "Could not clean old backup {Directory}", directory); }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> VerifyBackupAsync(BackupInfo backup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        await _gate.WaitAsync(ct);
        try
        {
            string? directory = ResolveBackupDirectory(backup.BackupId);
            if (directory == null) return false;
            BackupMetadata metadata = ReadMetadata(directory);
            if (!metadata.OriginalPath.Equals(backup.OriginalPath, StringComparison.OrdinalIgnoreCase) ||
                metadata.FileSize != backup.Size ||
                !metadata.OriginalHash.Equals(backup.ContentHash, StringComparison.OrdinalIgnoreCase))
                return false;

            string backupFile = GetBackupFile(directory, metadata);
            await using var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous);
            if (metadata.Encrypted)
            {
                (long size, string hash) = await BackupCipher.DecryptAsync(Key, source, Stream.Null, ct);
                return size == metadata.FileSize &&
                    hash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase);
            }
            string plainHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(source, ct));
            return source.Length == metadata.FileSize &&
                plainHash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or CryptographicException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Could not verify backup {BackupId}", backup.BackupId);
            return false;
        }
        finally { _gate.Release(); }
    }

    public async Task<BackupUpgradeResult> UpgradeLegacyBackupsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        int migrated = 0;
        int quarantined = 0;
        try
        {
            foreach (string directory in EnumerateBackupDirectories().ToList())
            {
                ct.ThrowIfCancellationRequested();
                string? encryptedPayload = null;
                string? metadataTemporary = null;
                try
                {
                    BackupMetadata metadata = ReadMetadata(directory);
                    string legacyPayload = GetBackupFilePath(directory, metadata, encrypted: false);
                    if (metadata.Encrypted)
                    {
                        string currentPayload = GetBackupFilePath(directory, metadata, encrypted: true);
                        if (!File.Exists(currentPayload) || PathSafety.HasReparsePoint(currentPayload))
                        {
                            if (Quarantine(directory, "Missing or linked encrypted payload")) quarantined++;
                            continue;
                        }
                        if (PathSafety.HasReparsePoint(legacyPayload)) continue;
                        if (File.Exists(legacyPayload)) File.Delete(legacyPayload);
                        continue;
                    }

                    if (!File.Exists(legacyPayload) || PathSafety.HasReparsePoint(legacyPayload))
                    {
                        if (Quarantine(directory, "Missing or linked legacy payload")) quarantined++;
                        continue;
                    }
                    string hash = await HashHelper.ComputeHashAsync(legacyPayload, ct);
                    if (new FileInfo(legacyPayload).Length != metadata.FileSize ||
                        !hash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Quarantine(directory, "Legacy backup failed integrity verification")) quarantined++;
                        continue;
                    }

                    string encryptedPath = GetBackupFilePath(directory, metadata, encrypted: true);
                    if (File.Exists(encryptedPath) && PathSafety.HasReparsePoint(encryptedPath))
                    {
                        if (Quarantine(directory, "Linked encrypted payload")) quarantined++;
                        continue;
                    }
                    encryptedPayload = encryptedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await using (var source = new FileStream(legacyPayload, FileMode.Open, FileAccess.Read,
                                     FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var destination = new FileStream(encryptedPayload, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, CopyBufferSize, FileOptions.Asynchronous))
                    {
                        (long size, string encryptedHash) = await BackupCipher.EncryptAsync(Key, source, destination, ct);
                        if (size != metadata.FileSize ||
                            !encryptedHash.Equals(metadata.OriginalHash, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(encryptedPayload);
                            encryptedPayload = null;
                            if (Quarantine(directory, "Legacy backup changed during migration")) quarantined++;
                            continue;
                        }
                    }

                    BackupMetadata upgraded = metadata with { Encrypted = true };
                    upgraded = upgraded with
                    {
                        Signature = BackupCipher.ComputeMetadataSignature(Key, upgraded.OriginalPath,
                            upgraded.OriginalHash, upgraded.CreationDate, upgraded.FileSize, upgraded.FileName,
                            upgraded.Encrypted)
                    };
                    metadataTemporary = Path.Combine(directory, "metadata.json." + Guid.NewGuid().ToString("N") + ".tmp");
                    await File.WriteAllTextAsync(metadataTemporary, JsonSerializer.Serialize(upgraded, JsonOptions), ct);
                    File.Move(encryptedPayload, encryptedPath, overwrite: true);
                    encryptedPayload = null;
                    File.Move(metadataTemporary, Path.Combine(directory, "metadata.json"), overwrite: true);
                    metadataTemporary = null;
                    File.Delete(legacyPayload);
                    migrated++;
                }
                catch (BackupKeyUnavailableException ex)
                {
                    _logger.LogWarning(ex, "Backup migration requires the recovery code for {Directory}", directory);
                }
                catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidDataException or
                    ArgumentException)
                {
                    if (Quarantine(directory, $"Invalid legacy backup metadata: {ex.Message}")) quarantined++;
                    else _logger.LogWarning(ex, "Could not quarantine invalid backup {Directory}", directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not migrate legacy backup {Directory}", directory);
                }
                finally
                {
                    if (encryptedPayload != null && File.Exists(encryptedPayload)) File.Delete(encryptedPayload);
                    if (metadataTemporary != null && File.Exists(metadataTemporary)) File.Delete(metadataTemporary);
                }
            }
        }
        finally { _gate.Release(); }
        return new BackupUpgradeResult(migrated, quarantined);
    }

    private bool Quarantine(string directory, string reason)
    {
        try
        {
            string source = PathSafety.Normalize(directory);
            if (!PathSafety.IsWithin(source, _backupsRoot) ||
                source.Equals(_backupsRoot, StringComparison.OrdinalIgnoreCase) ||
                PathSafety.HasReparsePoint(source)) return false;
            string root = QuarantineRoot;
            Directory.CreateDirectory(root);
            if (PathSafety.HasReparsePoint(root)) return false;
            string destination = Path.Combine(root, Path.GetFileName(source));
            if (Directory.Exists(destination))
                destination += "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            Directory.Move(source, destination);
            try
            {
                var marker = new { Reason = reason, QuarantinedAt = DateTime.UtcNow };
                File.WriteAllText(Path.Combine(destination, "quarantine.json"),
                    JsonSerializer.Serialize(marker, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(ex, "Could not write quarantine marker for {Directory}", destination); }
            _logger.LogWarning("Backup {Directory} quarantined: {Reason}", source, reason);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not quarantine backup {Directory}", directory);
            return false;
        }
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
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                        ArgumentException or CryptographicException or InvalidDataException)
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

    private BackupMetadata ReadMetadata(string directory)
    {
        string metadataPath = Path.Combine(directory, "metadata.json");
        if (PathSafety.HasReparsePoint(metadataPath)) throw new IOException("Linked backup metadata blocked");
        BackupMetadata metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath))
            ?? throw new JsonException("Missing backup metadata");
        PathSafety.Normalize(metadata.OriginalPath);
        if (metadata.FileSize < 0 || string.IsNullOrEmpty(metadata.OriginalHash) || metadata.OriginalHash.Length != 64 ||
            !metadata.OriginalHash.All(Uri.IsHexDigit)) throw new JsonException("Invalid backup metadata");

        if (!string.IsNullOrEmpty(metadata.Signature))
        {
            byte[] signature;
            try { signature = Convert.FromBase64String(metadata.Signature); }
            catch (FormatException ex) { throw new JsonException("Invalid backup metadata signature", ex); }
            string expected = BackupCipher.ComputeMetadataSignature(Key, metadata.OriginalPath, metadata.OriginalHash,
                metadata.CreationDate, metadata.FileSize, metadata.FileName, metadata.Encrypted);
            if (signature.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(signature, Convert.FromBase64String(expected)))
                throw new CryptographicException("Backup metadata signature is invalid");
        }
        else if (metadata.Encrypted)
        {
            throw new CryptographicException("Encrypted backup metadata is not signed");
        }
        return metadata;
    }

    private static string GetBackupFile(string directory, BackupMetadata metadata)
    {
        string file = GetBackupFilePath(directory, metadata, metadata.Encrypted);
        if (PathSafety.HasReparsePoint(file) || !File.Exists(file)) throw new IOException("Missing or linked backup data");
        return file;
    }

    private static string GetBackupFilePath(string directory, BackupMetadata metadata, bool encrypted)
    {
        string fileName = metadata.FileName ?? Path.GetFileName(metadata.OriginalPath);
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new JsonException("Invalid backup file name");
        if (encrypted) fileName += ".ocb";
        return Path.Combine(directory, "data", fileName);
    }

    private async Task WriteMetadataAsync(string directory, BackupMetadata metadata, CancellationToken ct)
    {
        string metadataPath = Path.Combine(directory, "metadata.json");
        string temporaryPath = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(metadata, JsonOptions), ct);
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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

    private sealed record BackupMetadata
    {
        public required string OriginalPath { get; init; }
        public required string OriginalHash { get; init; }
        public required DateTime CreationDate { get; init; }
        public required long FileSize { get; init; }
        public string? FileName { get; init; }
        public bool Encrypted { get; init; }
        public string? Signature { get; init; }
    }
}
