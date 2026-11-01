using System.Text;
using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class WingetServiceTests
{
    [Fact]
    public void SplitsLegacyArgumentStringWithoutShellInterpretation()
    {
        IReadOnlyList<string> arguments = WingetService.SplitArguments(
            "upgrade --id \"Contoso.App\" --exact");

        Assert.Equal(["upgrade", "--id", "Contoso.App", "--exact"], arguments);
    }

    [Fact]
    public void RejectsUnbalancedQuotes()
    {
        Assert.Throws<ArgumentException>(() => WingetService.SplitArguments("upgrade --id \"Contoso.App"));
    }

    [Fact]
    public void BoundedOutputCapsLineCount()
    {
        var output = new WingetService.BoundedOutput();
        for (int i = 0; i < WingetService.MaxOutputLines + 25; i++) output.Append("line " + i);

        Assert.True(output.Truncated);
        Assert.Equal(WingetService.MaxOutputLines, output.LineCount);
    }

    [Fact]
    public void BoundedOutputCapsLineLength()
    {
        var output = new WingetService.BoundedOutput();
        output.Append(new string('x', WingetService.MaxLineLength + 100));

        Assert.True(output.Truncated);
        Assert.Equal(WingetService.MaxLineLength, output.Text.TrimEnd().Length);
    }

    [Fact]
    public void BoundedOutputCapsTotalBytes()
    {
        var output = new WingetService.BoundedOutput();
        string line = new('x', 2000);
        for (int i = 0; i < 1000; i++) output.Append(line);

        Assert.True(output.Truncated);
        Assert.True(Encoding.UTF8.GetByteCount(output.Text) <= WingetService.MaxOutputBytes);
    }
}
