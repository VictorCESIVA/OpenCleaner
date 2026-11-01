using System.Reflection;
using OpenCleaner.Core.Theming;
using Xunit;

namespace OpenCleaner.Core.Tests.Settings;

public sealed class ThemePaletteTests
{
    public static IEnumerable<object[]> PresetsAndModes()
    {
        foreach (ThemePreset preset in ThemeCatalog.Presets)
        {
            yield return [preset.Id, true];
            yield return [preset.Id, false];
        }
    }

    [Theory]
    [MemberData(nameof(PresetsAndModes))]
    public void PresetAccentMeetsContrastRequirements(string accentId, bool dark)
    {
        ThemePalette palette = ThemePalette.Build(dark, accentId);

        AssertContrastAtLeast(palette.Accent, palette.BgMain);
        AssertContrastAtLeast(palette.Accent, palette.BgSelected);
        AssertContrastAtLeast(palette.AccentForeground, palette.Accent);
        AssertContrastAtLeast(palette.HeroText, palette.HeroStart);
        AssertContrastAtLeast(palette.HeroMuted, palette.HeroStart);
    }

    [Fact]
    public void AllColorTokensAreValidNonEmptyHex()
    {
        foreach (bool dark in new[] { true, false })
        {
            ThemePalette palette = ThemePalette.Build(dark, ThemeCatalog.DefaultId);
            foreach (PropertyInfo property in typeof(ThemePalette).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string)) continue;

                string value = Assert.IsType<string>(property.GetValue(palette));
                Assert.False(string.IsNullOrWhiteSpace(value), property.Name);
                Assert.True(ColorMath.TryParse(value, out _), property.Name);
            }
        }
    }

    [Fact]
    public void CustomHexIsUsedForDarkTheme()
    {
        ThemePalette palette = ThemePalette.Build(true, ThemeCatalog.CustomId, "#C9A227");

        Assert.Equal("#C9A227", palette.Accent);
        AssertContrastAtLeast(palette.Accent, palette.BgMain);
        AssertContrastAtLeast(palette.Accent, palette.BgSelected);
    }

    [Fact]
    public void CustomHexIsUsedForLightTheme()
    {
        ThemePalette palette = ThemePalette.Build(false, ThemeCatalog.CustomId, "#145B9C");

        Assert.Equal("#145B9C", palette.Accent);
        AssertContrastAtLeast(palette.Accent, palette.BgMain);
        AssertContrastAtLeast(palette.Accent, palette.BgSelected);
    }

    [Fact]
    public void CustomHexWithoutHashIsNormalized()
    {
        ThemePalette palette = ThemePalette.Build(true, ThemeCatalog.CustomId, "c9a227");

        Assert.Equal("#C9A227", palette.Accent);
    }

    [Fact]
    public void UnknownAccentFallsBackToMint()
    {
        ThemePalette expected = ThemePalette.Build(true, ThemeCatalog.DefaultId);
        ThemePalette actual = ThemePalette.Build(true, "does-not-exist");

        Assert.Equal(expected.Accent, actual.Accent);
        Assert.Equal(expected.BgSelected, actual.BgSelected);
    }

    [Fact]
    public void InvalidCustomHexFallsBackToPreset()
    {
        ThemePalette expected = ThemePalette.Build(true, ThemeCatalog.DefaultId);
        ThemePalette actual = ThemePalette.Build(true, ThemeCatalog.CustomId, "not-a-color");

        Assert.Equal(expected.Accent, actual.Accent);
    }

    [Fact]
    public void AccentLookupIsCaseInsensitive()
    {
        ThemePalette expected = ThemePalette.Build(true, "Azure");
        ThemePalette actual = ThemePalette.Build(true, "AZURE");

        Assert.Equal(expected.Accent, actual.Accent);
    }

    private static void AssertContrastAtLeast(string foreground, string background)
    {
        double ratio = ColorMath.ContrastRatio(ColorMath.Parse(foreground), ColorMath.Parse(background));
        Assert.True(ratio >= ThemePalette.MinimumContrast,
            $"Contrast between {foreground} and {background} was {ratio:F2}, below {ThemePalette.MinimumContrast}.");
    }
}
