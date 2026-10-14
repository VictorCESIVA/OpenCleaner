using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.Diagnostics;

namespace OpenCleaner.Plugins.System.Plugins;

public sealed class SteamCleanerPlugin : SafeFileCleanerPlugin
{
    public SteamCleanerPlugin(IFileGuardian fileGuardian, ILogger<SteamCleanerPlugin> logger) : base(fileGuardian, logger) { }
    public override string Id => "games.steam";
    public override string Name => "Caches Steam";
    public override string Description => "Caches et journaux anciens ; téléchargements en cours conservés";
    public override PluginCategory Category => PluginCategory.Applications;
    public override RiskLevel MaxRiskLevel => RiskLevel.Recommended;
    protected override bool CanClean() => !IsRunning("steam");
    internal static bool IsRunning(string name)
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(name);
            return processes.Length > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception) { return true; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    protected override IEnumerable<ScanTarget> GetTargets()
    {
        if (!CanClean()) yield break;
        var paths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Steam")
        };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            paths.Add(Path.Combine(drive.RootDirectory.FullName, "Steam"));
            paths.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam"));
        }
        foreach (var root in paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            yield return new ScanTarget(Path.Combine(root, "appcache", "httpcache"), "Cache HTTP Steam", MinAge: TimeSpan.FromDays(7));
            yield return new ScanTarget(Path.Combine(root, "logs"), "Journal Steam", MinAge: TimeSpan.FromDays(30));
            yield return new ScanTarget(Path.Combine(root, "appcache", "librarycache"), "Images Steam", MinAge: TimeSpan.FromDays(30));
            yield return new ScanTarget(Path.Combine(root, "steamapps", "shadercache"), "Cache shaders Steam",
                RiskLevel.Recommended, MinAge: TimeSpan.FromDays(30));
        }
    }
}
