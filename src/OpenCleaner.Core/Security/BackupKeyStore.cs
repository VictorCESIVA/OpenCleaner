using System.Security.Cryptography;
using System.Text.Json;

namespace OpenCleaner.Core.Security;

public sealed class BackupKeyUnavailableException : CryptographicException
{
    public BackupKeyUnavailableException(string message) : base(message) { }
}

internal sealed class BackupKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _keyPath;
    private readonly string _recoveryPath;
    private byte[]? _key;

    public BackupKeyStore(string keyPath)
    {
        _keyPath = PathSafety.Normalize(keyPath);
        _recoveryPath = _keyPath + ".recovery";
    }

    public bool IsKeyAvailable
    {
        get { lock (_sync) return _key != null || File.Exists(_keyPath); }
    }

    public bool HasRecoveryEnvelope => File.Exists(_recoveryPath);

    public byte[] GetKey(bool allowCreate)
    {
        lock (_sync)
        {
            if (_key != null) return _key;
            if (File.Exists(_keyPath))
            {
                if (PathSafety.HasReparsePoint(_keyPath))
                    throw new IOException("Backup key file contains a reparse point");
                _key = BackupCipher.UnprotectKey(File.ReadAllBytes(_keyPath));
                return _key;
            }
            if (File.Exists(_recoveryPath) || !allowCreate)
                throw new BackupKeyUnavailableException("The backup key is missing; use the recovery code");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");

            byte[] key = RandomNumberGenerator.GetBytes(64);
            try
            {
                WriteProtectedKey(key, overwrite: false);
            }
            catch (IOException) when (File.Exists(_keyPath))
            {
                CryptographicOperations.ZeroMemory(key);
                _key = BackupCipher.UnprotectKey(File.ReadAllBytes(_keyPath));
                return _key;
            }
            _key = key;
            return key;
        }
    }

    public string? EnsureRecoveryCode(bool createKeyIfMissing)
    {
        lock (_sync)
        {
            _ = GetKey(createKeyIfMissing);
            if (File.Exists(_recoveryPath)) return null;
            return CreateAndWriteRecoveryCode(_key!);
        }
    }

    public string RegenerateRecoveryCode()
    {
        lock (_sync)
        {
            _ = GetKey(allowCreate: false);
            return CreateAndWriteRecoveryCode(_key!);
        }
    }

    public bool RecoverKey(string recoveryCode)
    {
        lock (_sync)
        {
            if (!File.Exists(_recoveryPath)) throw new BackupKeyUnavailableException("No recovery envelope exists");
            if (PathSafety.HasReparsePoint(_recoveryPath))
                throw new IOException("Recovery envelope contains a reparse point");
            RecoveryEnvelope envelope = JsonSerializer.Deserialize<RecoveryEnvelope>(File.ReadAllText(_recoveryPath))
                ?? throw new CryptographicException("Invalid recovery envelope");
            byte[] key = BackupCipher.OpenRecoveryEnvelope(envelope, recoveryCode);
            WriteProtectedKey(key, overwrite: true);
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = key;
            return true;
        }
    }

    private string CreateAndWriteRecoveryCode(byte[] key)
    {
        string code = RecoveryCode.Generate();
        RecoveryEnvelope envelope = BackupCipher.CreateRecoveryEnvelope(key, code);
        string temporaryPath = _recoveryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, JsonOptions));
            File.Move(temporaryPath, _recoveryPath, overwrite: true);
            return code;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void WriteProtectedKey(byte[] key, bool overwrite)
    {
        string directory = Path.GetDirectoryName(_keyPath)!;
        Directory.CreateDirectory(directory);
        if (PathSafety.HasReparsePoint(directory))
            throw new IOException("Backup key directory contains a reparse point");
        if (!overwrite && File.Exists(_keyPath)) throw new IOException("The backup key already exists");
        byte[] protectedKey = BackupCipher.ProtectKey(key);
        string temporaryPath = _keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedKey);
            File.Move(temporaryPath, _keyPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
