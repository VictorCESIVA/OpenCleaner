using OpenCleaner.Core.Settings;
using Xunit;

namespace OpenCleaner.Core.Tests.Settings;

public sealed class PreferencesStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _file;

    public PreferencesStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "preferences.json");
    }

    [Fact]
    public void MissingFileUsesDefaults()
    {
        AppPreferences preferences = PreferencesStore.Load(Path.Combine(_directory, "absent.json"));

        Assert.True(preferences.DarkTheme);
        Assert.Equal(AppPreferences.DefaultLowSpacePercent, preferences.LowSpaceThresholdPercent);
        Assert.True(preferences.BackgroundUpdateDownload);
    }

    [Fact]
    public void BackgroundUpdateDownloadDefaultsToTrue()
    {
        File.WriteAllText(_file, "{\"DarkTheme\":false}");

        Assert.True(PreferencesStore.Load(_file).BackgroundUpdateDownload);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BackgroundUpdateDownloadRoundTrips(bool enabled)
    {
        Assert.True(PreferencesStore.Save(
            new AppPreferences { BackgroundUpdateDownload = enabled }, _file));

        Assert.Equal(enabled, PreferencesStore.Load(_file).BackgroundUpdateDownload);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var expected = new AppPreferences { DarkTheme = false, LowSpaceThresholdPercent = 15, Language = "en" };

        Assert.True(PreferencesStore.Save(expected, _file));
        AppPreferences actual = PreferencesStore.Load(_file);

        Assert.False(actual.DarkTheme);
        Assert.Equal(15, actual.LowSpaceThresholdPercent);
        Assert.Equal("en", actual.Language);
    }

    [Fact]
    public void MissingLanguageDefaultsToFrench()
    {
        File.WriteAllText(_file, "{\"DarkTheme\":false,\"LowSpaceThresholdPercent\":10}");

        Assert.Equal("fr", PreferencesStore.Load(_file).Language);
    }

    [Theory]
    [InlineData("de", "fr")]
    [InlineData("EN", "en")]
    [InlineData(null, "fr")]
    public void LanguageIsNormalized(string? stored, string expected)
    {
        File.WriteAllText(_file, $"{{\"Language\":{(stored == null ? "null" : $"\"{stored}\"")}}}");

        Assert.Equal(expected, PreferencesStore.Load(_file).Language);
    }

    [Theory]
    [InlineData(0, AppPreferences.MinimumLowSpacePercent)]
    [InlineData(-20, AppPreferences.MinimumLowSpacePercent)]
    [InlineData(100, AppPreferences.MaximumLowSpacePercent)]
    [InlineData(17, 17)]
    public void ThresholdIsClamped(int stored, int expected)
    {
        File.WriteAllText(_file, $"{{\"DarkTheme\":true,\"LowSpaceThresholdPercent\":{stored}}}");

        Assert.Equal(expected, PreferencesStore.Load(_file).LowSpaceThresholdPercent);
    }

    [Fact]
    public void CorruptFileUsesDefaults()
    {
        File.WriteAllText(_file, "{ pas du json");

        AppPreferences preferences = PreferencesStore.Load(_file);

        Assert.True(preferences.DarkTheme);
        Assert.Equal(AppPreferences.DefaultLowSpacePercent, preferences.LowSpaceThresholdPercent);
    }

    [Fact]
    public void SaveNormalizesBeforeWriting()
    {
        Assert.True(PreferencesStore.Save(
            new AppPreferences { LowSpaceThresholdPercent = 99 }, _file));

        Assert.Equal(AppPreferences.MaximumLowSpacePercent, PreferencesStore.Load(_file).LowSpaceThresholdPercent);
    }

    [Fact]
    public void AccentRoundTrips()
    {
        Assert.True(PreferencesStore.Save(new AppPreferences { Accent = "azure" }, _file));

        AppPreferences actual = PreferencesStore.Load(_file);

        Assert.Equal("azure", actual.Accent);
        Assert.Null(actual.AccentHex);
    }

    [Theory]
    [InlineData("AZURE", "azure")]
    [InlineData("Mint", "mint")]
    [InlineData("", "mint")]
    [InlineData(null, "mint")]
    public void AccentIsNormalized(string? stored, string expected)
    {
        File.WriteAllText(_file, $"{{\"Accent\":{(stored == null ? "null" : $"\"{stored}\"")}}}");

        Assert.Equal(expected, PreferencesStore.Load(_file).Accent);
    }

    [Fact]
    public void InvalidAccentFallsBackToMint()
    {
        File.WriteAllText(_file, "{\"Accent\":\"not-a-theme\"}");

        Assert.Equal("mint", PreferencesStore.Load(_file).Accent);
    }

    [Fact]
    public void InvalidAccentHexIsCleared()
    {
        Assert.True(PreferencesStore.Save(
            new AppPreferences { Accent = "custom", AccentHex = "not-hex" }, _file));

        AppPreferences actual = PreferencesStore.Load(_file);

        Assert.Equal("mint", actual.Accent);
        Assert.Null(actual.AccentHex);
    }

    [Fact]
    public void LegacyJsonWithoutAccentUsesMint()
    {
        File.WriteAllText(_file, "{\"DarkTheme\":false,\"Language\":\"en\",\"LowSpaceThresholdPercent\":15}");

        AppPreferences actual = PreferencesStore.Load(_file);

        Assert.False(actual.DarkTheme);
        Assert.Equal("en", actual.Language);
        Assert.Equal("mint", actual.Accent);
        Assert.Null(actual.AccentHex);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("112233", "#112233")]
    [InlineData(" #fff ", "#FFFFFF")]
    public void CustomAccentHexIsNormalizedAndPreserved(string stored, string expected)
    {
        Assert.True(PreferencesStore.Save(
            new AppPreferences { Accent = "mint", AccentHex = stored }, _file));

        AppPreferences actual = PreferencesStore.Load(_file);

        Assert.Equal("custom", actual.Accent);
        Assert.Equal(expected, actual.AccentHex);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
