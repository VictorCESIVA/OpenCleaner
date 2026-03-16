using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

public sealed class BackupManager : IBackupManager, IDisposable
{
    private readonly ILogger<BackupManager> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new();
    private readonly string _backupsRoot;
    private readonly string _baseBackupDirectory;

    public BackupManager(ILogger<BackupManager> logger)
        : this(logger, null)
    {
    }

    public BackupManager(ILogger<BackupManager> logger, string? backupsRootOverride)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(backupsRootOverride))
        {
            _backupsRoot = backupsRootOverride;
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backupsRoot = Path.Combine(localAppData, "OpenCleaner", "Backups");
        }

        _baseBackupDirectory = Path.Combine(_backupsRoot, DateTime.Now.ToString("yyyy-MM-dd"));
    }

    public async Task<BackupInfo> CreateBackupAsync(string originalPath, CancellationToken ct = default)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original file not found", originalPath);
        }

        string fileKey = Path.GetFullPath(originalPath).ToLowerInvariant();
        SemaphoreSlim semaphore = _operationLocks.GetOrAdd(fileKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            string backupId = Guid.NewGuid().ToString("N");
            string backupDirectory = Path.Combine(_baseBackupDirectory, backupId);
            string dataDirectory = Path.Combine(backupDirectory, "data");
            string metadataPath = Path.Combine(backupDirectory, "metadata.json");

            Directory.CreateDirectory(dataDirectory);

            string fileName = Path.GetFileName(originalPath);
            string backupFilePath = Path.Combine(dataDirectory, fileName);

            File.Copy(originalPath, backupFilePath, overwrite: false);

            string contentHash = await HashHelper.ComputeHashAsync(originalPath, ct);
            long fileSize = new FileInfo(originalPath).Length;

            var metadata = new BackupMetadata
            {
                OriginalPath = originalPath,
                OriginalHash = contentHash,
                CreationDate = DateTime.UtcNow,
                FileSize = fileSize
            };

            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct);

            _logger.LogInformation("Backup created: {BackupId} for {OriginalPath}", backupId, originalPath);

            return new BackupInfo(
                BackupId: backupId,
                OriginalPath: originalPath,
                BackupPath: backupFilePath,
                CreatedAt: metadata.CreationDate,
                Size: fileSize,
                ContentHash: contentHash
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> RestoreAsync(string backupId, CancellationToken ct = default)
    {
        string? backupDirectory = ResolveBackupDirectory(backupId);
        if (backupDirectory == null)
        {
            _logger.LogWarning("Backup not found: {BackupId}", backupId);
            return false;
        }

        string metadataPath = Path.Combine(backupDirectory, "metadata.json");

        string metadataJson = await File.ReadAllTextAsync(metadataPath, ct);
        BackupMetadata? metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);

        if (metadata == null)
        {
            _logger.LogError("Failed to deserialize metadata for backup: {BackupId}", backupId);
            return false;
        }

        string dataDirectory = Path.Combine(backupDirectory, "data");
        if (!Directory.Exists(dataDirectory))
        {
            _logger.LogError("Backup data directory not found for: {BackupId}", backupId);
            return false;
        }

        string? backupFile = Directory.GetFiles(dataDirectory).FirstOrDefault();

        if (backupFile == null || !File.Exists(backupFile))
        {
            _logger.LogError("Backup data not found for: {BackupId}", backupId);
            return false;
        }

        string fileKey = Path.GetFullPath(metadata.OriginalPath).ToLowerInvariant();
        SemaphoreSlim semaphore = _operationLocks.GetOrAdd(fileKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            string? originalDirectory = Path.GetDirectoryName(metadata.OriginalPath);
            if (!string.IsNullOrEmpty(originalDirectory))
            {
                Directory.CreateDirectory(originalDirectory);
            }

            File.Copy(backupFile, metadata.OriginalPath, overwrite: true);
            _logger.LogInformation("Restored backup {BackupId} to {OriginalPath}", backupId, metadata.OriginalPath);

            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task CleanupOldBackupsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        if (!Directory.Exists(_backupsRoot))
        {
            return Task.CompletedTask;
        }

        DateTime cutoffDate = DateTime.Now - maxAge;

        foreach (string dateDirectory in Directory.GetDirectories(_backupsRoot))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (Directory.GetCreationTime(dateDirectory) < cutoffDate)
            {
                try
                {
                    Directory.Delete(dateDirectory, recursive: true);
                    _logger.LogInformation("Cleaned up old backup directory: {Directory}", dateDirectory);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete old backup directory: {Directory}", dateDirectory);
                }
            }
        }

        return Task.CompletedTask;
    }


    public Task<IReadOnlyList<BackupInfo>> GetAllBackupsAsync()
    {
        var backups = new List<BackupInfo>();
        if (!Directory.Exists(_backupsRoot))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>(backups);
        }

        foreach (string dateDirectory in Directory.GetDirectories(_backupsRoot))
        {
            foreach (string backupDirectory in Directory.GetDirectories(dateDirectory))
            {
                string metadataPath = Path.Combine(backupDirectory, "metadata.json");
                if (!File.Exists(metadataPath))
                {
                    continue;
                }

                try
                {
                    string metadataJson = File.ReadAllText(metadataPath);
                    BackupMetadata? metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);

                    if (metadata != null)
                    {
                        string backupId = Path.GetFileName(backupDirectory);
                        string dataDirectory = Path.Combine(backupDirectory, "data");
                        string? backupFile = Directory.GetFiles(dataDirectory).FirstOrDefault();

                        if (backupFile != null)
                        {
                            backups.Add(new BackupInfo(
                                BackupId: backupId,
                                OriginalPath: metadata.OriginalPath,
                                BackupPath: backupFile,
                                CreatedAt: metadata.CreationDate,
                                Size: metadata.FileSize,
                                ContentHash: metadata.OriginalHash
                            ));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read backup metadata from: {Directory}", backupDirectory);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<BackupInfo>>(backups);
    }

    private string? ResolveBackupDirectory(string backupId)
    {
        string todayPath = Path.Combine(_baseBackupDirectory, backupId);
        if (File.Exists(Path.Combine(todayPath, "metadata.json")))
        {
            return todayPath;
        }

        if (!Directory.Exists(_backupsRoot))
        {
            return null;
        }

        foreach (string dateDirectory in Directory.GetDirectories(_backupsRoot))
        {
            string candidate = Path.Combine(dateDirectory, backupId);
            if (File.Exists(Path.Combine(candidate, "metadata.json")))
            {
                return candidate;
            }
        }

        return null;
    }



    public void Dispose()
    {
        foreach (SemaphoreSlim semaphore in _operationLocks.Values)
        {
            semaphore.Dispose();
        }
        _operationLocks.Clear();
    }

    private sealed class BackupMetadata
    {
        public required string OriginalPath { get; init; }
        public required string OriginalHash { get; init; }
        public required DateTime CreationDate { get; init; }
        public required long FileSize { get; init; }
    }
}
