using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace OpenCleaner.Plugins.System.Plugins;

[SupportedOSPlatform("windows")]
public sealed class RegistryCleanerPlugin : ICleanerPlugin
{
    private sealed record ScannedKey(CleanableItem Item, string Fingerprint);
    private readonly IRegistryGuardian _registryGuardian;
    private readonly ILogger<RegistryCleanerPlugin> _logger;
    private Dictionary<string, ScannedKey> _selection = new(StringComparer.Ordinal);
    private static readonly string[] Targets =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StreamMRU",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU"
    ];
    public string Id => "registry.safe";
    public string Name => "Historiques du registre";
    public string Description => "Historiques utilisateur uniquement, après examen ; entrées de désinstallation conservées";
    public PluginCategory Category => PluginCategory.Registry;
    public RiskLevel MaxRiskLevel => RiskLevel.ExpertOnly;
    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool RequiresAdmin => false;

    public RegistryCleanerPlugin(IRegistryGuardian registryGuardian, ILogger<RegistryCleanerPlugin> logger)
    {
        _registryGuardian = registryGuardian;
        _logger = logger;
    }

    public Task<IReadOnlyList<CleanableItem>> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _selection = new(StringComparer.Ordinal);
        var items = new List<CleanableItem>();
        for (int index = 0; index < Targets.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Targets[index]);
                if (key != null)
                    foreach (var child in key.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        using var subkey = key.OpenSubKey(child);
                        if (subkey == null || subkey.SubKeyCount != 0) continue;
                        var item = new CleanableItem(Guid.NewGuid().ToString(),
                            $@"HKEY_CURRENT_USER\{Targets[index]}\{child}", 0, $"Historique utilisateur — {child}",
                            RiskLevel.ExpertOnly, ItemType.RegistryKey, ParentPluginId: Id);
                        _selection[item.Id] = new ScannedKey(item, Fingerprint(subkey, ct));
                        items.Add(item);
                    }
            }
            catch (OperationCanceledException) { _selection.Clear(); throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Security.SecurityException)
            {
                _logger.LogDebug(ex, "Historique inaccessible : {Key}", Targets[index]);
            }
            progress?.Report((index + 1d) / Targets.Length);
        }
        progress?.Report(1);
        return Task.FromResult<IReadOnlyList<CleanableItem>>(items);
    }

    public async Task<OperationResult> CleanAsync(IEnumerable<CleanableItem> items, IBackupManager backupManager,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var list = items.DistinctBy(i => i.Id).ToList();
        int success = 0;
        for (int index = 0; index < list.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var item = list[index];
            try
            {
                var allowed = Targets.Any(t => item.Path.StartsWith($@"HKEY_CURRENT_USER\{t}\",
                    StringComparison.OrdinalIgnoreCase));
                if (allowed && item.Type == ItemType.RegistryKey && _selection.TryGetValue(item.Id, out var scanned) &&
                    scanned.Item == item && !_registryGuardian.IsSystemCriticalKey(item.Path))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(item.Path[("HKEY_CURRENT_USER".Length + 1)..]);
                    if (key != null && Fingerprint(key, ct) == scanned.Fingerprint)
                    {
                        key.Dispose();
                        var result = await _registryGuardian.SafeDeleteKeyAsync(item.Path, backupFirst: true, ct);
                        if (result.Success) success++;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Historique non supprimé : {Key}", item.Path);
            }
            progress?.Report((index + 1d) / list.Count);
        }
        progress?.Report(1);
        return new OperationResult(success == list.Count, $"{success}/{list.Count} historiques supprimés",
            Guid.NewGuid(), ItemsProcessed: success, BytesRecovered: 0);
    }

    private static string Fingerprint(RegistryKey key, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) Write(key, writer, ct, 0);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(RegistryKey key, BinaryWriter writer, CancellationToken ct, int depth)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 32) throw new IOException("Registry history is unexpectedly deep.");
        writer.Write(key.Name);
        foreach (var name in key.GetValueNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            writer.Write(name);
            writer.Write((int)key.GetValueKind(name));
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var bytes = value switch
            {
                byte[] binary => binary,
                string[] strings => Encoding.UTF8.GetBytes(string.Join("\0", strings)),
                _ => Encoding.UTF8.GetBytes(value?.ToString() ?? "")
            };
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        foreach (var name in key.GetSubKeyNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            using var child = key.OpenSubKey(name);
            if (child != null) Write(child, writer, ct, depth + 1);
        }
    }

    public Task<long> EstimateSizeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}
