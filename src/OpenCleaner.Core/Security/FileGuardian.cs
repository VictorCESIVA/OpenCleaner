using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

public sealed class FileGuardian : IFileGuardian
{
    private readonly IBackupManager _backupManager;
    private readonly ILogger<FileGuardian> _logger;

    private static readonly HashSet<string> SystemPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows\System32",
        @"C:\Windows\SysWOW64",
        @"C:\Program Files",
        @"C:\ProgramData\Microsoft",
        @"C:\$Recycle.Bin",
        @"C:\Boot",
        @"C:\Recovery"
    };

    private static readonly HashSet<string> SystemFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        "ntuser.dat"
    };

    public FileGuardian(IBackupManager backupManager, ILogger<FileGuardian> logger)
    {
        _backupManager = backupManager;
        _logger = logger;
    }

    public bool IsSystemCriticalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string normalizedPath = Path.GetFullPath(path).ToLowerInvariant();

        foreach (string systemPath in SystemPaths)
        {
            if (normalizedPath.StartsWith(systemPath.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        string fileName = Path.GetFileName(normalizedPath);
        if (SystemFileNames.Contains(fileName))
        {
            return true;
        }

        try
        {
            if (File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.System) == FileAttributes.System &&
                    (attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check attributes for path: {Path}", path);
        }

        return false;
    }

    public bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        FileStream? stream = null;
        try
        {
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    public async Task<OperationResult> SafeDeleteAsync(string path, bool createBackup = true, CancellationToken ct = default)
    {
        Guid transactionId = Guid.NewGuid();

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled",
                TransactionId: transactionId);
        }

        // Étape 1 : Vérification chemin critique système
        if (IsSystemCriticalPath(path))
        {
            _logger.LogError("Attempted to delete system critical path: {Path}", path);
            return new OperationResult(
                Success: false,
                Message: "System critical path blocked",
                TransactionId: transactionId,
                OriginalFile: null,
                BackupPath: null);
        }

        // Étape 2 : Vérification existence fichier
        if (!File.Exists(path))
        {
            return new OperationResult(
                Success: false,
                Message: "File not found",
                TransactionId: transactionId,
                OriginalFile: null,
                BackupPath: null);
        }

        // Étape 3 : Vérification fichier verrouillé
        if (IsFileLocked(path))
        {
            return new OperationResult(
                Success: false,
                Message: "File is locked by another process",
                TransactionId: transactionId,
                OriginalFile: null,
                BackupPath: null);
        }

        // Récupération des infos avant suppression
        FileInfo fileInfo = new(path);
        string contentHash = await HashHelper.ComputeHashAsync(path, ct);
        FileEntry originalFile = new(
            Path: path,
            Size: fileInfo.Length,
            LastAccessTime: fileInfo.LastAccessTime,
            ContentHash: contentHash);

        string? backupPath = null;

        // Étape 4 : Création du backup si demandé
        if (createBackup)
        {
            try
            {
                BackupInfo backup = await _backupManager.CreateBackupAsync(path, ct);
                backupPath = backup.BackupPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup failed for {Path}, aborting delete", path);
                return new OperationResult(
                    Success: false,
                    Message: "Backup failed, aborting delete",
                    TransactionId: transactionId,
                    OriginalFile: originalFile,
                    BackupPath: null);
            }
        }

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled before delete",
                TransactionId: transactionId,
                OriginalFile: originalFile,
                BackupPath: backupPath);
        }

        // Étape 5 : Suppression du fichier (UNIQUEMENT ici)
        try
        {
            File.Delete(path);
            _logger.LogInformation("File deleted successfully: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {Path}", path);
            return new OperationResult(
                Success: false,
                Message: $"Delete failed: {ex.Message}",
                TransactionId: transactionId,
                OriginalFile: originalFile,
                BackupPath: backupPath);
        }

        // Étape 6 : Retour du résultat succès
        return new OperationResult(
            Success: true,
            Message: "Deleted successfully",
            TransactionId: transactionId,
            OriginalFile: originalFile,
            BackupPath: backupPath);
    }

    public async Task<OperationResult> SafeCleanDirectoryAsync(string dir, string searchPattern = "*", bool recursive = false, CancellationToken ct = default)
    {
        Guid transactionId = Guid.NewGuid();

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled",
                TransactionId: transactionId);
        }

        // Vérification que le répertoire lui-même n'est pas un chemin critique
        if (IsSystemCriticalPath(dir))
        {
            _logger.LogError("Attempted to clean system critical directory: {Dir}", dir);
            return new OperationResult(
                Success: false,
                Message: "System critical directory blocked",
                TransactionId: transactionId);
        }

        if (!Directory.Exists(dir))
        {
            return new OperationResult(
                Success: false,
                Message: "Directory not found",
                TransactionId: transactionId);
        }

        List<OperationResult> results = [];
        long totalSpaceRecovered = 0;
        int filesProcessed = 0;

        try
        {
            IEnumerable<string> files = recursive
                ? Directory.EnumerateFiles(dir, searchPattern, SearchOption.AllDirectories)
                : Directory.EnumerateFiles(dir, searchPattern, SearchOption.TopDirectoryOnly);

            foreach (string file in files)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                OperationResult result = await SafeDeleteAsync(file, createBackup: true, ct);
                results.Add(result);

                if (result.Success && result.OriginalFile != null)
                {
                    totalSpaceRecovered += result.OriginalFile.Size;
                    filesProcessed++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating files in directory: {Dir}", dir);
            return new OperationResult(
                Success: false,
                Message: $"Directory enumeration failed: {ex.Message}",
                TransactionId: transactionId);
        }

        bool allSuccess = results.Count > 0 && results.All(r => r.Success);

        return new OperationResult(
            Success: allSuccess,
            Message: allSuccess
                ? $"Successfully cleaned {filesProcessed} files, recovered {totalSpaceRecovered} bytes"
                : $"Cleaned with {results.Count(r => !r.Success)} failures out of {results.Count} files",
            TransactionId: transactionId);
    }


}
