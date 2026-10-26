namespace OpenCleaner.Core.Security;

/// <summary>Fail-closed checks shared by deletion and backup restoration.</summary>
internal static class PathSafety
{
    private static readonly HashSet<string> CriticalFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "ntuser.dat", "ntuser.dat.log1",
        "ntuser.dat.log2", "usrclass.dat", "usrclass.dat.log1", "usrclass.dat.log2",
        "bootmgr", "bootnxt", "ntldr", "ntdetect.com"
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
            dirs.Add(commonData.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        string systemDrive = Path.GetPathRoot(WindowsDirectory) ??
            Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        string usersRoot = Path.Combine(systemDrive, "Users");
        string currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(usersRoot))
        {
            try
            {
                foreach (string profile in Directory.EnumerateDirectories(usersRoot))
                {
                    string normalized = profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.Equals(normalized, currentProfile, StringComparison.OrdinalIgnoreCase))
                        dirs.Add(normalized);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { }
        }

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
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c is '\0' or '\r' or '\n') ||
            !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute local path is required", nameof(path));

        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            if (!IsLocalDriveRoot(root))
                throw new ArgumentException("Network, device and volume paths are not supported", nameof(path));
            if (path.AsSpan(2).Contains(':'))
                throw new ArgumentException("Alternate data streams are not supported", nameof(path));
            ValidateLocalComponents(path, root.Length);
        }

        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (!IsLocalDriveRoot(root))
                throw new ArgumentException("Network, device and volume paths are not supported", nameof(path));
            ValidateLocalComponents(fullPath, root.Length);
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsLocalDriveRoot(string root) =>
        root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1] == ':' &&
        (root[2] == Path.DirectorySeparatorChar || root[2] == Path.AltDirectorySeparatorChar);

    private static void ValidateLocalComponents(string path, int rootLength)
    {
        if (rootLength >= path.Length) return;
        foreach (string segment in path[rootLength..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                throw new ArgumentException("Relative path components are not supported", nameof(path));
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new ArgumentException("Trailing spaces and dots are not supported", nameof(path));
            string stem = Path.GetFileNameWithoutExtension(segment);
            int tilde = stem.LastIndexOf('~');
            if (tilde >= 0 && tilde < stem.Length - 1 &&
                stem.AsSpan(tilde + 1).IndexOfAnyExcept("0123456789") < 0)
                throw new ArgumentException("Short (8.3) path aliases are not supported", nameof(path));
        }
    }

    public static bool IsSamePath(string left, string right)
    {
        try { return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
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

    public static bool IsCritical(string path) => IsCritical(path, checkReparse: true);

    internal static bool IsCritical(string path, bool checkReparse)
    {
        string normalized;
        try { normalized = Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return true; }

        string root = Path.GetPathRoot(normalized)!;
        ReadOnlySpan<char> rootTrimmed = root.AsSpan().TrimEnd("\\/");
        if (normalized.Length == 0 || normalized.AsSpan().Equals(rootTrimmed, StringComparison.OrdinalIgnoreCase))
            return true;
        if (CriticalFileNames.Contains(Path.GetFileName(normalized)) ||
            (checkReparse && HasReparsePoint(normalized)))
            return true;

        if (!string.IsNullOrEmpty(WindowsDirectory) && IsWithin(normalized, WindowsDirectory) &&
            !IsWithin(normalized, Path.Combine(WindowsDirectory, "Temp")))
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
            FileAttributes blocked = checkReparse
                ? FileAttributes.System
                : FileAttributes.System | FileAttributes.ReparsePoint;
            return (File.GetAttributes(normalized) & blocked) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
