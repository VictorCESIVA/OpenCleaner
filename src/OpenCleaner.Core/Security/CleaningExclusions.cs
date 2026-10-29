using System.Text.Json;

namespace OpenCleaner.Core.Security;

/// <summary>
/// Dossiers que l'utilisateur exclut explicitement : aucun fichier sous ces chemins
/// ne doit être analysé ni supprimé. La politique est relue par le gardien et les plugins.
/// </summary>
public static class CleaningExclusions
{
    private const int MaxEntries = 200;
    private static readonly object Gate = new();
    private static readonly AsyncLocal<string[]?> TestOverride = new();
    private static string[] _global = [];

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenCleaner", "exclusions.json");

    public static IReadOnlyList<string> Paths => Volatile.Read(ref _global).ToArray();

    public static bool IsExcluded(string path)
    {
        string[] entries = TestOverride.Value ?? Volatile.Read(ref _global);
        if (entries.Length == 0 || !TryNormalize(path, out string candidate)) return false;
        foreach (string entry in entries)
        {
            if (candidate.Equals(entry, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = entry.EndsWith(Path.DirectorySeparatorChar) ||
                            entry.EndsWith(Path.AltDirectorySeparatorChar)
                ? entry
                : entry + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Charge la liste depuis le disque ; un fichier absent ou illisible vide la liste.</summary>
    public static void Load(string? path = null)
    {
        string file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
            {
                Volatile.Write(ref _global, []);
                return;
            }
            string[]? values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file));
            Volatile.Write(ref _global, NormalizeAll(values ?? []));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            Volatile.Write(ref _global, []);
        }
    }

    public static bool Add(string path)
    {
        if (!TryNormalize(path, out string normalized)) return false;
        lock (Gate)
        {
            string[] current = Volatile.Read(ref _global);
            if (current.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return true;
            var next = current.Append(normalized).Take(MaxEntries).ToArray();
            Volatile.Write(ref _global, next);
            return Save();
        }
    }

    public static bool Remove(string path)
    {
        if (!TryNormalize(path, out string normalized)) return false;
        lock (Gate)
        {
            string[] current = Volatile.Read(ref _global);
            var next = current.Where(entry =>
                !entry.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (next.Length == current.Length) return true;
            Volatile.Write(ref _global, next);
            return Save();
        }
    }

    /// <summary>Isole une liste d'exclusions dans le flux asynchrone courant (tests).</summary>
    public static IDisposable OverrideForTests(IEnumerable<string> paths)
    {
        string[]? previous = TestOverride.Value;
        TestOverride.Value = NormalizeAll(paths);
        return new Restore(previous);
    }

    private static bool Save()
    {
        try
        {
            string file = DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Volatile.Read(ref _global)));
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string[] NormalizeAll(IEnumerable<string> paths)
    {
        var values = new List<string>();
        foreach (string path in paths)
            if (TryNormalize(path, out string normalized) &&
                !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        return values.Take(MaxEntries).ToArray();
    }

    private static bool TryNormalize(string path, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return normalized.Length > 0 && Path.IsPathFullyQualified(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed class Restore(string[]? previous) : IDisposable
    {
        public void Dispose() => TestOverride.Value = previous;
    }
}
