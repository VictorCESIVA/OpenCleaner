using System.Security.Cryptography;

namespace OpenCleaner.Core;

/// <summary>
/// Utilitaire partagé pour le hachage SHA-256 de fichiers.
/// Utilisé par BackupManager, FileGuardian et DuplicateFinderPlugin.
/// </summary>
public static class HashHelper
{
    private const int BufferSize = 64 * 1024; // 64 Ko

    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            sha256.TransformBlock(buffer, 0, read, null, 0);

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!);
    }
}
