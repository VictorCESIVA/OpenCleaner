using System.Buffers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;

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
        : this(fileGuardian, logger, [Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
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
        var published = new List<(CleanableItem Item, DateTime LastWriteUtc)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < _roots.Length; index++)
        {
            foreach (var info in PluginFileSafety.EnumerateFileInfos(_roots[index], "*", recursive: true, ct, maxDepth: 3))
            {
                ct.ThrowIfCancellationRequested();
                string file = info.FullName;
                if (!visited.Add(file) || !info.Exists) continue;
                try
                {
                    var name = info.Name;
                    string? suspect = null;
                    for (int s = 0; s < SuspectNames.Length; s++)
                    {
                        if (name.Contains(SuspectNames[s], StringComparison.OrdinalIgnoreCase))
                        {
                            suspect = SuspectNames[s];
                            break;
                        }
                    }

                    bool canInspectContent = suspect == null &&
                        info.Length is > 0 and < 512_000 &&
                        (TextExtensions.Contains(info.Extension) || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase));

                    if (suspect == null && !canInspectContent) continue;
                    if (_fileGuardian.IsSystemCriticalPath(file)) continue;

                    string? description = suspect != null ? $"Nom à vérifier — « {suspect} »" : null;
                    if (description == null && canInspectContent)
                    {
                        int maxChars = (int)Math.Min(info.Length, 102_400);
                        char[] buffer = ArrayPool<char>.Shared.Rent(maxChars);
                        try
                        {
                            using var reader = new StreamReader(file);
                            var count = await reader.ReadAsync(buffer.AsMemory(0, maxChars), ct);
                            if (SensitiveContent.IsMatch(buffer.AsSpan(0, count)))
                                description = "Motif d'identifiant sensible détecté — vérifier avant toute suppression";
                        }
                        finally
                        {
                            ArrayPool<char>.Shared.Return(buffer);
                        }
                    }
                    if (description != null)
                    {
                        var item = new CleanableItem(Guid.NewGuid().ToString(), file, info.Length, description,
                            RiskLevel.ExpertOnly, ItemType.File, info.LastAccessTime, Id);
                        items.Add(item);
                        published.Add((item, info.LastWriteTimeUtc));
                    }
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
        _selection.PublishKnown(published);
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
