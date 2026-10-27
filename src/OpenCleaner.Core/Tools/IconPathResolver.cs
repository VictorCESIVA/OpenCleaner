using System.IO;
using System.Security;

namespace OpenCleaner.Core.Tools;

internal static class IconPathResolver
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> StartMenuShortcuts =
        new(BuildStartMenuShortcuts);
    private static readonly HashSet<string> HostExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "conhost.exe",
        "cscript.exe",
        "explorer.exe",
        "msiexec.exe",
        "powershell.exe",
        "pwsh.exe",
        "rundll32.exe",
        "wscript.exe"
    };

    public static string? FromDisplayIcon(string? displayIcon, string? uninstallCommand)
    {
        string? icon = NormalizeDisplayIcon(displayIcon);
        if (icon != null) return icon;

        string? executable = ExtractExecutable(uninstallCommand);
        return executable != null && File.Exists(executable) ? executable : null;
    }

    public static string? FromStartMenuShortcut(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        return StartMenuShortcuts.Value.TryGetValue(displayName.Trim(), out string? path) &&
               File.Exists(path) ? path : null;
    }

    public static string? FromFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string value = path.Trim();
        if (value.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            string original = value[..^".disabled".Length];
            return File.Exists(original) ? original : null;
        }
        return File.Exists(value) ? value : null;
    }

    public static string? NormalizeDisplayIcon(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        string value = Environment.ExpandEnvironmentVariables(location.Trim());

        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            if (end > 1) value = value[1..end];
        }
        else
        {
            int comma = value.LastIndexOf(',');
            if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), out _))
                value = value[..comma];

            int exeEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd > 0)
            {
                exeEnd += ".exe".Length;
                if (exeEnd < value.Length) value = value[..exeEnd];
            }
        }

        value = value.Trim().Trim('"');
        return value.Length > 0 && File.Exists(value) ? value : null;
    }

    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string value = Environment.ExpandEnvironmentVariables(command.Trim());

        string? candidate;
        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            candidate = end > 1 ? value[1..end] : null;
        }
        else
        {
            int exeEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd > 0)
                candidate = value[..(exeEnd + ".exe".Length)];
            else
            {
                int space = value.IndexOf(' ');
                candidate = space > 0 ? value[..space] : value;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate)) return null;
        candidate = candidate.Trim().Trim('"');
        if (!File.Exists(candidate)) return null;
        return HostExecutables.Contains(Path.GetFileName(candidate)) ? null : candidate;
    }

    private static IReadOnlyDictionary<string, string> BuildStartMenuShortcuts()
    {
        var shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.lnk", options))
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (key.Length > 0) shortcuts.TryAdd(key, file);
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            { }
        }
        return shortcuts;
    }
}
