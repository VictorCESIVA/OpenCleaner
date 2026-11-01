using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class WingetPackageIdTests
{
    [Theory]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("7zip.7zip")]
    [InlineData("A")]
    [InlineData("Contoso.App_1.0+beta~test-2")]
    [InlineData("OBSProject.OBSStudio")]
    public void AcceptsValidIds(string id) => Assert.True(WingetPackageId.IsValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("tab\tid")]
    [InlineData("line\nbreak")]
    [InlineData("trailing-newline\n")]
    [InlineData("trailing-null\0")]
    [InlineData("-leading")]
    [InlineData("--all")]
    [InlineData("id;calc")]
    [InlineData("id&calc")]
    [InlineData("id|calc")]
    [InlineData("id>file")]
    [InlineData("id<file")]
    [InlineData("id$(calc)")]
    [InlineData("id`calc`")]
    [InlineData("id\"quoted")]
    [InlineData("id'single")]
    [InlineData("id/path")]
    [InlineData("id\\path")]
    [InlineData("id:stream")]
    [InlineData(".leadingdot")]
    public void RejectsInvalidIds(string? id) => Assert.False(WingetPackageId.IsValid(id));

    [Fact]
    public void RejectsIdsLongerThanTwoHundredCharacters()
    {
        Assert.True(WingetPackageId.IsValid(new string('a', 200)));
        Assert.False(WingetPackageId.IsValid(new string('a', 201)));
    }

    [Fact]
    public void PreservesCase() =>
        Assert.True(WingetPackageId.IsValid("Microsoft.VisualStudioCode"));

    [Fact]
    public void DeduplicatesCaseInsensitivelyAndPreservesFirstSpelling()
    {
        IReadOnlyList<string> distinct = WingetPackageId.Distinct(
            ["Microsoft.VisualStudioCode", "microsoft.visualstudiocode", "7zip.7zip", "7ZIP.7ZIP"]);

        Assert.Equal(["Microsoft.VisualStudioCode", "7zip.7zip"], distinct);
    }
}
