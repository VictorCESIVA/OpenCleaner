using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class WingetUpgradeServiceTests
{
    [Fact]
    public void BuildsKnownPackageCommand()
    {
        IReadOnlyList<string> arguments = WingetUpgradeService.BuildUpgradeArguments(
            new WingetUpgradeTarget("Microsoft.VisualStudioCode"));

        Assert.Equal(
            [
                "upgrade", "--id", "Microsoft.VisualStudioCode", "--exact", "--source", "winget",
                "--silent", "--accept-package-agreements", "--accept-source-agreements",
                "--disable-interactivity"
            ],
            arguments);
    }

    [Fact]
    public void AddsIncludeUnknown()
    {
        IReadOnlyList<string> arguments = WingetUpgradeService.BuildUpgradeArguments(
            new WingetUpgradeTarget("Contoso.App", IncludeUnknown: true));

        Assert.Contains("--include-unknown", arguments);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("machine")]
    public void AddsExplicitScope(string scope)
    {
        IReadOnlyList<string> arguments = WingetUpgradeService.BuildUpgradeArguments(
            new WingetUpgradeTarget("Contoso.App", Scope: scope));

        int index = arguments.ToList().IndexOf("--scope");
        Assert.True(index >= 0);
        Assert.Equal(scope, arguments[index + 1]);
    }

    [Fact]
    public void OmitsDefaultScope() =>
        Assert.DoesNotContain("--scope",
            WingetUpgradeService.BuildUpgradeArguments(new WingetUpgradeTarget("Contoso.App")));

    [Fact]
    public void AcceptsStoreSource() =>
        Assert.Contains("msstore", WingetUpgradeService.BuildUpgradeArguments(
            new WingetUpgradeTarget("Contoso.App", Source: "msstore")));

    [Theory]
    [InlineData("--all")]
    [InlineData("Contoso App")]
    [InlineData("Contoso.App;calc")]
    [InlineData("")]
    public void RejectsHostileIds(string id) =>
        Assert.Throws<ArgumentException>(() =>
            WingetUpgradeService.BuildUpgradeArguments(new WingetUpgradeTarget(id)));

    [Theory]
    [InlineData("--all")]
    [InlineData("private")]
    [InlineData("")]
    public void RejectsUnlistedSources(string source) =>
        Assert.Throws<ArgumentException>(() =>
            WingetUpgradeService.BuildUpgradeArguments(new WingetUpgradeTarget("Contoso.App", Source: source)));

    [Theory]
    [InlineData("system")]
    [InlineData("--all")]
    [InlineData("")]
    public void RejectsUnlistedScopes(string scope) =>
        Assert.Throws<ArgumentException>(() =>
            WingetUpgradeService.BuildUpgradeArguments(new WingetUpgradeTarget("Contoso.App", Scope: scope)));

    [Fact]
    public void NeverEmitsDangerousFlags()
    {
        IReadOnlyList<string> arguments = WingetUpgradeService.BuildUpgradeArguments(
            new WingetUpgradeTarget("Contoso.App", IncludeUnknown: true));

        Assert.DoesNotContain("--all", arguments);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("--override", arguments);
        Assert.DoesNotContain("--custom", arguments);
    }

    [Fact]
    public void ListCommandIsFixed()
    {
        Assert.Equal(
            [
                "list", "--upgrade-available", "--include-unknown",
                "--accept-source-agreements", "--disable-interactivity"
            ],
            WingetUpgradeService.ListArguments);
    }
}
