using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace OpenCleaner.Core.Tools;

public static class WingetPackageId
{
    public const int MaxLength = 200;
    private static readonly Regex ValidPattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._+~-]{0,199}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEqualityComparer<string> Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static bool IsValid([NotNullWhen(true)] string? id) =>
        !string.IsNullOrEmpty(id) && ValidPattern.IsMatch(id);

    public static IReadOnlyList<string> Distinct(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var seen = new HashSet<string>(Comparer);
        var result = new List<string>();
        foreach (string id in ids)
        {
            if (id != null && seen.Add(id)) result.Add(id);
        }
        return result;
    }
}
