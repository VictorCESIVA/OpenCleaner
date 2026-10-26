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

    public static byte[] ProtectKey(byte[] key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");
        if (key.Length != KeySize) throw new CryptographicException("Invalid backup key length");
        return ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
    }

    public static byte[] UnprotectKey(byte[] protectedKey)
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

    public static RecoveryEnvelope CreateRecoveryEnvelope(byte[] key, string code)
    {
        if (key.Length != KeySize) throw new CryptographicException("Invalid backup key length");
        string normalized = RecoveryCode.Normalize(code);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 600_000;
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(normalized, salt, iterations, HashAlgorithmName.SHA256, 32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[KeySize];
        byte[] tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(derived, TagSize);
            aes.Encrypt(nonce, key, ciphertext, tag);
            return new RecoveryEnvelope(1, Convert.ToBase64String(salt), iterations,
                Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    public static byte[] OpenRecoveryEnvelope(RecoveryEnvelope envelope, string code)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Version != 1 || envelope.Iterations is < 100_000 or > 5_000_000)
            throw new CryptographicException("Invalid recovery envelope");
        byte[] salt = Convert.FromBase64String(envelope.Salt);
        byte[] nonce = Convert.FromBase64String(envelope.Nonce);
        byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        byte[] tag = Convert.FromBase64String(envelope.Tag);
        if (salt.Length != 16 || nonce.Length != 12 || ciphertext.Length != KeySize || tag.Length != TagSize)
            throw new CryptographicException("Invalid recovery envelope");

        string normalized = RecoveryCode.Normalize(code);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(normalized, salt, envelope.Iterations,
            HashAlgorithmName.SHA256, 32);
        byte[] key = new byte[KeySize];
        try
        {
            using var aes = new AesGcm(derived, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, key);
            return key;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
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

}

internal sealed record RecoveryEnvelope(
    int Version,
    string Salt,
    int Iterations,
    string Nonce,
    string Ciphertext,
    string Tag);
