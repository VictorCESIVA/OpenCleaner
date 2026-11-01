using OpenCleaner.UI;
using Xunit;

namespace OpenCleaner.UI.Tests;

public sealed class UpdateNotesFormatterTests
{
    [Fact]
    public void RemovesHeadingsBoldInlineCodeAndLinkTargets()
    {
        string result = UpdateNotesFormatter.Format(
            "# Version 2.0\n\n**Important** fix in `Core` see [details](https://example.com).");

        Assert.Equal("Version 2.0\n\nImportant fix in Core see details.", result);
    }

    [Fact]
    public void KeepsBulletItems()
    {
        string result = UpdateNotesFormatter.Format("- First\n* Second\n+ Third");

        Assert.Equal("• First\n• Second\n• Third", result);
    }

    [Fact]
    public void RemovesScriptAndHtmlTags()
    {
        string result = UpdateNotesFormatter.Format(
            "Hello <script>alert('x')</script><b>world</b><br/>Next");

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", result, StringComparison.Ordinal);
        Assert.DoesNotContain(">", result, StringComparison.Ordinal);
        Assert.Contains("Hello world", result);
        Assert.Contains("Next", result);
    }

    [Fact]
    public void NormalizesLineEndings()
    {
        Assert.Equal("one\ntwo\nthree", UpdateNotesFormatter.Format("one\r\ntwo\rthree"));
    }

    [Fact]
    public void CollapsesExcessBlankLines()
    {
        Assert.Equal("one\n\ntwo", UpdateNotesFormatter.Format("one\n\n\n\n\ntwo"));
    }

    [Fact]
    public void DecodesCommonEntities()
    {
        Assert.Equal("A & B < C", UpdateNotesFormatter.Format("A &amp; B &lt; C"));
    }

    [Fact]
    public void RemovesFencesAndImagesKeepAltText()
    {
        string result = UpdateNotesFormatter.Format("```csharp\nvar x = 1;\n```\n![Logo](https://example.com/logo.png)");

        Assert.DoesNotContain("```", result);
        Assert.Contains("var x = 1;", result);
        Assert.Contains("Logo", result);
        Assert.DoesNotContain("https://", result);
    }

    [Fact]
    public void EmptyNotesReturnEmptyText()
    {
        Assert.Equal("", UpdateNotesFormatter.Format(null));
        Assert.Equal("", UpdateNotesFormatter.Format("   "));
    }

    [Fact]
    public void LongNotesAreTruncatedAtTheRequestedLength()
    {
        string result = UpdateNotesFormatter.Format(new string('a', 5000), 50);

        Assert.True(result.Length <= 50);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncationDoesNotSplitSurrogatePairs()
    {
        string result = UpdateNotesFormatter.Format("😀😀😀😀", 3);

        Assert.True(result.Length <= 3);
        Assert.DoesNotContain('\uFFFD', result);
    }

    [Fact]
    public void HtmlOnlyNotesBecomeEmpty()
    {
        Assert.Equal("", UpdateNotesFormatter.Format("<div><script>bad()</script></div>"));
    }

    [Fact]
    public void InlineMarkersInsideWordsArePreserved()
    {
        Assert.Equal("snake_case_name and 2*3", UpdateNotesFormatter.Format("snake_case_name and 2*3"));
    }
}
