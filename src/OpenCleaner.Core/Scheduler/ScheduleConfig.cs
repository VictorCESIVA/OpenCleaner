namespace OpenCleaner.Core.Scheduler;

/// <summary>
/// Configuration persistée du planificateur automatique.
/// Sérialisée en JSON dans %AppData%\OpenCleaner\schedule.json
/// </summary>
public sealed record ScheduleConfig
{
    private static readonly HashSet<string> AutomaticPluginIds = new(StringComparer.Ordinal)
    {
        "system.temp", "browser.chrome", "system.thumbnailcache", "applications.office", "games.steam",
        "applications.caches"
    };
    public static bool IsAutomaticPlugin(string id) => AutomaticPluginIds.Contains(id);
    public bool         IsEnabled   { get; init; } = false;
    public TimeSpan     TimeOfDay   { get; init; } = new TimeSpan(3, 0, 0); // 03:00 par défaut
    public DayOfWeek[]  Days        { get; init; } = [DayOfWeek.Monday, DayOfWeek.Friday];
    public string[]     PluginIds   { get; init; } = ["system.temp"];

    public void Validate()
    {
        if (TimeOfDay < TimeSpan.Zero || TimeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(TimeOfDay), "The scheduled time must be within one day");
        if (Days == null || Days.Any(day => !Enum.IsDefined(day)) || (IsEnabled && Days.Length == 0))
            throw new ArgumentException("Select valid days for the schedule", nameof(Days));
        if (PluginIds == null || (IsEnabled && PluginIds.Length == 0) || PluginIds.Any(id =>
                string.IsNullOrWhiteSpace(id) || !IsAutomaticPlugin(id) || id.Length > 100 ||
                id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')))
            throw new ArgumentException("Invalid plugin identifiers for the schedule", nameof(PluginIds));
    }

    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenCleaner",
        "schedule.json");
}
