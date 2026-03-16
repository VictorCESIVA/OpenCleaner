using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace OpenCleaner.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class RegistryGuardian : IRegistryGuardian
{
    private readonly ILogger<RegistryGuardian> _logger;

    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_LOCAL_MACHINE\HARDWARE",
        @"HKEY_LOCAL_MACHINE\SECURITY",
        @"HKEY_LOCAL_MACHINE\SAM",
        @"HKEY_LOCAL_MACHINE\BCD00000000",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
    };

    public RegistryGuardian(ILogger<RegistryGuardian> logger)
    {
        _logger = logger;
    }

    public bool IsSystemCriticalKey(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return true;
        }

        string normalizedPath = NormalizeKeyPath(keyPath);

        // Vérifie les clés explicitement interdites
        foreach (string forbidden in ForbiddenKeys)
        {
            if (normalizedPath.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Vérifie les CLSID et Interface sous HKCR
        if (normalizedPath.Contains(@"\CLSID\", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(@"\CLSID", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains(@"\INTERFACE\", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(@"\INTERFACE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Clés de boot/drivers
        if (normalizedPath.StartsWith(@"HKEY_LOCAL_MACHINE\SYSTEM", StringComparison.OrdinalIgnoreCase) &&
            (normalizedPath.Contains(@"\CONTROLSET", StringComparison.OrdinalIgnoreCase) ||
             normalizedPath.Contains(@"\CURRENTCONTROLSET", StringComparison.OrdinalIgnoreCase)))
        {
            // Autorise uniquement les sous-clés non critiques comme\Services\MonService (mais pas les pilotes système)
            if (normalizedPath.Contains(@"\ENUM\", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains(@"\CONTROL\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<string> ExportKeyAsync(string keyPath, string backupDirectory, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }

        (RegistryKey? rootKey, string subKeyPath) = ParseKeyPath(keyPath);

        if (rootKey == null)
        {
            throw new ArgumentException($"Invalid registry key path: {keyPath}", nameof(keyPath));
        }

        Directory.CreateDirectory(backupDirectory);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string safeKeyName = subKeyPath.Replace('\\', '_').Replace('/', '_');
        string backupFilePath = Path.Combine(backupDirectory, $"{safeKeyName}_{timestamp}.reg");

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"; Backup OpenCleaner - Date: {DateTime.UtcNow:O} - Original Path: {keyPath}");
        stringBuilder.AppendLine("Windows Registry Editor Version 5.00");
        stringBuilder.AppendLine();

        ExportKeyRecursive(rootKey, subKeyPath, stringBuilder);

        await File.WriteAllTextAsync(backupFilePath, stringBuilder.ToString(), ct);

        _logger.LogInformation("Registry key exported: {KeyPath} to {BackupFile}", keyPath, backupFilePath);

        return backupFilePath;
    }

    public async Task<OperationResult> SafeDeleteKeyAsync(string keyPath, bool backupFirst = true, CancellationToken ct = default)
    {
        Guid transactionId = Guid.NewGuid();

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled",
                TransactionId: transactionId);
        }

        // Étape 1 : Vérification chemin critique
        if (IsSystemCriticalKey(keyPath))
        {
            _logger.LogError("Attempted to delete system critical registry key: {KeyPath}", keyPath);
            return new OperationResult(
                Success: false,
                Message: "System critical registry key blocked",
                TransactionId: transactionId);
        }

        (RegistryKey? rootKey, string subKeyPath) = ParseKeyPath(keyPath);

        if (rootKey == null)
        {
            return new OperationResult(
                Success: false,
                Message: "Invalid registry key path",
                TransactionId: transactionId);
        }

        // Étape 2 : Vérification des permissions (ouverture en Write)
        using RegistryKey? key = rootKey.OpenSubKey(subKeyPath, writable: false);
        if (key == null)
        {
            return new OperationResult(
                Success: false,
                Message: "Registry key not found",
                TransactionId: transactionId);
        }

        // Test d'écriture sur la clé parente
        string? parentPath = Path.GetDirectoryName(subKeyPath)?.Replace('/', '\\');
        if (parentPath != null)
        {
            using RegistryKey? parentKey = rootKey.OpenSubKey(parentPath, writable: true);
            if (parentKey == null)
            {
                return new OperationResult(
                    Success: false,
                    Message: "Insufficient permissions to delete registry key",
                    TransactionId: transactionId);
            }
        }

        string? backupPath = null;

        // Étape 3 : Backup si demandé
        if (backupFirst)
        {
            try
            {
                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCleaner", "RegistryBackups");
                backupPath = await ExportKeyAsync(keyPath, backupDir, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry backup failed for {KeyPath}, aborting delete", keyPath);
                return new OperationResult(
                    Success: false,
                    Message: "Registry backup failed, aborting delete",
                    TransactionId: transactionId);
            }
        }

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled before delete",
                TransactionId: transactionId,
                BackupPath: backupPath);
        }

        // Étape 4 : Suppression de la clé (DeleteSubKey, pas DeleteSubKeyTree)
        try
        {
            string keyName = Path.GetFileName(subKeyPath);
            if (parentPath != null)
            {
                using RegistryKey? parentKey = rootKey.OpenSubKey(parentPath, writable: true);
                if (parentKey != null)
                {
                    parentKey.DeleteSubKey(keyName, throwOnMissingSubKey: false);
                }
            }

            _logger.LogInformation("Registry key deleted: {KeyPath}", keyPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete registry key: {KeyPath}", keyPath);
            return new OperationResult(
                Success: false,
                Message: $"Registry delete failed: {ex.Message}",
                TransactionId: transactionId,
                BackupPath: backupPath);
        }

        // Étape 5 : Succès
        return new OperationResult(
            Success: true,
            Message: "Registry key deleted successfully",
            TransactionId: transactionId,
            BackupPath: backupPath);
    }

    public async Task<OperationResult> SafeDeleteValueAsync(string keyPath, string valueName, bool backupFirst = true, CancellationToken ct = default)
    {
        Guid transactionId = Guid.NewGuid();

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled",
                TransactionId: transactionId);
        }

        // Étape 1 : Vérification chemin critique
        if (IsSystemCriticalKey(keyPath))
        {
            _logger.LogError("Attempted to modify system critical registry key: {KeyPath}", keyPath);
            return new OperationResult(
                Success: false,
                Message: "System critical registry key blocked",
                TransactionId: transactionId);
        }

        (RegistryKey? rootKey, string subKeyPath) = ParseKeyPath(keyPath);

        if (rootKey == null)
        {
            return new OperationResult(
                Success: false,
                Message: "Invalid registry key path",
                TransactionId: transactionId);
        }

        // Étape 2 : Vérification existence et permissions
        using RegistryKey? key = rootKey.OpenSubKey(subKeyPath, writable: true);
        if (key == null)
        {
            return new OperationResult(
                Success: false,
                Message: "Registry key not found or insufficient permissions",
                TransactionId: transactionId);
        }

        if (key.GetValue(valueName) == null)
        {
            return new OperationResult(
                Success: false,
                Message: "Registry value not found",
                TransactionId: transactionId);
        }

        string? backupPath = null;

        // Étape 3 : Backup si demandé
        if (backupFirst)
        {
            try
            {
                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCleaner", "RegistryBackups");
                backupPath = await ExportKeyAsync(keyPath, backupDir, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry backup failed for {KeyPath}, aborting value delete", keyPath);
                return new OperationResult(
                    Success: false,
                    Message: "Registry backup failed, aborting delete",
                    TransactionId: transactionId);
            }
        }

        if (ct.IsCancellationRequested)
        {
            return new OperationResult(
                Success: false,
                Message: "Operation cancelled before delete",
                TransactionId: transactionId,
                BackupPath: backupPath);
        }

        // Étape 4 : Suppression de la valeur
        try
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.LogInformation("Registry value deleted: {ValueName} from {KeyPath}", valueName, keyPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete registry value: {ValueName} from {KeyPath}", valueName, keyPath);
            return new OperationResult(
                Success: false,
                Message: $"Registry value delete failed: {ex.Message}",
                TransactionId: transactionId,
                BackupPath: backupPath);
        }

        // Étape 5 : Succès
        return new OperationResult(
            Success: true,
            Message: "Registry value deleted successfully",
            TransactionId: transactionId,
            BackupPath: backupPath);
    }

    private static string NormalizeKeyPath(string keyPath)
    {
        string normalized = keyPath.ToUpperInvariant().Trim();

        // Remplace les abréviations courantes
        normalized = normalized
            .Replace("HKLM\\", "HKEY_LOCAL_MACHINE\\")
            .Replace("HKCU\\", "HKEY_CURRENT_USER\\")
            .Replace("HKCR\\", "HKEY_CLASSES_ROOT\\")
            .Replace("HKU\\", "HKEY_USERS\\")
            .Replace("HKCC\\", "HKEY_CURRENT_CONFIG\\")
            .Replace("HKPD\\", "HKEY_PERFORMANCE_DATA\\");

        return normalized;
    }

    private static (RegistryKey? RootKey, string SubKeyPath) ParseKeyPath(string keyPath)
    {
        string normalizedPath = NormalizeKeyPath(keyPath);

        if (normalizedPath.StartsWith("HKEY_LOCAL_MACHINE\\"))
        {
            return (Registry.LocalMachine, normalizedPath[19..]);
        }
        if (normalizedPath.StartsWith("HKEY_CURRENT_USER\\"))
        {
            return (Registry.CurrentUser, normalizedPath[18..]);
        }
        if (normalizedPath.StartsWith("HKEY_CLASSES_ROOT\\"))
        {
            return (Registry.ClassesRoot, normalizedPath[18..]);
        }
        if (normalizedPath.StartsWith("HKEY_USERS\\"))
        {
            return (Registry.Users, normalizedPath[11..]);
        }
        if (normalizedPath.StartsWith("HKEY_CURRENT_CONFIG\\"))
        {
            return (Registry.CurrentConfig, normalizedPath[20..]);
        }

        return (null, string.Empty);
    }

    private static void ExportKeyRecursive(RegistryKey rootKey, string subKeyPath, StringBuilder output, int depth = 0)
    {
        using RegistryKey? key = rootKey.OpenSubKey(subKeyPath);
        if (key == null)
        {
            return;
        }

        string fullPath = $"[{key.Name}]";
        output.AppendLine(fullPath);

        // Exporte les valeurs
        foreach (string valueName in key.GetValueNames())
        {
            object? value = key.GetValue(valueName);
            RegistryValueKind kind = key.GetValueKind(valueName);

            string formattedValue = FormatRegistryValue(valueName, value, kind);
            output.AppendLine(formattedValue);
        }

        output.AppendLine();

        // Exporte les sous-clés récursivement
        foreach (string subKeyName in key.GetSubKeyNames())
        {
            string childPath = $"{subKeyPath}\\{subKeyName}";
            ExportKeyRecursive(rootKey, childPath, output, depth + 1);
        }
    }

    private static string FormatRegistryValue(string name, object? value, RegistryValueKind kind)
    {
        string escapedName = string.IsNullOrEmpty(name) ? "@" : $"\"{name.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        switch (kind)
        {
            case RegistryValueKind.String:
                string strValue = value?.ToString() ?? "";
                strValue = strValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"{escapedName}=\"{strValue}\"";

            case RegistryValueKind.DWord:
                int dwordValue = value != null ? Convert.ToInt32(value) : 0;
                return $"{escapedName}=dword:{dwordValue:X8}";

            case RegistryValueKind.QWord:
                long qwordValue = value != null ? Convert.ToInt64(value) : 0;
                return $"{escapedName}=qword:{qwordValue:X16}";

            case RegistryValueKind.Binary:
                byte[]? binaryValue = value as byte[] ?? [];
                string hexString = BitConverter.ToString(binaryValue).Replace("-", "").ToLowerInvariant();
                // Format hex avec continuation de ligne si > 80 caractères
                if (hexString.Length > 80)
                {
                    var lines = new StringBuilder();
                    lines.Append($"{escapedName}=hex:");
                    for (int i = 0; i < hexString.Length; i += 2)
                    {
                        if (i > 0 && i % 80 == 0)
                        {
                            lines.Append("\\");
                            lines.AppendLine();
                            lines.Append("  ");
                        }
                        lines.Append(hexString.Substring(i, 2));
                        lines.Append(",");
                    }
                    if (lines[lines.Length - 1] == ',')
                    {
                        lines.Length--;
                    }
                    return lines.ToString();
                }
                return $"{escapedName}=hex:{hexString}";

            case RegistryValueKind.MultiString:
                string[]? multiStrings = value as string[] ?? [];
                var multiBuilder = new StringBuilder();
                multiBuilder.Append($"{escapedName}=hex(7):");
                foreach (string s in multiStrings)
                {
                    foreach (char c in s)
                    {
                        multiBuilder.AppendFormat("{0:X2},00,", (ushort)c);
                    }
                    multiBuilder.Append("00,00,");
                }
                multiBuilder.Append("00,00");
                return multiBuilder.ToString();

            case RegistryValueKind.ExpandString:
                string? expandStr = value?.ToString() ?? "";
                var expandBuilder = new StringBuilder();
                expandBuilder.Append($"{escapedName}=hex(2):");
                foreach (char c in expandStr)
                {
                    expandBuilder.AppendFormat("{0:X2},00,", (ushort)c);
                }
                expandBuilder.Append("00,00");
                return expandBuilder.ToString();

            default:
                return $"{escapedName}=\"{value}\"";
        }
    }
}
