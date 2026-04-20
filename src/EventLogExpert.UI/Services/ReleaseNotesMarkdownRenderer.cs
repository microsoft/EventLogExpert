// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Services;

/// <summary>
/// Renders a small, well-defined subset of Markdown to safe HTML for the in-app
/// release notes modal. Inputs are HTML-escaped first; only the renderer itself
/// emits structural tags. Raw HTML in the input is treated as text.
/// </summary>
public static partial class ReleaseNotesMarkdownRenderer
{
    private const char CodePlaceholderSentinel = '\u0001';

    public static string RenderToHtml(string? title, string? markdown)
    {
        StringBuilder builder = new();

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("<h1>").Append(EscapeHtml(title)).Append("</h1>");
        }

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            builder.Append(RenderToHtml(markdown));
        }

        return builder.ToString();
    }

    public static string RenderToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        StringBuilder output = new();
        List<string> paragraphBuffer = [];
        bool inList = false;

        void FlushParagraph()
        {
            if (paragraphBuffer.Count == 0) { return; }

            output.Append("<p>");
            output.Append(string.Join("<br />", paragraphBuffer.Select(ProcessInline)));
            output.Append("</p>");
            paragraphBuffer.Clear();
        }

        void FlushList()
        {
            if (!inList) { return; }

            output.Append("</ul>");
            inList = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            var headingMatch = HeadingRegex().Match(line);

            if (headingMatch.Success)
            {
                FlushParagraph();
                FlushList();
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                output.Append("<h").Append(level).Append('>')
                    .Append(ProcessInline(text))
                    .Append("</h").Append(level).Append('>');
                continue;
            }

            var bulletMatch = BulletRegex().Match(line);

            if (bulletMatch.Success)
            {
                FlushParagraph();

                if (!inList)
                {
                    output.Append("<ul>");
                    inList = true;
                }

                output.Append("<li>")
                    .Append(ProcessInline(bulletMatch.Groups[1].Value))
                    .Append("</li>");
                continue;
            }

            FlushList();
            paragraphBuffer.Add(line);
        }

        FlushParagraph();
        FlushList();

        return output.ToString();
    }

    [GeneratedRegex(@"\*\*([^*\n]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex("\u0001(\\d+)\u0001")]
    private static partial Regex CodePlaceholderRegex();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex CodeSpanRegex();

    private static string EscapeHtml(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\*([^*\n]+)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    private static string ProcessInline(string line)
    {
        // Strip any user-supplied sentinel characters so they cannot collide with
        // the code-span placeholder protocol below.
        var sanitized = line.Replace(CodePlaceholderSentinel.ToString(), string.Empty);
        var escaped = EscapeHtml(sanitized);

        List<string> codeSpans = [];

        var withCodePlaceholders = CodeSpanRegex().Replace(escaped, match =>
        {
            codeSpans.Add($"<code>{match.Groups[1].Value}</code>");
            return $"{CodePlaceholderSentinel}{codeSpans.Count - 1}{CodePlaceholderSentinel}";
        });

        // Render Markdown links as their visible text only. The release notes
        // modal is intentionally read-only; we do not want users navigating to
        // external URLs from inside the app.
        var withLinkTextOnly = LinkRegex().Replace(withCodePlaceholders, match => match.Groups[1].Value);

        var withBold = BoldRegex().Replace(withLinkTextOnly, "<strong>$1</strong>");
        var withItalic = ItalicRegex().Replace(withBold, "<em>$1</em>");

        var restored = CodePlaceholderRegex().Replace(withItalic, match =>
        {
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return index >= 0 && index < codeSpans.Count ? codeSpans[index] : match.Value;
        });

        return restored;
    }
}
