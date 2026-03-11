namespace OpenCleaner.Contracts;

/// <summary>
/// Utilitaire partagé pour formater les tailles en octets vers un format lisible.
/// Utilisé par les plugins et l'UI — évite la duplication de code.
/// </summary>
public static class SizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.##} Go",
        >= 1_048_576     => $"{bytes / 1_048_576.0:0.#} Mo",
        >= 1_024         => $"{bytes / 1_024.0:0} Ko",
        _                => $"{bytes} o"
    };
}
