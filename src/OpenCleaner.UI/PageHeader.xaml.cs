using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCleaner.UI;

/// <summary>
/// En-tête commun : titre, ligne d'explication, actions à droite.
/// Le contenu du contrôle est la zone d'actions, pour que les boutons nommés
/// restent dans la portée de la fenêtre.
/// </summary>
public class PageHeader : ContentControl
{
    public static readonly DependencyProperty EyebrowProperty =
        DependencyProperty.Register(nameof(Eyebrow), typeof(object), typeof(PageHeader));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(object), typeof(PageHeader));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(object), typeof(PageHeader));

    public static readonly DependencyProperty SubtitleForegroundProperty =
        DependencyProperty.Register(nameof(SubtitleForeground), typeof(Brush), typeof(PageHeader));

    public object? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Brush? SubtitleForeground
    {
        get => (Brush?)GetValue(SubtitleForegroundProperty);
        set => SetValue(SubtitleForegroundProperty, value);
    }
}
