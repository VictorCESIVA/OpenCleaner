namespace OpenCleaner.Contracts;

/// <summary>
/// Utilitaire partagé pour formater les tailles en octets vers un format lisible.
/// Utilisé par les plugins et l'UI — évite la duplication de code.
/// </summary>
public static class SizeFormatter
{
    private static string[] _units = ["o", "Ko", "Mo", "Go", "To"];

    /// <summary>Adapte les symboles d'unités à la langue de l'interface.</summary>
    public static void ConfigureUnits(string byteUnit, string kilobyte, string megabyte, string gigabyte,
        string terabyte)
    {
        ArgumentNullException.ThrowIfNull(byteUnit);
        ArgumentNullException.ThrowIfNull(kilobyte);
        ArgumentNullException.ThrowIfNull(megabyte);
        ArgumentNullException.ThrowIfNull(gigabyte);
        ArgumentNullException.ThrowIfNull(terabyte);
        Volatile.Write(ref _units, [byteUnit, kilobyte, megabyte, gigabyte, terabyte]);
    }

    public static string Format(long bytes)
    {
        string[] units = Volatile.Read(ref _units);
        return bytes switch
        {
            >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:0.##} {units[4]}",
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.##} {units[3]}",
            >= 1_048_576     => $"{bytes / 1_048_576.0:0.#} {units[2]}",
            >= 1_024         => $"{bytes / 1_024.0:0} {units[1]}",
            _                => $"{bytes} {units[0]}"
        };
    }
}
