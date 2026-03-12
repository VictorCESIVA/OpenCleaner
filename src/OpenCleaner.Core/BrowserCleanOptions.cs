namespace OpenCleaner.Core;

/// <summary>
/// Options de nettoyage des navigateurs (Chrome, Edge, Brave).
/// Configuré par l'UI, lu par ChromeCleanerPlugin.
/// </summary>
public static class BrowserCleanOptions
{
    public static bool IncludeCache { get; set; } = true;
    public static bool IncludeCodeCache { get; set; } = true;
    public static bool IncludeGpuCache { get; set; } = true;
    public static bool IncludeServiceWorkers { get; set; } = true;
    public static bool IncludeCookies { get; set; } = false;
    public static bool IncludeHistory { get; set; } = false;
    public static bool IncludeFavicons { get; set; } = false;
}
