namespace OpenCleaner.Core.Security;

/// <summary>Fail-closed checks shared by deletion and backup restoration.</summary>
internal static class PathSafety
{
    private static readonly HashSet<string> CriticalFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "ntuser.dat", "ntuser.dat.log1",
        "ntuser.dat.log2", "bootmgr", "bootnxt", "ntldr", "ntdetect.com"
    };

    private static readonly HashSet<string> RootCriticalFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boot", "Recovery", "$Recycle.Bin", "System Volume Information"
    };

    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static readonly string[] BaseCriticalDirectories = BuildBaseCriticalDirectories();

    private static string[] BuildBaseCriticalDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\System32", @"C:\Windows\SysWOW64", @"C:\Program Files", @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft", @"C:\Boot", @"C:\Recovery", @"C:\$Recycle.Bin", @"C:\System Volume Information"
        };

        if (!string.IsNullOrEmpty(WindowsDirectory))
        {
            foreach (string sub in new[] { "System32", "SysWOW64", "WinSxS", "servicing" })
                dirs.Add(Path.Combine(WindowsDirectory, sub));
        }

        foreach (var specialFolder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.System,
                     Environment.SpecialFolder.SystemX86
                 })
        {
            string directory = Environment.GetFolderPath(specialFolder);
            if (!string.IsNullOrEmpty(directory))
                dirs.Add(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(commonData))
            dirs.Add(Path.Combine(commonData, "Microsoft"));

        foreach (var applicationData in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            string directory = Environment.GetFolderPath(applicationData);
            if (!string.IsNullOrEmpty(directory))
                dirs.Add(Path.Combine(directory, "OpenCleaner"));
        }

        return [.. dirs];
    }

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

    public static bool IsWithin(string path, string directory)
    {
        ReadOnlySpan<char> dirSpan = directory.AsSpan().TrimEnd("\\/");
        if (dirSpan.IsEmpty) return false;
        ReadOnlySpan<char> pathSpan = path.AsSpan();
        if (pathSpan.Equals(dirSpan, StringComparison.OrdinalIgnoreCase)) return true;
        return pathSpan.Length > dirSpan.Length &&
               pathSpan.StartsWith(dirSpan, StringComparison.OrdinalIgnoreCase) &&
               (pathSpan[dirSpan.Length] == Path.DirectorySeparatorChar || pathSpan[dirSpan.Length] == Path.AltDirectorySeparatorChar);
    }

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

        string root = Path.GetPathRoot(normalized)!;
        ReadOnlySpan<char> rootTrimmed = root.AsSpan().TrimEnd("\\/");
        if (normalized.Length == 0 || normalized.AsSpan().Equals(rootTrimmed, StringComparison.OrdinalIgnoreCase))
            return true;
        if (CriticalFileNames.Contains(Path.GetFileName(normalized)) || HasReparsePoint(normalized))
            return true;

        if (!string.IsNullOrEmpty(WindowsDirectory) &&
            string.Equals(normalized, WindowsDirectory, StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.Length > root.Length)
        {
            ReadOnlySpan<char> relative = normalized.AsSpan(root.Length);
            int sepIndex = relative.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ReadOnlySpan<char> firstSegment = sepIndex >= 0 ? relative[..sepIndex] : relative;
            foreach (string protectedRoot in RootCriticalFolderNames)
            {
                if (firstSegment.Equals(protectedRoot.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        foreach (string directory in BaseCriticalDirectories)
        {
            if (IsWithin(normalized, directory))
                return true;
        }

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
