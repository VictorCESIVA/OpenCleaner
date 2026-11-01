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
                     "Cleanup.Title", "Cleanup.WithoutBackupDetail", "Cleanup.DeleteWithoutBackup",
                     "Cleanup.BackupAndDelete", "Settings.ScheduleBackupHint",
                     "Settings.Language", "Plugin.Browser.Title"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheAccessibilityAutomationKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Main.ProgressAutomation", "Main.RiskFilterAutomation", "Main.ResultsAutomation",
                     "Nav.ThemeAutomation", "Common.ProgressAutomation", "Health.MemoryAutomation",
                     "Privacy.ListAutomation", "Dev.ListAutomation",
                     "Shred.ListAutomation", "Disk.DriveAutomation"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé d'accessibilité absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé d'accessibilité absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheAppearanceKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Theme.Mint", "Theme.Azure", "Theme.Violet", "Theme.Teal",
                     "Theme.Rose", "Theme.Graphite",
                     "Settings.AppearanceTitle", "Settings.AppearanceDesc",
                     "Settings.ThemeMode", "Settings.ThemeDark", "Settings.ThemeLight",
                     "Settings.Accent", "Settings.AccentAutomation", "Settings.AppearanceSaved"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé d'apparence absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé d'apparence absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheTruthfulScopeCopy()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Main.CookieScopeNote", "Dup.KeepOne", "Disk.LogicalSizeNote",
                     "App.CrashTitle", "App.CrashMessage"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé de portée absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé de portée absente de l'anglais : " + key);
        }

        // The duplicate finder designates a copy to keep; it never identifies an "original".
        Assert.DoesNotContain("origin", french.Format("Dup.GroupLabel", 2, "1 Mo"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", english.Format("Dup.GroupLabel", 2, "1 MB"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", french.Format("Dup.ConfirmDelete", 2), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", english.Format("Dup.ConfirmDelete", 2), StringComparison.OrdinalIgnoreCase);
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
    public void CatalogsCoverTheDataToolKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Restore.Title", "Restore.ConfirmDelete", "Restore.SummaryNone",
                     "Privacy.Title", "Privacy.ConfirmDelete", "Privacy.Progress",
                     "Dup.Title", "Dup.GroupLabel", "Dup.ConfirmDelete", "Dup.PreviewNone"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheLastWindowKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Dev.Title", "Dev.CacheNpm", "Dev.Confirm", "Dev.SafeDescription",
                     "Shred.Title", "Shred.Confirm", "Shred.Done",
                     "Recovery.CodeTitle", "Recovery.KeepHelp", "Recovery.EnterPrompt"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverApplicationAndUpdateMessages()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "App.Recovery.Pending", "App.Recovery.Incomplete", "App.Backup.Corrupt",
                     "App.Busy.Message", "App.AlreadyRunning",
                     "Update.Busy", "Update.NotInstalled", "Update.Downloading",
                     "Update.RestartConfirm", "Update.Error"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé absente de l'anglais : " + key);
        }
    }

    [Fact]
    public void CatalogsCoverTheVisualUpdatesWindowKeys()
    {
        var french = French();
        var english = English();
        foreach (string key in new[]
                 {
                     "Updates.SelectAll", "Updates.SelectNone", "Updates.UpdateSelected",
                     "Updates.Details", "Updates.DetailsEmpty", "Updates.Progress",
                     "Updates.ProgressAutomation", "Updates.ListAutomation", "Updates.DetailsAutomation",
                     "Updates.LogAutomation", "Updates.AlertAutomation", "Updates.StatusAutomation",
                     "Updates.SelectionAutomation", "Updates.Count.Selected", "Updates.Count.None",
                     "Updates.Source.Winget", "Updates.Source.Store", "Updates.UnknownVersion",
                     "Updates.Row.Ready", "Updates.Row.Unknown", "Updates.Row.Running",
                     "Updates.Row.Succeeded", "Updates.Row.Failed", "Updates.Row.FailedCode",
                     "Updates.Row.Cancelled", "Updates.Row.Skipped",
                     "Updates.Status.Initial", "Updates.Status.Found", "Updates.Status.UpToDate",
                     "Updates.Status.UpgradingOne", "Updates.Status.AllDone", "Updates.Status.Partial",
                     "Updates.Status.FailedCount", "Updates.Status.Cancelling", "Updates.Status.CheckFailed",
                     "Updates.NoSelection", "Updates.TooMany", "Updates.ConfirmSelected",
                     "Updates.State.InitialTitle", "Updates.State.InitialDescription",
                     "Updates.State.CheckingTitle", "Updates.State.CheckingDescription",
                     "Updates.State.NoUpdatesTitle", "Updates.State.NoUpdatesDescription",
                     "Updates.State.ParseFailedTitle", "Updates.State.ParseFailedDescription",
                     "Updates.State.CheckFailedTitle", "Updates.State.CheckFailedDescription",
                     "Updates.State.WingetMissingTitle", "Updates.State.ElevationDeniedTitle",
                     "Updates.State.ErrorTitle", "Updates.State.CancelledTitle",
                     "Updates.State.CancelledDescription", "Updates.Warnings",
                     "Updates.Preview.FailureDetail"
                 })
        {
            Assert.True(french.TryGet(key, out _), "Clé de mises à jour absente du français : " + key);
            Assert.True(english.TryGet(key, out _), "Clé de mises à jour absente de l'anglais : " + key);
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
