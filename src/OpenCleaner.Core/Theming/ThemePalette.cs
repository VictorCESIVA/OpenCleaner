namespace OpenCleaner.Core.Theming;

public sealed record ThemePalette
{
    public const double MinimumContrast = 4.5d;

    public bool IsDark { get; init; }
    public required string Accent { get; init; }
    public required string AccentHover { get; init; }
    public required string AccentForeground { get; init; }
    public required string AccentSoft { get; init; }
    public required string BgMain { get; init; }
    public required string BgSidebar { get; init; }
    public required string BgCard { get; init; }
    public required string BgHover { get; init; }
    public required string BgSelected { get; init; }
    public required string HeroStart { get; init; }
    public required string HeroEnd { get; init; }
    public required string HeroBorder { get; init; }
    public required string HeroText { get; init; }
    public required string HeroMuted { get; init; }
    public required string TextPrimary { get; init; }
    public required string TextSecond { get; init; }
    public required string Border { get; init; }
    public required string RiskSafe { get; init; }
    public required string RiskRecommended { get; init; }
    public required string RiskExpert { get; init; }
    public required string SizeHuge { get; init; }
    public required string SizeLarge { get; init; }
    public required string SizeMed { get; init; }
    public required string SizeSmall { get; init; }
    public required string WarnColor { get; init; }

    public static ThemePalette Build(bool dark, string? accentId, string? accentHex = null)
    {
        ThemePreset preset = ThemeCatalog.Find(accentId);
        string accentSource = ColorMath.TryNormalizeHex(accentHex, out string customHex)
            ? customHex
            : dark ? preset.DarkAccent : preset.LightAccent;

        string bgMainHex = dark ? "#101215" : "#F5F7F8";
        string bgSidebarHex = dark ? "#15181C" : "#EDF1F2";
        string bgCardHex = dark ? "#191D23" : "#FFFFFF";
        string bgHoverHex = dark ? "#232931" : "#E6EBEE";
        string textPrimaryHex = dark ? "#F3F5F7" : "#202830";
        string textSecondHex = dark ? "#A4ADB9" : "#55616D";
        string borderHex = dark ? "#2B313A" : "#D2D8DD";

        RgbaColor accent = ColorMath.Parse(accentSource);
        RgbaColor bgMain = ColorMath.Parse(bgMainHex);
        HslColor accentHsl = ColorMath.RgbToHsl(accent);
        double selectedSaturation = Math.Clamp(
            accentHsl.S * (dark ? 0.50d : 0.45d),
            dark ? 0.18d : 0.12d,
            dark ? 0.40d : 0.30d);
        RgbaColor bgSelected = ColorMath.HslToRgb(accentHsl.H, selectedSaturation, dark ? 0.18d : 0.90d);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            accent = ColorMath.EnsureContrast(accent, bgMain, MinimumContrast);
            accent = ColorMath.EnsureContrast(accent, bgSelected, MinimumContrast);
            if (ColorMath.ContrastRatio(accent, bgMain) >= MinimumContrast &&
                ColorMath.ContrastRatio(accent, bgSelected) >= MinimumContrast) break;
        }

        accentHsl = ColorMath.RgbToHsl(accent);
        RgbaColor bgCard = ColorMath.Parse(bgCardHex);
        RgbaColor border = ColorMath.Parse(borderHex);

        double hoverSaturation = accentHsl.S * (dark ? 0.90d : 0.95d);
        double hoverLightness = Math.Clamp(accentHsl.L + (dark ? 0.08d : -0.06d), 0.12d, 0.92d);
        RgbaColor accentHover = ColorMath.HslToRgb(accentHsl.H, hoverSaturation, hoverLightness);
        RgbaColor accentSoft = accent with { A = (byte)(dark ? 0x14 : 0x12) };
        RgbaColor accentForeground = ColorMath.EnsureContrast(
            ColorMath.ReadableForeground(accent), accent, MinimumContrast);

        double heroSaturation = accentHsl.S * (dark ? 0.55d : 0.35d);
        RgbaColor heroStart = ColorMath.HslToRgb(accentHsl.H, heroSaturation, dark ? 0.19d : 0.93d);
        RgbaColor heroEnd = ColorMath.HslToRgb(accentHsl.H, heroSaturation, dark ? 0.13d : 0.96d);
        RgbaColor heroBorder = dark
            ? ColorMath.Blend(accent, border, 0.45d)
            : ColorMath.Blend(accent, bgCard, 0.55d);

        RgbaColor textPrimary = ColorMath.Parse(textPrimaryHex);
        RgbaColor heroNearBlack = ColorMath.Parse("#0B1117");
        RgbaColor heroText = ColorMath.ContrastRatio(textPrimary, heroStart) >=
                             ColorMath.ContrastRatio(heroNearBlack, heroStart)
            ? textPrimary
            : heroNearBlack;
        heroText = ColorMath.EnsureContrast(heroText, heroStart, MinimumContrast);
        RgbaColor heroMuted = ColorMath.EnsureContrast(
            ColorMath.BlendToward(heroText, heroStart, 0.72d), heroStart, MinimumContrast);

        return new ThemePalette
        {
            IsDark = dark,
            Accent = ColorMath.Format(accent),
            AccentHover = ColorMath.Format(accentHover),
            AccentForeground = ColorMath.Format(accentForeground),
            AccentSoft = ColorMath.Format(accentSoft, includeAlpha: true),
            BgMain = bgMainHex,
            BgSidebar = bgSidebarHex,
            BgCard = bgCardHex,
            BgHover = bgHoverHex,
            BgSelected = ColorMath.Format(bgSelected),
            HeroStart = ColorMath.Format(heroStart),
            HeroEnd = ColorMath.Format(heroEnd),
            HeroBorder = ColorMath.Format(heroBorder),
            HeroText = ColorMath.Format(heroText),
            HeroMuted = ColorMath.Format(heroMuted),
            TextPrimary = textPrimaryHex,
            TextSecond = textSecondHex,
            Border = borderHex,
            RiskSafe = dark ? "#85E0B4" : "#176649",
            RiskRecommended = dark ? "#F4C27A" : "#805211",
            RiskExpert = dark ? "#F5A0A4" : "#B7263B",
            SizeHuge = dark ? "#F5A0A4" : "#B7263B",
            SizeLarge = dark ? "#F4C27A" : "#805211",
            SizeMed = dark ? "#E7D88A" : "#746418",
            SizeSmall = dark ? "#85E0B4" : "#176649",
            WarnColor = dark ? "#F4C27A" : "#805211"
        };
    }
}
