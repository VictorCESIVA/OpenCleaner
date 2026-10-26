using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenCleaner.Core.Security;

internal static class BackupCipher
{
    private const int ChunkSize = 1024 * 1024;
    private const int TagSize = 16;
    private const int NonceSize = 12;
    private const int KeySize = 64;
    private static readonly byte[] PayloadMagic = "OCBK0001"u8.ToArray();
    private static readonly byte[] Entropy = "OpenCleaner.BackupKey.v1"u8.ToArray();

    public static byte[] LoadOrCreateKey(string keyPath)
    {
        string path = PathSafety.Normalize(keyPath);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (PathSafety.HasReparsePoint(directory))
            throw new IOException("Backup key directory contains a reparse point");

        if (File.Exists(path))
        {
            if (PathSafety.HasReparsePoint(path))
                throw new IOException("Backup key file contains a reparse point");
            return Unprotect(File.ReadAllBytes(path));
        }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");

        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        byte[] protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedKey);
            try
            {
                File.Move(temporaryPath, path);
                return key;
            }
            catch (IOException) when (File.Exists(path))
            {
                CryptographicOperations.ZeroMemory(key);
                return Unprotect(File.ReadAllBytes(path));
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static string ComputeMetadataSignature(byte[] key, string originalPath, string originalHash,
        DateTime creationDate, long fileSize, string? fileName, bool encrypted)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key.AsSpan(32, 32));
        Append(hmac, originalPath);
        Append(hmac, originalHash);
        Append(hmac, creationDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(hmac, fileSize.ToString(CultureInfo.InvariantCulture));
        Append(hmac, fileName ?? string.Empty);
        Append(hmac, encrypted ? "1" : "0");
        return Convert.ToBase64String(hmac.GetHashAndReset());
    }

    private static void Append(IncrementalHash hmac, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hmac.AppendData(length);
        hmac.AppendData(bytes);
    }

    public static async Task<(long Size, string Hash)> EncryptAsync(byte[] key, Stream source, Stream destination,
        CancellationToken ct)
    {
        byte[] prefix = RandomNumberGenerator.GetBytes(8);
        await destination.WriteAsync(PayloadMagic, ct);
        await destination.WriteAsync(prefix, ct);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var aes = new AesGcm(key.AsSpan(0, 32), TagSize);
        byte[] plain = ArrayPool<byte>.Shared.Rent(ChunkSize);
        byte[] cipher = ArrayPool<byte>.Shared.Rent(ChunkSize);
        byte[] tag = new byte[TagSize];
        byte[] lengthBytes = new byte[4];
        byte[] nonce = new byte[NonceSize];
        Buffer.BlockCopy(prefix, 0, nonce, 0, prefix.Length);
        long total = 0;
        uint counter = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(plain.AsMemory(0, ChunkSize), ct)) > 0)
            {
                if (counter == uint.MaxValue) throw new CryptographicException("Backup is too large");
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), counter++);
                hasher.AppendData(plain, 0, read);
                aes.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag);
                BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, read);
                await destination.WriteAsync(lengthBytes, ct);
                await destination.WriteAsync(cipher.AsMemory(0, read), ct);
                await destination.WriteAsync(tag, ct);
                total += read;
            }
            await destination.FlushAsync(ct);
            return (total, Convert.ToHexString(hasher.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plain);
            ArrayPool<byte>.Shared.Return(cipher);
        }
    }

    public static async Task<(long Size, string Hash)> DecryptAsync(byte[] key, Stream source, Stream destination,
        CancellationToken ct)
    {
        byte[] magic = new byte[PayloadMagic.Length];
        await source.ReadExactlyAsync(magic, ct);
        if (!magic.AsSpan().SequenceEqual(PayloadMagic)) throw new InvalidDataException("Invalid backup format");

        byte[] prefix = new byte[8];
        await source.ReadExactlyAsync(prefix, ct);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var aes = new AesGcm(key.AsSpan(0, 32), TagSize);
        byte[] plain = ArrayPool<byte>.Shared.Rent(ChunkSize);
        byte[] cipher = ArrayPool<byte>.Shared.Rent(ChunkSize);
        byte[] tag = new byte[TagSize];
        byte[] lengthBytes = new byte[4];
        byte[] nonce = new byte[NonceSize];
        Buffer.BlockCopy(prefix, 0, nonce, 0, prefix.Length);
        long total = 0;
        uint counter = 0;
        try
        {
            while (true)
            {
                int first = await source.ReadAsync(lengthBytes.AsMemory(0, 1), ct);
                if (first == 0) break;
                await source.ReadExactlyAsync(lengthBytes.AsMemory(1, 3), ct);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length is <= 0 or > ChunkSize) throw new InvalidDataException("Invalid backup chunk");
                await source.ReadExactlyAsync(cipher.AsMemory(0, length), ct);
                await source.ReadExactlyAsync(tag, ct);
                if (counter == uint.MaxValue) throw new CryptographicException("Backup is too large");
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), counter++);
                aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length));
                await destination.WriteAsync(plain.AsMemory(0, length), ct);
                hasher.AppendData(plain, 0, length);
                total += length;
            }
            await destination.FlushAsync(ct);
            return (total, Convert.ToHexString(hasher.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plain);
            ArrayPool<byte>.Shared.Return(cipher);
        }
    }

    private static byte[] Unprotect(byte[] protectedKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");
        byte[] key = ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser);
        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Invalid backup key length");
        }
        return key;
    }
}
