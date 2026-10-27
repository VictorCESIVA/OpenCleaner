using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace OpenCleaner.Core.Tools;

public sealed record InstalledProgram(
    string Name,
    string? Version,
    string? Publisher,
    string? InstallLocation,
    string? UninstallString,
    long? EstimatedSizeBytes,
    bool MachineWide,
    bool Is64Bit)
{
    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString);
    public string SizeLabel => EstimatedSizeBytes is > 0 ? Contracts.SizeFormatter.Format(EstimatedSizeBytes.Value) : "";
    public string SourceLabel => MachineWide ? "Ordinateur" : "Utilisateur";
}

[SupportedOSPlatform("windows")]
public static class InstalledProgramService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<InstalledProgram> GetInstalledPrograms()
    {
        var programs = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        AddHive(programs, RegistryHive.LocalMachine, RegistryView.Registry64, machineWide: true, is64Bit: true);
        AddHive(programs, RegistryHive.LocalMachine, RegistryView.Registry32, machineWide: true, is64Bit: false);
        AddHive(programs, RegistryHive.CurrentUser, RegistryView.Registry64, machineWide: false, is64Bit: true);
        AddHive(programs, RegistryHive.CurrentUser, RegistryView.Registry32, machineWide: false, is64Bit: false);
        return programs.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool LaunchUninstall(InstalledProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!program.CanUninstall) return false;
        string command = program.UninstallString!;
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c " + command,
            UseShellExecute = true,
            WorkingDirectory = program.InstallLocation is { Length: > 0 } location && Directory.Exists(location)
                ? location : Environment.SystemDirectory
        };
        Process.Start(start)?.Dispose();
        return true;
    }

    private static void AddHive(Dictionary<string, InstalledProgram> programs, RegistryHive hive,
        RegistryView view, bool machineWide, bool is64Bit)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall == null) return;
            foreach (string subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? key = uninstall.OpenSubKey(subKeyName);
                    if (key == null) continue;
                    InstalledProgram? program = Read(key, machineWide, is64Bit);
                    if (program == null) continue;
                    string id = $"{program.Name}|{program.Version}|{program.Publisher}";
                    programs.TryAdd(id, program);
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                { }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { }
    }

    private static InstalledProgram? Read(RegistryKey key, bool machineWide, bool is64Bit)
    {
        string? name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (GetInt(key, "SystemComponent") == 1) return null;
        if (key.GetValue("ParentKeyName") != null) return null;
        string? releaseType = key.GetValue("ReleaseType") as string;
        if (string.Equals(releaseType, "Update", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(releaseType, "Hotfix", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(releaseType, "Security Update", StringComparison.OrdinalIgnoreCase)) return null;

        long? size = null;
        object? rawSize = key.GetValue("EstimatedSize");
        if (rawSize is int intSize && intSize > 0) size = intSize * 1024L;
        else if (rawSize is long longSize && longSize > 0) size = longSize * 1024L;
        return new InstalledProgram(
            name.Trim(),
            (key.GetValue("DisplayVersion") as string)?.Trim(),
            (key.GetValue("Publisher") as string)?.Trim(),
            (key.GetValue("InstallLocation") as string)?.Trim(),
            (key.GetValue("UninstallString") as string)?.Trim(),
            size,
            machineWide,
            is64Bit);
    }

    private static int? GetInt(RegistryKey key, string name)
    {
        object? value = key.GetValue(name);
        return value switch
        {
            int number => number,
            long number => (int)number,
            string text when int.TryParse(text, out int number) => number,
            _ => null
        };
    }
}
