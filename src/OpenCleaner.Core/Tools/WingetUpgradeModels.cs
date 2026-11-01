namespace OpenCleaner.Core.Tools;

public sealed record WingetUpgradeItem(
    string Id,
    string Name,
    string CurrentVersion,
    string AvailableVersion,
    string Source,
    bool HasUnknownVersion,
    bool NameTruncated);

public sealed record WingetUpgradeList(
    IReadOnlyList<WingetUpgradeItem> Items,
    IReadOnlyList<string> Warnings,
    int ExitCode,
    bool ParseFailed);

public sealed record WingetUpgradeTarget(
    string Id,
    string Source = "winget",
    string Scope = "default",
    bool IncludeUnknown = false);

public sealed record WingetPackageUpdate(
    string Id,
    string Phase,
    int ExitCode,
    string? Message = null);

public sealed record WingetBatchResult(
    IReadOnlyList<WingetPackageUpdate> Packages,
    int Succeeded,
    int Failed,
    int Cancelled)
{
    public bool WasCancelled => Cancelled > 0;
}

public static class WingetUpgradePhase
{
    public const string Running = "running";
    public const string Output = "output";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
}
