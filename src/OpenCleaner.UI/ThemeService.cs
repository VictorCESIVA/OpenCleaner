using System.Windows;
using System.Windows.Media;
using OpenCleaner.Core.Settings;
using OpenCleaner.Core.Theming;

namespace OpenCleaner.UI;

/// <summary>
/// Applique la palette Core aux ressources WPF sans casser les liaisons DynamicResource.
/// </summary>
internal static class ThemeService
{
    public static ThemePalette Current { get; private set; } =
        ThemePalette.Build(dark: true, ThemeCatalog.DefaultId);

    public static event EventHandler<ThemePalette>? ThemeChanged;

    public static void Apply(AppPreferences preferences)
    {
        AppPreferences normalized = (preferences ?? new AppPreferences()).Normalize();
        Apply(normalized.DarkTheme, normalized.Accent, normalized.AccentHex);
    }

    public static void Apply(bool dark, string accent, string? accentHex)
    {
        ThemePalette palette = ThemePalette.Build(dark, accent, accentHex);
        Current = palette;
        if (Application.Current != null)
        {
            SetColor("Accent", palette.Accent);
            SetColor("AccentHover", palette.AccentHover);
            SetColor("AccentForeground", palette.AccentForeground);
            SetColor("AccentSoft", palette.AccentSoft);
            SetColor("BgMain", palette.BgMain);
            SetColor("BgSidebar", palette.BgSidebar);
            SetColor("BgCard", palette.BgCard);
            SetColor("BgHover", palette.BgHover);
            SetColor("BgSelected", palette.BgSelected);
            SetColor("HeroStart", palette.HeroStart);
            SetColor("HeroEnd", palette.HeroEnd);
            SetColor("HeroBorder", palette.HeroBorder);
            SetColor("HeroText", palette.HeroText);
            SetColor("HeroMuted", palette.HeroMuted);
            SetColor("TextPrimary", palette.TextPrimary);
            SetColor("TextSecond", palette.TextSecond);
            SetColor("Border", palette.Border);
            SetColor("RiskSafe", palette.RiskSafe);
            SetColor("RiskRecommended", palette.RiskRecommended);
            SetColor("RiskExpert", palette.RiskExpert);
            SetColor("SizeHuge", palette.SizeHuge);
            SetColor("SizeLarge", palette.SizeLarge);
            SetColor("SizeMed", palette.SizeMed);
            SetColor("SizeSmall", palette.SizeSmall);
            SetColor("WarnColor", palette.WarnColor);
            SetColor("Warning", palette.RiskRecommended);
            SetColor("Danger", palette.RiskExpert);
            SetColor("Violet", palette.IsDark ? "#BDA9EF" : "#6C4BA8");
            SetColor("VioletSoft", palette.IsDark ? "#18BDA9EF" : "#126C4BA8");
            SetHeroArtColors(palette);
            SetHeroGradient(palette);
        }

        ThemeChanged?.Invoke(null, palette);
    }

    /// <summary>Aperçu et tests : applique sans enregistrer les préférences.</summary>
    public static void ApplyPreview(bool dark, string accentId) => Apply(dark, accentId, null);

    private static void SetColor(string key, string hex)
    {
        Color color = (Color)ColorConverter.ConvertFromString(hex);
        ResourceDictionary resources = Application.Current.Resources;
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void SetHeroArtColors(ThemePalette palette)
    {
        RgbaColor accent = ColorMath.Parse(palette.Accent);
        RgbaColor heroStart = ColorMath.Parse(palette.HeroStart);
        RgbaColor heroText = ColorMath.Parse(palette.HeroText);
        RgbaColor white = RgbaColor.White;
        SetColor("HeroArtFolderFill", ColorMath.Format(ColorMath.Blend(accent, heroStart, 0.55)));
        SetColor("HeroArtFolderStroke", ColorMath.Format(ColorMath.Blend(accent, heroText, 0.35)));
        SetColor("HeroArtPaper", ColorMath.Format(ColorMath.Blend(accent, white, 0.08)));
        SetColor("HeroArtLine", ColorMath.Format(ColorMath.Blend(accent, heroStart, 0.35)));
        SetColor("HeroArtGradientStart", ColorMath.Format(ColorMath.Blend(accent, white, 0.25)));
        SetColor("HeroArtGradientEnd", ColorMath.Format(ColorMath.Blend(accent, heroStart, 0.20)));
        SetColor("HeroArtCheckFill", ColorMath.Format(ColorMath.Blend(accent, white, 0.55)));
        SetColor("HeroArtCheckStroke", ColorMath.Format(ColorMath.Blend(accent, heroText, 0.30)));
        SetColor("HeroArtCheckMark", ColorMath.Format(ColorMath.Blend(accent, heroText, 0.10)));
        SetColor("HeroArtSparkle", ColorMath.Format(ColorMath.Blend(accent, white, 0.35)));
        SetColor("HeroArtDot", palette.Accent);
        SetGradient("HeroArtGradient",
            ColorMath.Format(ColorMath.Blend(accent, white, 0.25)),
            ColorMath.Format(ColorMath.Blend(accent, heroStart, 0.20)));
    }

    private static void SetGradient(string key, string startHex, string endHex)
    {
        Color start = (Color)ColorConverter.ConvertFromString(startHex);
        Color end = (Color)ColorConverter.ConvertFromString(endHex);
        ResourceDictionary resources = Application.Current!.Resources;
        if (resources[key] is LinearGradientBrush { IsFrozen: false } gradient &&
            gradient.GradientStops.Count >= 2 &&
            !gradient.GradientStops[0].IsFrozen && !gradient.GradientStops[1].IsFrozen)
        {
            gradient.GradientStops[0].Color = start;
            gradient.GradientStops[1].Color = end;
            return;
        }

        var replacement = new LinearGradientBrush(start, end, new Point(0, 0), new Point(0.8, 1));
        replacement.Freeze();
        resources[key] = replacement;
    }

    private static void SetHeroGradient(ThemePalette palette)
    {
        Color start = (Color)ColorConverter.ConvertFromString(palette.HeroStart);
        Color end = (Color)ColorConverter.ConvertFromString(palette.HeroEnd);
        ResourceDictionary resources = Application.Current!.Resources;
        if (resources["HeroGradient"] is LinearGradientBrush { IsFrozen: false } gradient &&
            gradient.GradientStops.Count >= 2 &&
            !gradient.GradientStops[0].IsFrozen && !gradient.GradientStops[1].IsFrozen)
        {
            gradient.GradientStops[0].Color = start;
            gradient.GradientStops[1].Color = end;
            return;
        }

        var replacement = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 0.8));
        replacement.Freeze();
        resources["HeroGradient"] = replacement;
    }
}
