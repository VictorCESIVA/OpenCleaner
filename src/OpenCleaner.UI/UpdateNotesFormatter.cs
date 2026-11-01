using System.Net;
using System.Text.RegularExpressions;

namespace OpenCleaner.UI;

public static class UpdateNotesFormatter
{
    public const int DefaultMaxLength = 4000;

    private static readonly Regex ScriptStyle = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlComment = new(
        @"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AutoLink = new(
        @"<((?:https?|mailto):[^>\s]+)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkdownLink = new(
        @"!?\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)",
        RegexOptions.Compiled);

    private static readonly Regex BreakTag = new(
        @"<br\s*/?>|</p\s*>|</li\s*>|</div\s*>|</h[1-6]\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtmlTag = new(
        @"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Fence = new(
        @"^[ \t]*(?:```|~~~)[^\n]*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Heading = new(
        @"^[ \t]{0,3}#{1,6}[ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex BlockQuote = new(
        @"^[ \t]{0,3}>[ \t]?", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex HorizontalRule = new(
        @"^[ \t]*(?:-{3,}|\*{3,}|_{3,})[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Bullet = new(
        @"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Bold = new(
        @"(\*\*|__)(.+?)\1", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Italic = new(
        @"(?<![\p{L}\p{N}*])\*(?![\s*])([^*\n]+?)\*(?![\p{L}\p{N}*])|(?<![\p{L}\p{N}_])_(?![\s_])([^_\n]+?)_(?![\p{L}\p{N}_])",
        RegexOptions.Compiled);

    private static readonly Regex InlineCode = new(
        @"`+([^`\n]*?)`+", RegexOptions.Compiled);

    private static readonly Regex Spaces = new(
        @"[ \t]+", RegexOptions.Compiled);

    private static readonly Regex ManyBlankLines = new(
        @"\n{3,}", RegexOptions.Compiled);

    public static string ToPlainText(string? markdown, int maxLength = DefaultMaxLength) =>
        Format(markdown, maxLength);

    public static string Format(string? markdown, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        if (maxLength <= 0) return "";
        if (maxLength == 1) return "…";

        string text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        text = WebUtility.HtmlDecode(text);
        text = ScriptStyle.Replace(text, "");
        text = HtmlComment.Replace(text, "");
        text = AutoLink.Replace(text, "$1");
        text = MarkdownLink.Replace(text, "$1");
        text = BreakTag.Replace(text, "\n");
        text = HtmlTag.Replace(text, "");
        text = Fence.Replace(text, "");
        text = Heading.Replace(text, "");
        text = BlockQuote.Replace(text, "");
        text = HorizontalRule.Replace(text, "");
        text = Bullet.Replace(text, "• ");

        string previous;
        do
        {
            previous = text;
            text = Bold.Replace(text, "$2");
        } while (text != previous);

        text = InlineCode.Replace(text, "$1");
        text = Italic.Replace(text, match =>
            match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        text = ManyBlankLines.Replace(text, "\n\n");

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
            lines[index] = Spaces.Replace(lines[index], " ").TrimEnd();
        text = string.Join("\n", lines).Trim();

        if (text.Length <= maxLength) return text;

        int cut = maxLength - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        if (cut <= 0) return "…";
        int lineBreak = text.LastIndexOf('\n', cut - 1);
        if (lineBreak > maxLength / 2) cut = lineBreak;
        return text[..cut].TrimEnd() + "…";
    }
}
