namespace OpenCleaner.UI;

/// <summary>
/// Vues dessinées manuellement qui doivent recalculer leurs couleurs quand le thème change.
/// </summary>
internal interface IThemeAware
{
    void ApplyTheme(bool dark);
}
