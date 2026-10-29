using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace OpenCleaner.Core.Tools;

public enum StartupItemLocation
{
    CurrentUserRun,
    CurrentUserRunOnce,
    LocalMachineRun,
    LocalMachineRunOnce,
    CurrentUserStartup,
    CommonStartup,
    DisabledByOpenCleaner,
    DisabledByOpenCleanerMachine
}

public sealed record StartupItem(
    string Id,
    string Name,
    string Command,
    StartupItemLocation Location,
    bool IsEnabled,
    bool CanToggle,
    string? RegistryPath = null,
    string? ValueName = null,
    string? FilePath = null,
    string? IconPath = null,
    StartupImpact Impact = StartupImpact.Unknown)
{
    public string StatusLabel => IsEnabled ? "Activé" : "Désactivé";
    public string ImpactLabel => "Impact estimé : " + StartupImpactEstimator.Label(Impact);
    public bool IsMachine => Location is StartupItemLocation.LocalMachineRun
        or StartupItemLocation.LocalMachineRunOnce or StartupItemLocation.DisabledByOpenCleanerMachine;
    public string LocationLabel => Location switch
    {
        StartupItemLocation.CurrentUserRun => "HKCU · Run",
        StartupItemLocation.CurrentUserRunOnce => "HKCU · RunOnce",
        StartupItemLocation.LocalMachineRun => "HKLM · Run (admin)",
        StartupItemLocation.LocalMachineRunOnce => "HKLM · RunOnce (admin)",
        StartupItemLocation.CurrentUserStartup => "Dossier Démarrage",
        StartupItemLocation.CommonStartup => "Démarrage commun (admin)",
        StartupItemLocation.DisabledByOpenCleanerMachine => "Désactivé · HKLM (admin)",
        _ => "Désactivé par OpenCleaner"
    };
}

[SupportedOSPlatform("windows")]
public static class StartupItemService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string DisabledRoot = @"Software\OpenCleaner\DisabledStartup";

    public static IReadOnlyList<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();
        AddRunItems(items, Registry.CurrentUser, RunPath, StartupItemLocation.CurrentUserRun, canToggle: true);
        AddRunItems(items, Registry.CurrentUser, RunOncePath, StartupItemLocation.CurrentUserRunOnce, canToggle: true);
        AddRunItems(items, Registry.LocalMachine, RunPath, StartupItemLocation.LocalMachineRun, canToggle: true);
        AddRunItems(items, Registry.LocalMachine, RunOncePath, StartupItemLocation.LocalMachineRunOnce, canToggle: true);
        AddStartupFiles(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            StartupItemLocation.CurrentUserStartup, canToggle: true);
        AddStartupFiles(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            StartupItemLocation.CommonStartup, canToggle: false);
        AddDisabledItems(items);
        AddMachineDisabledItems(items);
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Disable(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsEnabled || !item.CanToggle) return false;
        if (item.Location is StartupItemLocation.CurrentUserRun or StartupItemLocation.CurrentUserRunOnce)
            return DisableRunValue(item);
        if (item.Location == StartupItemLocation.CurrentUserStartup && item.FilePath != null)
            return DisableStartupFile(item.FilePath);
        if (item.Location is StartupItemLocation.LocalMachineRun or StartupItemLocation.LocalMachineRunOnce &&
            item.ValueName != null)
        {
            var request = new MachineStartupRequest(MachineStartupAction.Disable,
                item.Location == StartupItemLocation.LocalMachineRunOnce
                    ? MachineStartupKey.RunOnce
                    : MachineStartupKey.Run,
                item.ValueName, item.Command);
            return new MachineStartupService().Apply(request).Success;
        }
        return false;
    }

    public static bool Enable(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsEnabled || !item.CanToggle) return false;
        if (item.Location == StartupItemLocation.DisabledByOpenCleaner)
        {
            if (item.FilePath != null) return EnableStartupFile(item.FilePath);
            if (item.RegistryPath != null && item.ValueName != null) return EnableRunValue(item);
        }
        if (item.Location == StartupItemLocation.DisabledByOpenCleanerMachine && item.ValueName != null)
        {
            var request = new MachineStartupRequest(MachineStartupAction.Enable,
                item.RegistryPath == RunOncePath ? MachineStartupKey.RunOnce : MachineStartupKey.Run,
                item.ValueName);
            return new MachineStartupService().Apply(request).Success;
        }
        return false;
    }

    private static bool IsExecutable(string filePath) =>
        filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".com", StringComparison.OrdinalIgnoreCase);

    private static void AddRunItems(List<StartupItem> items, RegistryKey root, string path,
        StartupItemLocation location, bool canToggle)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(path);
            if (key == null) return;
            foreach (string name in key.GetValueNames())
            {
                string? command = key.GetValue(name)?.ToString();
                if (string.IsNullOrWhiteSpace(command)) continue;
                string? executable = IconPathResolver.ExtractExecutable(command) ??
                                     IconPathResolver.FromStartMenuShortcut(name);
                items.Add(new StartupItem($"{location}|{name}", name, command, location, true, canToggle,
                    path, name, IconPath: executable,
                    Impact: StartupImpactEstimator.Estimate(executable)));
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { }
    }

    private static void AddStartupFiles(List<StartupItem> items, string folder, StartupItemLocation location,
        bool canToggle)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new StartupItem($"{location}|{file}", name, file, location, true, canToggle,
                    FilePath: file, IconPath: IconPathResolver.FromFilePath(file),
                    Impact: IsExecutable(file) ? StartupImpactEstimator.Estimate(file) : StartupImpact.Unknown));
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { }
    }

    private static void AddMachineDisabledItems(List<StartupItem> items)
    {
        try
        {
            using RegistryKey? disabled = Registry.LocalMachine.OpenSubKey(MachineStartupService.MachineBackupRoot);
            if (disabled == null) return;
            foreach (string subKey in new[] { "Run", "RunOnce" })
            {
                using RegistryKey? key = disabled.OpenSubKey(subKey);
                if (key == null) continue;
                string originalPath = subKey == "Run"
                    ? MachineStartupService.MachineRunPath
                    : MachineStartupService.MachineRunOncePath;
                foreach (string name in key.GetValueNames())
                {
                    string? command = key.GetValue(name)?.ToString();
                    if (string.IsNullOrWhiteSpace(command)) continue;
                    string? executable = IconPathResolver.ExtractExecutable(command) ??
                                         IconPathResolver.FromStartMenuShortcut(name);
                    items.Add(new StartupItem($"disabled-machine|{subKey}|{name}", name, command,
                        StartupItemLocation.DisabledByOpenCleanerMachine, false, true, originalPath, name,
                        IconPath: executable,
                        Impact: StartupImpactEstimator.Estimate(executable)));
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { }
    }

    private static void AddDisabledItems(List<StartupItem> items)
    {
        try
        {
            using RegistryKey? disabled = Registry.CurrentUser.OpenSubKey(DisabledRoot);
            if (disabled != null)
            {
                foreach (string subKey in new[] { "Run", "RunOnce" })
                {
                    using RegistryKey? key = disabled.OpenSubKey(subKey);
                    if (key == null) continue;
                    string originalPath = subKey == "Run" ? RunPath : RunOncePath;
                    foreach (string name in key.GetValueNames())
                    {
                        string? command = key.GetValue(name)?.ToString();
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        string? executable = IconPathResolver.ExtractExecutable(command) ??
                                             IconPathResolver.FromStartMenuShortcut(name);
                        items.Add(new StartupItem($"disabled|{subKey}|{name}", name, command,
                            StartupItemLocation.DisabledByOpenCleaner, false, true, originalPath, name,
                            IconPath: executable,
                            Impact: StartupImpactEstimator.Estimate(executable)));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { }

        foreach ((string folder, bool canToggle) in new[]
                 {
                     (Environment.GetFolderPath(Environment.SpecialFolder.Startup), true),
                     (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), false)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.disabled"))
                {
                    string name = Path.GetFileName(file);
                    name = name[..^".disabled".Length];
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add(new StartupItem($"disabled|{file}", name, file,
                        StartupItemLocation.DisabledByOpenCleaner, false, canToggle, FilePath: file,
                        IconPath: IconPathResolver.FromFilePath(file) ??
                                  IconPathResolver.FromStartMenuShortcut(Path.GetFileNameWithoutExtension(name)),
                        Impact: IsExecutable(file) ? StartupImpactEstimator.Estimate(file) : StartupImpact.Unknown));
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            { }
        }
    }

    private static bool DisableRunValue(StartupItem item)
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(item.RegistryPath!, writable: true);
            if (run == null) return false;
            object? value = run.GetValue(item.ValueName!);
            if (value == null) return false;
            string subKey = item.RegistryPath == RunPath ? "Run" : "RunOnce";
            using (RegistryKey backup = Registry.CurrentUser.CreateSubKey($@"{DisabledRoot}\{subKey}"))
                backup.SetValue(item.ValueName!, value);
            run.DeleteValue(item.ValueName!, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    private static bool EnableRunValue(StartupItem item)
    {
        try
        {
            string subKey = item.RegistryPath == RunPath ? "Run" : "RunOnce";
            using RegistryKey? backup = Registry.CurrentUser.OpenSubKey($@"{DisabledRoot}\{subKey}", writable: true);
            if (backup == null) return false;
            object? value = backup.GetValue(item.ValueName!);
            if (value == null) return false;
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(item.RegistryPath!))
                run.SetValue(item.ValueName!, value);
            backup.DeleteValue(item.ValueName!, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    private static bool DisableStartupFile(string path)
    {
        try
        {
            if (!File.Exists(path) || File.Exists(path + ".disabled")) return false;
            File.Move(path, path + ".disabled");
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    private static bool EnableStartupFile(string path)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) return false;
            string target = path[..^".disabled".Length];
            if (File.Exists(target)) return false;
            File.Move(path, target);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }
}
