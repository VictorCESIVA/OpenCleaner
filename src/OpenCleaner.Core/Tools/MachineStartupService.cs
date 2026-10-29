using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace OpenCleaner.Core.Tools;

public enum MachineStartupAction
{
    Disable = 0,
    Enable = 1
}

public enum MachineStartupKey
{
    Run = 0,
    RunOnce = 1
}

/// <summary>Demande validée avant toute écriture HKLM, y compris depuis le processus élevé.</summary>
public sealed record MachineStartupRequest(
    MachineStartupAction Action,
    MachineStartupKey Key,
    string ValueName,
    string? ExpectedValue = null);

public sealed record MachineStartupResult(bool Success, string Message);

/// <summary>
/// Active ou désactive un élément HKLM Run/RunOnce avec sauvegarde préalable.
/// La sauvegarde est conservée sous HKLM\SOFTWARE\OpenCleaner\DisabledStartup.
/// Toute la validation est refaite ici, même quand l'appel vient du processus élevé.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MachineStartupService
{
    public const string MachineRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string MachineRunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    public const string MachineBackupRoot = @"SOFTWARE\OpenCleaner\DisabledStartup";
    private const int MaxValueNameLength = 512;
    private const int MaxValueLength = 32_767;

    private readonly RegistryKey _root;
    private readonly string _runPath;
    private readonly string _runOncePath;
    private readonly string _backupRoot;

    public MachineStartupService()
        : this(Registry.LocalMachine, MachineRunPath, MachineRunOncePath, MachineBackupRoot)
    {
    }

    internal MachineStartupService(RegistryKey root, string runPath, string runOncePath, string backupRoot)
    {
        _root = root;
        _runPath = runPath;
        _runOncePath = runOncePath;
        _backupRoot = backupRoot;
    }

    public static bool Validate(MachineStartupRequest request, out string error)
    {
        error = "";
        if (request == null)
        {
            error = "Demande absente.";
            return false;
        }
        if (!Enum.IsDefined(request.Action))
        {
            error = "Action inconnue.";
            return false;
        }
        if (!Enum.IsDefined(request.Key))
        {
            error = "Clé de démarrage inconnue.";
            return false;
        }
        string name = request.ValueName ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxValueNameLength || name.Contains('\0'))
        {
            error = "Nom d'élément invalide.";
            return false;
        }
        if (request.Action == MachineStartupAction.Disable &&
            (request.ExpectedValue == null || request.ExpectedValue.Length > MaxValueLength))
        {
            error = "Valeur attendue absente ou trop longue.";
            return false;
        }
        return true;
    }

    public MachineStartupResult Apply(MachineStartupRequest request)
    {
        if (!Validate(request, out string error)) return new MachineStartupResult(false, error);
        try
        {
            return request.Action == MachineStartupAction.Disable
                ? Disable(request)
                : Enable(request);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return new MachineStartupResult(false, "Droits administrateur requis : " + ex.Message);
        }
    }

    private MachineStartupResult Disable(MachineStartupRequest request)
    {
        string targetPath = TargetPath(request.Key);
        string backupPath = BackupPath(request.Key);
        using RegistryKey? target = _root.OpenSubKey(targetPath, writable: true);
        if (target == null) return new MachineStartupResult(false, "Clé de démarrage introuvable.");

        object? raw = target.GetValue(request.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw == null) return new MachineStartupResult(false, "Cet élément n'existe plus.");
        RegistryValueKind kind = target.GetValueKind(request.ValueName);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            return new MachineStartupResult(false, "Type de valeur non pris en charge.");

        string expanded = Environment.ExpandEnvironmentVariables(raw.ToString() ?? "");
        if (!string.Equals(expanded, request.ExpectedValue, StringComparison.Ordinal))
            return new MachineStartupResult(false, "La commande a changé depuis l'analyse. Actualisez la liste.");

        using RegistryKey backup = _root.CreateSubKey(backupPath, writable: true);
        if (backup.GetValue(request.ValueName, null) != null)
            return new MachineStartupResult(false,
                "Une sauvegarde existe déjà pour cet élément. Réactivez-le d'abord.");

        backup.SetValue(request.ValueName, raw, kind);
        object? stored = backup.GetValue(request.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (!string.Equals(stored?.ToString(), raw.ToString(), StringComparison.Ordinal))
            return new MachineStartupResult(false, "La sauvegarde n'a pas pu être vérifiée.");

        try
        {
            target.DeleteValue(request.ValueName, throwOnMissingValue: false);
            if (target.GetValue(request.ValueName, null) != null)
                return new MachineStartupResult(false, "La valeur n'a pas pu être supprimée.");
        }
        catch
        {
            backup.DeleteValue(request.ValueName, throwOnMissingValue: false);
            throw;
        }
        return new MachineStartupResult(true, "Élément désactivé. Sauvegarde conservée dans le registre.");
    }

    private MachineStartupResult Enable(MachineStartupRequest request)
    {
        string targetPath = TargetPath(request.Key);
        string backupPath = BackupPath(request.Key);
        using RegistryKey? backup = _root.OpenSubKey(backupPath, writable: true);
        if (backup == null) return new MachineStartupResult(false, "Aucune sauvegarde pour cet élément.");

        object? raw = backup.GetValue(request.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw == null) return new MachineStartupResult(false, "Aucune sauvegarde pour cet élément.");
        RegistryValueKind kind = backup.GetValueKind(request.ValueName);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            return new MachineStartupResult(false, "Type de sauvegarde non pris en charge.");

        using RegistryKey target = _root.CreateSubKey(targetPath, writable: true);
        if (target.GetValue(request.ValueName, null) != null)
            return new MachineStartupResult(false,
                "Un élément du même nom existe déjà au démarrage. Supprimez-le ou renommez-le d'abord.");

        target.SetValue(request.ValueName, raw, kind);
        object? restored = target.GetValue(request.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (!string.Equals(restored?.ToString(), raw.ToString(), StringComparison.Ordinal))
            return new MachineStartupResult(false, "La réactivation n'a pas pu être vérifiée.");

        backup.DeleteValue(request.ValueName, throwOnMissingValue: false);
        return new MachineStartupResult(true, "Élément réactivé au démarrage.");
    }

    private string TargetPath(MachineStartupKey key) =>
        key == MachineStartupKey.RunOnce ? _runOncePath : _runPath;

    private string BackupPath(MachineStartupKey key) =>
        Path.Combine(_backupRoot, key == MachineStartupKey.RunOnce ? "RunOnce" : "Run");
}
