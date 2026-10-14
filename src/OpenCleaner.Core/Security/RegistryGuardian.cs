using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;

namespace OpenCleaner.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class RegistryGuardian : IRegistryGuardian
{
    private readonly ILogger<RegistryGuardian> _logger;
    private static readonly string[] ForbiddenKeys =
    [
        @"HKEY_LOCAL_MACHINE\SYSTEM", @"HKEY_LOCAL_MACHINE\HARDWARE", @"HKEY_LOCAL_MACHINE\SECURITY",
        @"HKEY_LOCAL_MACHINE\SAM", @"HKEY_LOCAL_MACHINE\BCD00000000",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
    ];

    public RegistryGuardian(ILogger<RegistryGuardian> logger) => _logger = logger;

    public bool IsSystemCriticalKey(string keyPath)
    {
        if (!TryNormalizeKeyPath(keyPath, out string normalized)) return true;
        if (!normalized.Contains('\\')) return true;
        // Ancestors are protected too: deleting SOFTWARE must never remove a protected child.
        if (ForbiddenKeys.Any(forbidden => IsWithin(normalized, forbidden) || IsWithin(forbidden, normalized))) return true;
        return normalized.Split('\\').Any(part => part.Equals("CLSID", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Interface", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> ExportKeyAsync(string keyPath, string backupDirectory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        (RegistryKey root, string subPath) = ParseKeyPath(keyPath);
        using RegistryKey key = root.OpenSubKey(subPath) ?? throw new IOException("Registry key not found");
        string directory = PathSafety.Normalize(backupDirectory);
        if (PathSafety.HasReparsePoint(directory)) throw new IOException("Linked registry backup directory blocked");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"registry-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.reg");
        var output = new StringBuilder();
        output.AppendLine("Windows Registry Editor Version 5.00");
        output.AppendLine($"; OpenCleaner backup {DateTime.UtcNow:O}");
        output.AppendLine();
        ExportKeyRecursive(key, output, ct);
        await File.WriteAllTextAsync(path, output.ToString(), Encoding.Unicode, ct);
        _logger.LogInformation("Registry key exported: {KeyPath} to {BackupFile}", keyPath, path);
        return path;
    }

    public Task<OperationResult> SafeDeleteKeyAsync(string keyPath, bool backupFirst = true, CancellationToken ct = default) =>
        DeleteAsync(keyPath, null, backupFirst, ct);

    public Task<OperationResult> SafeDeleteValueAsync(string keyPath, string valueName, bool backupFirst = true, CancellationToken ct = default) =>
        DeleteAsync(keyPath, valueName ?? throw new ArgumentNullException(nameof(valueName)), backupFirst, ct);

    private async Task<OperationResult> DeleteAsync(string keyPath, string? valueName, bool backupFirst, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Guid id = Guid.NewGuid();
        string? backupPath = null;
        OperationResult Failure(string message) => new(false, message, id, BackupPath: backupPath, ItemsProcessed: 0, BytesRecovered: 0);
        if (IsSystemCriticalKey(keyPath)) return Failure("System critical or invalid registry key blocked");
        try
        {
            (RegistryKey root, string subPath) = ParseKeyPath(keyPath);
            using (RegistryKey? key = root.OpenSubKey(subPath, writable: true))
            {
                if (key == null) return Failure("Registry key not found or inaccessible");
                if (valueName == null && key.SubKeyCount != 0)
                    return Failure("Only leaf registry keys may be deleted");
                if (valueName != null && !key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
                    return Failure("Registry value not found");
            }
            if (backupFirst)
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCleaner", "RegistryBackups");
                backupPath = await ExportKeyAsync(keyPath, directory, ct);
            }
            ct.ThrowIfCancellationRequested();
            if (valueName == null)
                root.DeleteSubKey(subPath, throwOnMissingSubKey: true);
            else
            {
                using RegistryKey key = root.OpenSubKey(subPath, writable: true) ?? throw new IOException("Registry key disappeared");
                key.DeleteValue(valueName, throwOnMissingValue: true);
            }
            return new(true, valueName == null ? "Registry key deleted" : "Registry value deleted", id,
                BackupPath: backupPath, ItemsProcessed: 1, BytesRecovered: 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            _logger.LogWarning(ex, "Registry deletion failed for {KeyPath}", keyPath);
            return Failure($"Registry delete failed: {ex.Message}");
        }
    }

    private static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeKeyPath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c is '\r' or '\n' or '\0')) return false;
        string[] parts = path.Trim().Replace('/', '\\').Split('\\');
        if (parts.Any(string.IsNullOrEmpty)) return false;
        string? hive = parts[0].ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKU" or "HKEY_USERS" => "HKEY_USERS",
            "HKCC" or "HKEY_CURRENT_CONFIG" => "HKEY_CURRENT_CONFIG",
            _ => null
        };
        if (hive == null) return false;
        parts[0] = hive;
        normalized = string.Join('\\', parts);
        return true;
    }

    private static (RegistryKey Root, string SubPath) ParseKeyPath(string path)
    {
        if (!TryNormalizeKeyPath(path, out string normalized)) throw new ArgumentException("Invalid registry key path", nameof(path));
        int separator = normalized.IndexOf('\\');
        if (separator < 0) throw new ArgumentException("A registry subkey is required", nameof(path));
        RegistryKey root = normalized[..separator] switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKEY_USERS" => Registry.Users,
            "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException("Invalid registry hive", nameof(path))
        };
        return (root, normalized[(separator + 1)..]);
    }

    private static void ExportKeyRecursive(RegistryKey key, StringBuilder output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (key.Name.Any(c => c is '\r' or '\n' or ']')) throw new IOException("Registry key cannot be represented safely in a .reg backup");
        output.AppendLine($"[{key.Name}]");
        foreach (string name in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            output.AppendLine(FormatRegistryValue(name, value, key.GetValueKind(name)));
        }
        output.AppendLine();
        foreach (string name in key.GetSubKeyNames())
        {
            using RegistryKey child = key.OpenSubKey(name) ?? throw new IOException("Registry subkey disappeared during backup");
            ExportKeyRecursive(child, output, ct);
        }
    }

    private static string FormatRegistryValue(string name, object? value, RegistryValueKind kind)
    {
        if (name.Any(c => c is '\r' or '\n' or '\0')) throw new IOException("Registry value name cannot be exported safely");
        string escapedName = name.Length == 0 ? "@" : $"\"{Escape(name)}\"";
        string encoded = kind switch
        {
            RegistryValueKind.String => FormatString((string?)value ?? ""),
            RegistryValueKind.ExpandString => "hex(2):" + Hex(Encoding.Unicode.GetBytes(((string?)value ?? "") + "\0")),
            RegistryValueKind.MultiString => "hex(7):" + Hex(Encoding.Unicode.GetBytes(string.Join('\0', (string[]?)value ?? []) + "\0\0")),
            RegistryValueKind.DWord => $"dword:{Convert.ToInt32(value, CultureInfo.InvariantCulture):x8}",
            RegistryValueKind.QWord => "hex(b):" + Hex(QwordBytes(Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            RegistryValueKind.Binary => "hex:" + Hex((byte[]?)value ?? []),
            RegistryValueKind.None => "hex(0):" + Hex((byte[]?)value ?? []),
            _ => throw new IOException("Unsupported registry value type; deletion aborted")
        };
        return $"{escapedName}={encoded}";
    }

    private static string FormatString(string value) => value.Any(char.IsControl)
        ? "hex(1):" + Hex(Encoding.Unicode.GetBytes(value + "\0")) : $"\"{Escape(value)}\"";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Hex(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    private static byte[] QwordBytes(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }
}
