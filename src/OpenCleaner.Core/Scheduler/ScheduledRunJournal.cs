using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCleaner.Core.Scheduler;

public sealed record ScheduledRunRecord(
    DateTime CompletedAt,
    int FilesCleaned,
    long BytesProcessed,
    int Failures)
{
    [JsonIgnore]
    public bool Success => Failures == 0;
}

/// <summary>
/// Journal local des exécutions planifiées (une ligne JSON par exécution).
/// Il ne contient aucun chemin de fichier, seulement compteurs, volume et date.
/// </summary>
public static class ScheduledRunJournal
{
    private const int MaxRecords = 200;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenCleaner", "scheduled-runs.jsonl");

    public static void Append(ScheduledRunRecord record, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        string file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string line = JsonSerializer.Serialize(record);
            File.AppendAllText(file, line + Environment.NewLine);
            Trim(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Le journal est un confort : son échec ne doit pas interrompre le nettoyage.
        }
    }

    public static IReadOnlyList<ScheduledRunRecord> ReadLatest(int maxRecords = 5, string? path = null)
    {
        if (maxRecords <= 0) return [];
        string file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return [];
            var records = new List<ScheduledRunRecord>();
            foreach (string line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    ScheduledRunRecord? record = JsonSerializer.Deserialize<ScheduledRunRecord>(line);
                    if (record != null) records.Add(record);
                }
                catch (JsonException) { }
            }
            return records
                .OrderByDescending(record => record.CompletedAt)
                .Take(maxRecords)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    private static void Trim(string file)
    {
        try
        {
            string[] lines = File.ReadAllLines(file);
            if (lines.Length <= MaxRecords) return;
            string temporary = file + ".tmp";
            File.WriteAllLines(temporary, lines[^MaxRecords..]);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }
}
