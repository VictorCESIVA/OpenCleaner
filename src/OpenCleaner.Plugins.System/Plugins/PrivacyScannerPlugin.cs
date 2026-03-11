using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Text.RegularExpressions;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>
/// Analyse les dossiers utilisateur à la recherche de fichiers potentiellement
/// sensibles : noms suspects (password, token, secret…) et contenu (api_key=, bearer…).
/// </summary>
public class PrivacyScannerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<PrivacyScannerPlugin> _logger;

    public string Id          => "privacy.scanner";
    public string Name        => "Analyse de confidentialité";
    public string Description => "Détecte les fichiers potentiellement sensibles (mots de passe, tokens, clés API)";
    public PluginCategory Category    => PluginCategory.System;
    public RiskLevel MaxRiskLevel     => RiskLevel.ExpertOnly;
    public bool IsAvailable           => true;
    public bool RequiresAdmin         => false;

    // ─── Noms de fichiers suspects ─────────────────────────────────────────
    private static readonly string[] SuspectNamePatterns =
    [
        "password", "passwd", "mdp", "pwd", "secret", "token",
        "apikey", "api_key", "credentials", "auth", "private_key"
    ];

    // ─── Extensions à lire (texte court) ──────────────────────────────────
    private static readonly HashSet<string> ReadableExtensions =
        new(["log", "txt", "env", "json", "xml", "yaml", "yml", "cfg", "ini", "config"],
            StringComparer.OrdinalIgnoreCase);

    // ─── Patterns de contenu sensible ─────────────────────────────────────
    private static readonly Regex SensitiveContent = new(
        @"(token\s*=|api_key\s*=|bearer\s+|password\s*=|Authorization\s*:|private_key\s*=|secret\s*=)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─── Dossiers scannés ─────────────────────────────────────────────────
    private static readonly string[] ScanDirs =
    [
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    ];

    public PrivacyScannerPlugin(IFileGuardian fileGuardian, ILogger<PrivacyScannerPlugin> logger)
    {
        _fileGuardian = fileGuardian;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ANALYZE
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
    {
        var items = new List<CleanableItem>();
        var dirs  = ScanDirs.Where(Directory.Exists).ToArray();
        int done  = 0;

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();
            await ScanDirectory(dir, items, ct);
            done++;
            progress?.Report((double)done / dirs.Length);
        }

        _logger.LogInformation("Privacy scan : {Count} alertes", items.Count);
        return items;
    }

    private async Task ScanDirectory(string dir, List<CleanableItem> results, CancellationToken ct)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth     = 3,
                IgnoreInaccessible    = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible de scanner {Dir}", dir);
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (_fileGuardian.IsSystemCriticalPath(file)) continue;

            var name    = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var ext     = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            var info    = new FileInfo(file);
            if (!info.Exists) continue;

            // 1) Nom suspect
            var suspectName = SuspectNamePatterns.FirstOrDefault(p => name.Contains(p));
            if (suspectName != null)
            {
                results.Add(MakeItem(file, info.Length,
                    $"Nom suspect — contient « {suspectName} »",
                    RiskLevel.Recommended));
                continue;
            }

            // 2) Contenu suspect (fichiers texte < 500 Ko seulement)
            if (ReadableExtensions.Contains(ext) && info.Length is > 0 and < 512_000)
            {
                try
                {
                    var buffer = new char[Math.Min(info.Length, 102_400)]; // 100 Ko max
                    using var reader = new StreamReader(file);
                    int read = await reader.ReadAsync(buffer, ct);
                    var text = new string(buffer, 0, read);
                    var match = SensitiveContent.Match(text);
                    if (match.Success)
                    {
                        results.Add(MakeItem(file, info.Length,
                            $"Contenu sensible — motif « {match.Value.Trim()} »",
                            RiskLevel.ExpertOnly));
                    }
                }
                catch { /* fichier inaccessible ou binaire déguisé */ }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CLEAN
    // ─────────────────────────────────────────────────────────────────────

    public async Task<OperationResult> CleanAsync(
        IEnumerable<CleanableItem> items,
        IBackupManager backupManager,
        IProgress<double>? progress = null,
        CancellationToken  ct       = default)
    {
        var list = items.ToList();
        int ok   = 0;
        int i    = 0;

        foreach (var item in list)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _fileGuardian.SafeDeleteAsync(item.Path, createBackup: true, ct);
            if (result.Success) ok++;
            progress?.Report((double)++i / list.Count);
        }

        return new OperationResult(ok > 0,
            $"{ok}/{list.Count} fichiers sensibles supprimés (backup créé)",
            Guid.NewGuid());
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default) => Task.FromResult(0L);

    private static CleanableItem MakeItem(string path, long size, string desc, RiskLevel risk) =>
        new(Guid.NewGuid().ToString(), path, size, desc, risk, ItemType.File,
            new FileInfo(path).LastAccessTime, "privacy.scanner");
}
