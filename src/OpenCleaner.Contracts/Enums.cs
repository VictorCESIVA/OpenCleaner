namespace OpenCleaner.Contracts;

public enum RiskLevel
{
    Safe,           // Fichiers temporaires évidents, cache navigateur
    Recommended,    // Anciens logs, prefetch vieux
    ExpertOnly      // Registre, fichiers système legacy
}

public enum PluginCategory
{
    System,         // Windows temp, logs, event viewer
    Browser,        // Chrome, Firefox, Edge
    Applications,   // Steam, Office, etc.
    Registry,       // Nettoyage registre (ExpertOnly)
    Advanced        // Outils spéciaux (défragmentation, etc.)
}

public enum ItemType
{
    File,
    Directory,
    RegistryKey,
    RegistryValue
}
