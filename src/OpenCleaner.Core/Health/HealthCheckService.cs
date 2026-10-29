using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.Core.Health;

/// <summary>Collecte les mesures locales de santé. Lecture seule, sans élévation.</summary>
[SupportedOSPlatform("windows")]
public static class HealthCheckService
{
    public static HealthMetrics Collect(CacheSnapshot? cache = null) =>
        Collect(info: null, recycle: null, startup: null, cache: cache, rebootPending: null);

    /// <summary>
    /// Construit les mesures à partir de données déjà collectées (interface) ou les lit
    /// lui-même lorsque l'appelant ne les fournit pas.
    /// </summary>
    public static HealthMetrics Collect(SystemHealthInfo? info, RecycleBinInfo? recycle,
        IReadOnlyList<StartupItem>? startup, CacheSnapshot? cache = null, bool? rebootPending = null)
    {
        cache ??= CacheSnapshot.Empty;

        if (info == null)
        {
            try { info = SystemInfoService.Get(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }

        if (recycle == null)
        {
            try { recycle = RecycleBinService.Query(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }

        if (startup == null)
        {
            try { startup = StartupItemService.GetItems(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }

        IReadOnlyList<HealthDisk> disks = info?.Disks
            .Select(disk => new HealthDisk(disk.Name, disk.TotalBytes, disk.FreeBytes))
            .ToList() ?? [];
        long recycleBytes = Math.Max(0, recycle?.SizeBytes ?? 0);
        int recycleItems = (int)Math.Clamp(recycle?.ItemCount ?? 0, 0, int.MaxValue);

        return new HealthMetrics
        {
            Disks = disks,
            TotalMemoryBytes = info?.TotalMemoryBytes ?? 0,
            AvailableMemoryBytes = info?.AvailableMemoryBytes ?? 0,
            Uptime = info?.Uptime ?? TimeSpan.Zero,
            RecycleBinBytes = recycleBytes,
            RecycleBinItems = recycleItems,
            EnabledStartupItems = startup?.Count(item => item.IsEnabled) ?? 0,
            DisabledStartupItems = startup?.Count(item => !item.IsEnabled) ?? 0,
            TemporaryBytes = Math.Max(0, cache.TemporaryBytes),
            TemporaryItems = Math.Max(0, cache.TemporaryItems),
            BrowserCacheBytes = Math.Max(0, cache.BrowserCacheBytes),
            BrowserCacheItems = Math.Max(0, cache.BrowserCacheItems),
            RebootPending = rebootPending ?? IsRebootPending()
        };
    }

    /// <summary>Détecte un redémarrage requis par Windows Update, CBS ou un renommage en attente.</summary>
    public static bool IsRebootPending()
    {
        try
        {
            using (RegistryKey? cbs = Registry.LocalMachine.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
            using (RegistryKey? update = Registry.LocalMachine.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                if (cbs != null || update != null) return true;

            using RegistryKey? session = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager");
            return session?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
