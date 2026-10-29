using System.Reflection;
using OpenCleaner.UI.Localization;
using Xunit;

namespace OpenCleaner.UI.Tests;

public sealed class LocalizationCatalogTests
{
    private static string ReadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Ressource absente : " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static LocalizationCatalog French() =>
        LocalizationCatalog.Parse("fr", ReadResource("Localization.strings-fr.json"));

    private static LocalizationCatalog English() =>
        LocalizationCatalog.Parse("en", ReadResource("Localization.strings-en.json"));

    [Fact]
    public void FrenchAndEnglishCatalogsHaveIdenticalKeysAndParameters()
    {
        Assert.Empty(LocalizationCatalog.ValidatePair(French(), English()));
        Assert.Empty(LocalizationCatalog.ValidatePair(English(), French()));
    }

    [Fact]
    public void CatalogsCoverTheMigratedMainWindowKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Nav.Temp", "Nav.Settings", "Main.Title", "Main.SmartScan", "Main.Clean",
                     "Main.ConfirmClean", "Main.ItemsFound", "Risk.Low", "Size.Gigabyte",
                     "Settings.Language", "Plugin.Browser.Title"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheHealthKeysGeneratedByTheCore()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Health.Title", "Health.Section.Storage", "Health.Grade.Watch",
                     "Health.Storage.Low.Title", "Health.Storage.Low.Detail",
                     "Health.Memory.Pressure.Detail", "Health.Startup.Many.Detail",
                     "Health.Maintenance.Reboot.Title", "Health.Maintenance.Uptime.Days.Detail",
                     "Health.Cleanliness.Temporary.Detail", "Health.Cleanliness.Browser.Detail",
                     "Health.Cleanliness.Recycle.Detail", "Health.Action.Explore"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé santé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé santé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void ValuesAreFormattedWithTheCatalogCulture()
    {
        Assert.Contains("3", French().Format("Main.ItemsFound", 3, "1 Mo"));
        Assert.Contains("items", English().Format("Main.ItemsFound", 3, "1 MB"));
    }

    [Fact]
    public void MissingKeyFallsBackToTheKey() =>
        Assert.Equal("Missing.Key", French().Format("Missing.Key"));

    [Fact]
    public void ValidationDetectsMissingExtraAndPlaceholderMismatches()
    {
        var reference = LocalizationCatalog.Parse("fr",
            """{"A":"Texte","B":"Valeur {0}","C":"Seulement ici"}""");
        var translation = LocalizationCatalog.Parse("en",
            """{"A":"Text","B":"Value","D":"Extra"}""");

        IReadOnlyList<string> errors = LocalizationCatalog.ValidatePair(reference, translation);

        Assert.Contains(errors, error => error.Contains("B") && error.Contains("Paramètres"));
        Assert.Contains(errors, error => error.Contains("C") && error.Contains("absente"));
        Assert.Contains(errors, error => error.Contains("D") && error.Contains("superflue"));
    }

    [Fact]
    public void InvalidJsonIsRejected() =>
        Assert.ThrowsAny<Exception>(() => LocalizationCatalog.Parse("fr", "{ pas du json"));
}
