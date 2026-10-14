namespace OpenCleaner.Core.Security;

/// <summary>Fail-closed checks shared by deletion and backup restoration.</summary>
internal static class PathSafety
{
    private static readonly HashSet<string> CriticalFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "ntuser.dat", "ntuser.dat.log1",
        "ntuser.dat.log2", "bootmgr", "bootnxt", "ntldr", "ntdetect.com"
    };

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute local path is required", nameof(path));

        // Extended/device paths and alternate data streams bypass normal path comparisons.
        if (OperatingSystem.IsWindows() &&
            (path.StartsWith(@"\\", StringComparison.Ordinal) || path.AsSpan(2).IndexOf(':') >= 0))
            throw new ArgumentException("Network, device and alternate stream paths are not supported", nameof(path));

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsWithin(string path, string directory) =>
        string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool HasReparsePoint(string path)
    {
        string? component = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(component))
        {
            try
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            // Inaccessible paths are unsafe: do not treat failed validation as permission to delete.
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            component = Path.GetDirectoryName(component);
        }
        return false;
    }

    public static bool IsCritical(string path)
    {
        string normalized;
        try { normalized = Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return true; }

        string root = Path.GetPathRoot(Path.GetFullPath(path))!;
        if (normalized.Length == 0 || string.Equals(normalized, root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return true;
        if (CriticalFileNames.Contains(Path.GetFileName(normalized)) || HasReparsePoint(normalized))
            return true;

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var criticalDirectories = new List<string>
        {
            @"C:\Windows\System32", @"C:\Windows\SysWOW64", @"C:\Program Files", @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft", @"C:\Boot", @"C:\Recovery", @"C:\$Recycle.Bin", @"C:\System Volume Information"
        };
        if (!string.IsNullOrEmpty(windows))
        {
            if (string.Equals(normalized, windows, StringComparison.OrdinalIgnoreCase)) return true;
            criticalDirectories.AddRange(new[] { "System32", "SysWOW64", "WinSxS", "servicing" }
                .Select(name => Path.Combine(windows, name)));
        }
        foreach (var specialFolder in new[] { Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.System,
                     Environment.SpecialFolder.SystemX86 })
        {
            string directory = Environment.GetFolderPath(specialFolder);
            if (!string.IsNullOrEmpty(directory)) criticalDirectories.Add(directory);
        }
        string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(commonData)) criticalDirectories.Add(Path.Combine(commonData, "Microsoft"));
        foreach (var applicationData in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            string directory = Environment.GetFolderPath(applicationData);
            if (!string.IsNullOrEmpty(directory)) criticalDirectories.Add(Path.Combine(directory, "OpenCleaner"));
        }
        criticalDirectories.AddRange(new[] { "Boot", "Recovery", "$Recycle.Bin", "System Volume Information" }
            .Select(name => Path.Combine(root, name)));
        if (criticalDirectories.Any(directory => IsWithin(normalized, directory))) return true;

        try
        {
            return (File.GetAttributes(normalized) & FileAttributes.System) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
