using System.Text;
using System.Text.RegularExpressions;

namespace OpenCleaner.Core.Tools;

public static class WingetUpgradeParser
{
    public const int MaxRows = 1000;
    public const int MaxLineLength = 8192;
    public const int MaxWarnings = 100;
    public const int MaxWarningBytes = 16 * 1024;

    private static readonly Regex AnsiCsi = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex AnsiOsc = new(@"\x1B\][^\x07]*(?:\x07|\x1B\\)", RegexOptions.Compiled);
    private static readonly Regex ColumnSeparator = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex Footer = new(@"^\s*\d+\s+.*\.\s*$", RegexOptions.Compiled);
    private static readonly Regex EllipsisSuffix = new(@"(?:\.\.\.|…)\s*$", RegexOptions.Compiled);

    private static readonly string[] UnknownVersions =
    [
        "", "unknown", "inconnu", "inconnue", "inconnu(e)", "?", "—", "–", "-", "n/a", "n/d"
    ];

    private static readonly string[] NoMatchMessages =
    [
        "no installed package found",
        "no applicable update",
        "no applicable upgrade",
        "no upgrades available",
        "no update available",
        "aucun paquet",
        "aucune mise à niveau",
        "aucune mise a jour",
        "aucune mise à jour",
        "aucun logiciel",
        "aucune application"
    ];

    public static WingetUpgradeList Parse(string? output) => Parse(output, 0);

    public static WingetUpgradeList Parse(string? output, int exitCode)
    {
        string text = StripControl(output ?? "");
        string[] lines = SplitLines(text);
        var warnings = new List<string>();
        int warningBytes = 0;

        void AddWarning(string value)
        {
            if (warnings.Count >= MaxWarnings) return;
            int bytes = Encoding.UTF8.GetByteCount(value) + 1;
            if (warningBytes + bytes > MaxWarningBytes) return;
            warnings.Add(value);
            warningBytes += bytes;
        }

        int headerIndex = -1;
        int fallbackIndex = -1;
        ColumnLayout layout = default;
        bool hasLayout = false;
        bool noMatch = false;
        bool zeroUpgrades = false;
        bool hasContent = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) continue;
            hasContent = true;

            if (IsNoMatch(line))
            {
                noMatch = true;
                continue;
            }
            if (Footer.IsMatch(line))
            {
                zeroUpgrades = ReadFooterCount(line) == 0;
                break;
            }
            if (IsWarning(line)) AddWarning(line);
            if (headerIndex < 0 && IsHeader(line))
            {
                headerIndex = i;
                hasLayout = TryGetColumnLayout(line, out layout);
                continue;
            }
            if (headerIndex < 0 && fallbackIndex < 0 && TryParseRow(line, out _))
                fallbackIndex = i;
        }

        if (noMatch || zeroUpgrades) return new WingetUpgradeList([], warnings, exitCode, false);

        int start = headerIndex >= 0 ? headerIndex + 1 : fallbackIndex;
        if (start < 0)
            return new WingetUpgradeList([], warnings, exitCode, hasContent);

        var items = new List<WingetUpgradeItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool rowsTruncated = false;

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) continue;
            if (Footer.IsMatch(line) || IsNoMatch(line)) break;
            if (IsWarning(line)) AddWarning(line);
            if (!TryParseRow(line, hasLayout ? layout : null, out WingetUpgradeItem item)) continue;
            if (items.Count >= MaxRows)
            {
                rowsTruncated = true;
                break;
            }
            if (seen.Add(item.Id + "\0" + item.Source)) items.Add(item);
        }

        if (rowsTruncated) AddWarning($"Liste tronquée à {MaxRows} mises à niveau.");
        return new WingetUpgradeList(items, warnings, exitCode, false);
    }

    private readonly record struct ColumnLayout(int Id, int Version, int Available, int Source)
    {
        public bool IsValid => Id > 0 && Version > Id && Available > Version && Source > Available;
    }

    internal static bool TryParseRow(string line, out WingetUpgradeItem item) =>
        TryParseRow(line, null, out item);

    private static bool TryParseRow(string line, ColumnLayout? layout, out WingetUpgradeItem item)
    {
        item = null!;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || Footer.IsMatch(trimmed)) return false;
        if (layout.HasValue && layout.Value.IsValid && TrySliceRow(line, layout.Value, out item)) return true;

        string[] tokens = ColumnSeparator.Split(trimmed);
        if (tokens.Length < 5) return false;

        string source = tokens[^1].Trim();
        if (!WingetUpgradeService.IsAllowedSource(source)) return false;

        string id = tokens[^4].Trim();
        if (!WingetPackageId.IsValid(id) || EllipsisSuffix.IsMatch(id)) return false;

        string current = tokens[^3].Trim();
        string available = tokens[^2].Trim();
        string name = string.Join("  ", tokens[..^4]).Trim();
        if (name.Length == 0) return false;

        item = new WingetUpgradeItem(
            id,
            name,
            current,
            available,
            source.ToLowerInvariant(),
            IsUnknownVersion(current) || IsUnknownVersion(available),
            EllipsisSuffix.IsMatch(name));
        return true;
    }

    private static bool TrySliceRow(string line, ColumnLayout layout, out WingetUpgradeItem item)
    {
        item = null!;
        if (line.Length < layout.Source) return false;

        string name = line[..layout.Id].Trim();
        string id = line[layout.Id..layout.Version].Trim();
        string current = line[layout.Version..layout.Available].Trim();
        string available = line[layout.Available..layout.Source].Trim();
        string source = line[layout.Source..].Trim();
        if (name.Length == 0 || !WingetUpgradeService.IsAllowedSource(source)) return false;
        if (!WingetPackageId.IsValid(id) || EllipsisSuffix.IsMatch(id)) return false;

        item = new WingetUpgradeItem(
            id,
            name,
            current,
            available,
            source.ToLowerInvariant(),
            IsUnknownVersion(current) || IsUnknownVersion(available),
            EllipsisSuffix.IsMatch(name));
        return true;
    }

    private static bool TryGetColumnLayout(string header, out ColumnLayout layout)
    {
        layout = default;
        string trimmed = header.TrimEnd();
        int id = FindLabel(trimmed, "ID", "Identifiant");
        int version = FindLabel(trimmed, "Version");
        int available = FindLabel(trimmed, "Disponible", "Available");
        int source = trimmed.LastIndexOf(' ') + 1;
        if (id <= 0 || version <= id || available <= version || source <= available) return false;

        layout = new ColumnLayout(id, version, available, source);
        return true;
    }

    private static int FindLabel(string line, params string[] labels)
    {
        foreach (string label in labels)
        {
            Match match = Regex.Match(line, $@"\b{Regex.Escape(label)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) return match.Index;
        }
        return -1;
    }

    private static string StripControl(string text)
    {
        if (text.Length == 0) return text;
        text = text.TrimStart('\uFEFF');
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = AnsiCsi.Replace(text, "");
        text = AnsiOsc.Replace(text, "");
        return text.TrimStart('\uFEFF');
    }

    private static string[] SplitLines(string text)
    {
        string[] raw = text.Split('\n');
        var lines = new string[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i];
            if (line.Length > MaxLineLength) line = line[..MaxLineLength];
            lines[i] = line.Trim();
        }
        return lines;
    }

    private static bool IsHeader(string line)
    {
        // The header uses single spaces between its trailing labels (for example
        // "Version Disponible Source"), while data rows use two or more spaces.
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4) return false;
        if (WingetUpgradeService.IsAllowedSource(tokens[^1])) return false;
        return tokens.Any(token =>
            string.Equals(token.Trim(), "ID", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token.Trim(), "Identifiant", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNoMatch(string line) =>
        NoMatchMessages.Any(message => line.Contains(message, StringComparison.OrdinalIgnoreCase));

    private static int ReadFooterCount(string line)
    {
        string trimmed = line.TrimStart();
        int length = 0;
        while (length < trimmed.Length && char.IsAsciiDigit(trimmed[length])) length++;
        return int.TryParse(trimmed.AsSpan(0, length), out int value) ? value : -1;
    }

    private static bool IsWarning(string line)
    {
        string value = line.TrimStart();
        return value.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("avertissement", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("attention", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("erreur", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("échec", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("echec", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("impossible", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknownVersion(string value)
    {
        string normalized = value.Trim();
        return UnknownVersions.Any(unknown =>
            string.Equals(unknown, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
