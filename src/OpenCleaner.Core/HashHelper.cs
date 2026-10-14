using System.Buffers;
using System.Security.Cryptography;

namespace OpenCleaner.Core;

/// <summary>
/// Utilitaire partagé pour le hachage SHA-256 de fichiers.
/// Utilisé par BackupManager, FileGuardian et DuplicateFinderPlugin.
/// </summary>
public static class HashHelper
{
    private const int BufferSize = 64 * 1024; // 64 Ko
    private const int PartialChunkSize = 4 * 1024; // 4 Ko (début + fin)

    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Empreinte rapide (premiers et derniers 4 Ko) pour pré-filtrer les fichiers de même taille
    /// avant un hachage SHA-256 complet.
    /// </summary>
    public static async Task<string> ComputePartialHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: PartialChunkSize, FileOptions.Asynchronous | FileOptions.RandomAccess);

        long length = stream.Length;
        if (length <= PartialChunkSize * 2)
        {
            byte[] fullHash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(fullHash);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(PartialChunkSize * 2);
        try
        {
            int firstRead = await stream.ReadAtLeastAsync(
                buffer.AsMemory(0, PartialChunkSize), PartialChunkSize, throwOnEndOfStream: false, ct);
            stream.Seek(-PartialChunkSize, SeekOrigin.End);
            int lastRead = await stream.ReadAtLeastAsync(
                buffer.AsMemory(firstRead, PartialChunkSize), PartialChunkSize, throwOnEndOfStream: false, ct);

            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(buffer.AsSpan(0, firstRead + lastRead), hash);
            return Convert.ToHexString(hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
