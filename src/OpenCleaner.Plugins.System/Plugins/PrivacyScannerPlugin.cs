using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Text.RegularExpressions;

namespace OpenCleaner.Plugins.System.Plugins;

/// <summary>Heuristic alerts are reviewed explicitly; they never qualify for automatic cleanup.</summary>
public sealed class PrivacyScannerPlugin : ICleanerPlugin
{
    private readonly IFileGuardian _fileGuardian;
    private readonly ILogger<PrivacyScannerPlugin> _logger;
    private readonly string[] _roots;
    private readonly ScannedFileSelection _selection = new();
    private static readonly string[] SuspectNames = ["password", "passwd", "mdp", "pwd", "secret", "token",
        "apikey", "api_key", "credentials", "private_key"];
    private static readonly HashSet<string> TextExtensions = new([".log", ".txt", ".env", ".json", ".xml",
        ".yaml", ".yml", ".cfg", ".ini", ".config"], StringComparer.OrdinalIgnoreCase);
    private static readonly Regex SensitiveContent = new(
        @"(token\s*=|api_key\s*=|bearer\s+|password\s*=|Authorization\s*:|private_key\s*=|secret\s*=)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    public string Id => "privacy.scanner";
    public string Name => "Analyse de confidentialité";
    public string Description => "Signale les fichiers potentiellement sensibles pour examen manuel";
    public PluginCategory Category => PluginCategory.System;
    public RiskLevel MaxRiskLevel => RiskLevel.ExpertOnly;
    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool RequiresAdmin => false;

    public PrivacyScannerPlugin(IFileGuardian fileGuardian, ILogger<PrivacyScannerPlugin> logger)
        : this(fileGuardian, logger, [Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)]) { }

    internal PrivacyScannerPlugin(IFileGuardian fileGuardian, ILogger<PrivacyScannerPlugin> logger, IEnumerable<string> roots)
    {
        _fileGuardian = fileGuardian;
        _logger = logger;
        // Whole AppData contains active credentials/configuration; do not search it as disposable data.
        _roots = roots.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _selection.Reset();
        var items = new List<CleanableItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < _roots.Length; index++)
        {
            foreach (var file in PluginFileSafety.EnumerateFiles(_roots[index], "*", recursive: true, ct, maxDepth: 3))
            {
                ct.ThrowIfCancellationRequested();
                if (!visited.Add(file) || _fileGuardian.IsSystemCriticalPath(file)) continue;
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    var name = info.Name.ToLowerInvariant();
                    var suspect = SuspectNames.FirstOrDefault(p => name.Contains(p, StringComparison.Ordinal));
                    string? description = suspect != null ? $"Nom à vérifier — « {suspect} »" : null;
                    if (description == null && (TextExtensions.Contains(info.Extension) || name.StartsWith(".env", StringComparison.Ordinal)) &&
                        info.Length is > 0 and < 512_000)
                    {
                        using var reader = new StreamReader(file);
                        var buffer = new char[(int)Math.Min(info.Length, 102_400)];
                        var count = await reader.ReadAsync(buffer.AsMemory(), ct);
                        if (SensitiveContent.IsMatch(new string(buffer, 0, count)))
                            description = "Motif d'identifiant sensible détecté — vérifier avant toute suppression";
                    }
                    if (description != null)
                        items.Add(new CleanableItem(Guid.NewGuid().ToString(), file, info.Length, description,
                            RiskLevel.ExpertOnly, ItemType.File, info.LastAccessTime, Id));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
                {
                    _logger.LogDebug(ex, "Fichier non analysable : {Path}", file);
                }
            }
            progress?.Report((index + 1d) / _roots.Length);
        }
        ct.ThrowIfCancellationRequested();
        _selection.Publish(items);
        progress?.Report(1);
        return items;
    }

    public Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IBackupManager backupManager,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => _selection.CleanAsync(items, _fileGuardian, _logger, progress, ct);

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}
