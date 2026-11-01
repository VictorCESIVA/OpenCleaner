using System.Runtime.Versioning;

namespace OpenCleaner.Core.Tools;

public static class WingetUpgradeService
{
    public const string WingetSource = "winget";
    public const string StoreSource = "msstore";
    public const string DefaultScope = "default";
    public const string UserScope = "user";
    public const string MachineScope = "machine";

    public static readonly IReadOnlyList<string> ListArguments =
    [
        "list",
        "--upgrade-available",
        "--include-unknown",
        "--accept-source-agreements",
        "--disable-interactivity"
    ];

    [SupportedOSPlatform("windows")]
    public static async Task<WingetUpgradeList> ListAsync(CancellationToken ct = default,
        IProgress<string>? progress = null, string executablePath = "winget")
    {
        WingetResult result = await WingetService.RunAsync(ListArguments, progress, ct, executablePath);
        string output = result.Output;
        if (!string.IsNullOrWhiteSpace(result.Error))
            output = string.IsNullOrEmpty(output) ? result.Error : output + Environment.NewLine + result.Error;
        return WingetUpgradeParser.Parse(output, result.ExitCode);
    }

    public static IReadOnlyList<string> BuildUpgradeArguments(WingetUpgradeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!WingetPackageId.IsValid(target.Id))
            throw new ArgumentException("Identifiant de paquet winget invalide.", nameof(target));
        if (!IsAllowedSource(target.Source))
            throw new ArgumentException("Source winget invalide.", nameof(target));
        if (!IsAllowedScope(target.Scope))
            throw new ArgumentException("Portée winget invalide.", nameof(target));

        string source = target.Source.ToLowerInvariant();
        string scope = target.Scope.ToLowerInvariant();
        var arguments = new List<string>
        {
            "upgrade",
            "--id",
            target.Id,
            "--exact",
            "--source",
            source
        };
        if (!string.Equals(scope, DefaultScope, StringComparison.Ordinal))
        {
            arguments.Add("--scope");
            arguments.Add(scope);
        }
        if (target.IncludeUnknown) arguments.Add("--include-unknown");
        arguments.Add("--silent");
        arguments.Add("--accept-package-agreements");
        arguments.Add("--accept-source-agreements");
        arguments.Add("--disable-interactivity");
        return arguments;
    }

    public static bool IsAllowedSource(string? source) =>
        string.Equals(source, WingetSource, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, StoreSource, StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedScope(string? scope) =>
        string.Equals(scope, DefaultScope, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, UserScope, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, MachineScope, StringComparison.OrdinalIgnoreCase);
}
