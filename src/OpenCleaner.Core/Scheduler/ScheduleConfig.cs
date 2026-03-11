namespace OpenCleaner.Core.Scheduler;

/// <summary>
/// Configuration persistée du planificateur automatique.
/// Sérialisée en JSON dans %AppData%\OpenCleaner\schedule.json
/// </summary>
public sealed record ScheduleConfig
{
    public bool         IsEnabled   { get; init; } = false;
    public TimeSpan     TimeOfDay   { get; init; } = new TimeSpan(3, 0, 0); // 03:00 par défaut
    public DayOfWeek[]  Days        { get; init; } = [DayOfWeek.Monday, DayOfWeek.Friday];
    public string[]     PluginIds   { get; init; } = ["system.temp"];

    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenCleaner",
        "schedule.json");
}
