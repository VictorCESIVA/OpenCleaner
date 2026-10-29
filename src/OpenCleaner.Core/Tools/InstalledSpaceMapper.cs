namespace OpenCleaner.Core.Tools;

/// <summary>Espace attribué à une application installée, mesuré dans l'arborescence ou estimé par le registre.</summary>
public sealed record InstalledSpaceEntry(
    string Name,
    string? Publisher,
    string Location,
    long Size,
    bool Measured)
{
    public bool Estimated => !Measured;
}

/// <summary>
/// Associe les applications installées aux dossiers déjà mesurés par l'analyse disque.
/// Aucun accès disque supplémentaire : un dossier absent de l'analyse retombe sur la
/// taille déclarée au registre, clairement marquée comme estimée.
/// </summary>
public static class InstalledSpaceMapper
{
    public static IReadOnlyList<InstalledSpaceEntry> Map(
        DiskUsageNode root, IEnumerable<InstalledProgram> programs, int maxEntries = 200)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(programs);
        if (maxEntries <= 0) return [];

        string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.FullPath));
        var entries = new List<InstalledSpaceEntry>();
        foreach (var program in programs)
        {
            if (string.IsNullOrWhiteSpace(program.Name) ||
                string.IsNullOrWhiteSpace(program.InstallLocation)) continue;
            if (!TryNormalize(program.InstallLocation, out string location)) continue;
            if (!IsUnderRoot(rootPath, location)) continue;
            // Attributing the whole scan root to one application would be misleading.
            if (string.Equals(location, rootPath, StringComparison.OrdinalIgnoreCase)) continue;

            DiskUsageNode? node = FindNode(root, location);
            long size = node?.Size ?? program.EstimatedSizeBytes ?? 0;
            if (size <= 0) continue;
            entries.Add(new InstalledSpaceEntry(program.Name.Trim(), program.Publisher, location, size,
                node != null));
        }

        return entries
            .OrderByDescending(entry => entry.Size)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxEntries)
            .ToList();
    }

    private static bool TryNormalize(string location, out string normalized)
    {
        try
        {
            string value = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(value))
            {
                normalized = string.Empty;
                return false;
            }
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            return normalized.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or IOException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool IsUnderRoot(string rootPath, string candidate)
    {
        if (string.Equals(candidate, rootPath, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = rootPath.EndsWith(Path.DirectorySeparatorChar) ||
                        rootPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static DiskUsageNode? FindNode(DiskUsageNode root, string location)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root.FullPath, location);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;

        var node = root;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Directories.FirstOrDefault(directory =>
                string.Equals(directory.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
        }
        return node;
    }
}
