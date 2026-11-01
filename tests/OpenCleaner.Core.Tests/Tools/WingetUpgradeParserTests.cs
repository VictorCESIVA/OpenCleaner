using System.Text;
using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class WingetUpgradeParserTests
{
    private const string FrenchHeader =
        "Nom                                ID                                Version      Disponible      Source";

    [Fact]
    public void ParsesFrenchTable()
    {
        string output = $"""
            {FrenchHeader}
            ----------------------------------------------------------------------------------------
            Mozilla Firefox                    Mozilla.Firefox                   129.0        130.0           winget
            7-Zip                              7zip.7zip                        24.06        24.07           winget

            2 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output, 0);

        Assert.False(list.ParseFailed);
        Assert.Equal(0, list.ExitCode);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("Mozilla.Firefox", list.Items[0].Id);
        Assert.Equal("Mozilla Firefox", list.Items[0].Name);
        Assert.Equal("129.0", list.Items[0].CurrentVersion);
        Assert.Equal("130.0", list.Items[0].AvailableVersion);
        Assert.Equal("winget", list.Items[0].Source);
        Assert.False(list.Items[0].HasUnknownVersion);
    }

    [Fact]
    public void ParsesEnglishHeaderAndCaseInsensitiveIdToken()
    {
        string output = """
            Name                              Id                      Version    Available    Source
            -------------------------------------------------------------------------------------
            Notepad++                         Notepad++.Notepad++     8.6.9      8.7.1        winget

            1 upgrade available.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.False(list.ParseFailed);
        WingetUpgradeItem item = Assert.Single(list.Items);
        Assert.Equal("Notepad++.Notepad++", item.Id);
        Assert.Equal("Notepad++", item.Name);
    }

    [Fact]
    public void PreservesDoubleSpacesInsideNames()
    {
        string output = $"""
            {FrenchHeader}
            Mon  Application                   Contoso.MonApp                    1.0          2.0             winget

            1 mise à niveau disponible.
            """;

        WingetUpgradeItem item = Assert.Single(WingetUpgradeParser.Parse(output).Items);

        Assert.Equal("Mon  Application", item.Name);
        Assert.Equal("Contoso.MonApp", item.Id);
    }

    [Fact]
    public void FlagsTruncatedNames()
    {
        string output = $"""
            {FrenchHeader}
            Application au nom très long…     Contoso.LongApp                   1.0          2.0             winget

            1 mise à niveau disponible.
            """;

        WingetUpgradeItem item = Assert.Single(WingetUpgradeParser.Parse(output).Items);

        Assert.True(item.NameTruncated);
    }

    [Fact]
    public void MarksUnknownVersionsAndStoreSource()
    {
        string output = $"""
            {FrenchHeader}
            Application du Store              Contoso.StoreApp                  Inconnu(e)   —               msstore
            Autre application                 Contoso.OtherApp                  1.0          ?               winget

            2 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Items[0].HasUnknownVersion);
        Assert.Equal("msstore", list.Items[0].Source);
        Assert.True(list.Items[1].HasUnknownVersion);
    }

    [Fact]
    public void StripsAnsiAndBomAndCollectsWarnings()
    {
        string output = "\uFEFF\u001B[33mAvertissement : une source est ignorée.\u001B[0m\r\n" +
            FrenchHeader + "\r\n" +
            "Application                       Contoso.App                       1.0          2.0             winget\r\n" +
            "\r\n1 mise à niveau disponible.\r\n";

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        WingetUpgradeItem item = Assert.Single(list.Items);
        Assert.Equal("Application", item.Name);
        Assert.Contains(list.Warnings, warning => warning.Contains("Avertissement", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroUpgradeFooterReturnsEmptyList()
    {
        string output = $"""
            {FrenchHeader}

            0 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.Empty(list.Items);
        Assert.False(list.ParseFailed);
    }

    [Fact]
    public void NoMatchMessageReturnsEmptyList()
    {
        WingetUpgradeList list = WingetUpgradeParser.Parse(
            "Aucun paquet correspondant à l'entrée n'a été trouvé.");

        Assert.Empty(list.Items);
        Assert.False(list.ParseFailed);
    }

    [Fact]
    public void MalformedOutputIsReported()
    {
        WingetUpgradeList list = WingetUpgradeParser.Parse("sortie inattendue sans tableau");

        Assert.Empty(list.Items);
        Assert.True(list.ParseFailed);
    }

    [Fact]
    public void RowsWithInvalidOrTruncatedIdsAreRejected()
    {
        string output = $"""
            {FrenchHeader}
            Application invalide              contoso bad id                    1.0          2.0             winget
            Application tronquée              Contoso.App...                    1.0          2.0             winget
            Application valide                Contoso.Valid                     1.0          2.0             winget

            3 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        WingetUpgradeItem item = Assert.Single(list.Items);
        Assert.Equal("Contoso.Valid", item.Id);
    }

    [Fact]
    public void DeduplicatesByIdAndSource()
    {
        string output = $"""
            {FrenchHeader}
            Application                       Contoso.App                       1.0          2.0             winget
            Application                       contoso.app                       1.0          2.0             winget
            Application                       Contoso.App                       1.0          2.0             msstore

            3 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void CapsRowsAtOneThousand()
    {
        var output = new StringBuilder();
        output.AppendLine(FrenchHeader);
        for (int i = 0; i < 1005; i++)
            output.AppendLine($"Application {i}                    Contoso.App{i}                    1.0          2.0             winget");
        output.AppendLine();
        output.AppendLine("1005 mises à niveau disponibles.");

        WingetUpgradeList list = WingetUpgradeParser.Parse(output.ToString());

        Assert.Equal(WingetUpgradeParser.MaxRows, list.Items.Count);
        Assert.Contains(list.Warnings, warning => warning.Contains("tronquée", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindsTableWithoutHeaderWhenRowsAreValid()
    {
        string output = """
            Application                       Contoso.App                       1.0          2.0             winget

            1 mise à niveau disponible.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.False(list.ParseFailed);
        Assert.Single(list.Items);
    }

    [Fact]
    public void CapsWarningsAtOneHundred()
    {
        var output = new StringBuilder();
        for (int i = 0; i < 150; i++) output.AppendLine($"Avertissement {i} : source ignorée.");
        output.AppendLine(FrenchHeader);
        output.AppendLine();
        output.AppendLine("0 mises à niveau disponibles.");

        WingetUpgradeList list = WingetUpgradeParser.Parse(output.ToString());

        Assert.Equal(WingetUpgradeParser.MaxWarnings, list.Warnings.Count);
        Assert.All(list.Warnings, warning => Assert.True(warning.Length <= WingetUpgradeParser.MaxLineLength));
    }

    [Fact]
    public void TruncatesOverlongLines()
    {
        string output = "Avertissement " + new string('x', WingetUpgradeParser.MaxLineLength * 2) + "\n" +
            FrenchHeader + "\n\n0 mises à niveau disponibles.\n";

        WingetUpgradeList list = WingetUpgradeParser.Parse(output);

        Assert.All(list.Warnings, warning => Assert.True(warning.Length <= WingetUpgradeParser.MaxLineLength));
    }

    [Fact]
    public void EmptyOutputIsNotAParseFailure()
    {
        WingetUpgradeList list = WingetUpgradeParser.Parse("");

        Assert.Empty(list.Items);
        Assert.False(list.ParseFailed);
    }

    [Fact]
    public void ParsesRealWingetOutputWithSingleSpacedHeaderTail()
    {
        string output = """
            Nom                       ID                     Version Disponible Source
            --------------------------------------------------------------------------
            FACEIT                    FACEITLTD.FACEITClient 2.1.23  2.10.0     winget
            Roblox Player for noela   Roblox.Roblox          Unknown 0.745      winget
            2 mises à niveau disponibles.
            """;

        WingetUpgradeList list = WingetUpgradeParser.Parse(output, 0);

        Assert.False(list.ParseFailed);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("FACEITLTD.FACEITClient", list.Items[0].Id);
        Assert.Equal("2.1.23", list.Items[0].CurrentVersion);
        Assert.Equal("2.10.0", list.Items[0].AvailableVersion);
        Assert.True(list.Items[1].HasUnknownVersion);
    }
}
