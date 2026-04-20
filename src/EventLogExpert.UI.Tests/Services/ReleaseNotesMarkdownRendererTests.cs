// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class ReleaseNotesMarkdownRendererTests
{
    [Fact]
    public void RenderToHtml_BlankLineBetweenBullets_StartsNewList()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("- a\n\n- b");

        Assert.Equal("<ul><li>a</li></ul><ul><li>b</li></ul>", html);
    }

    [Fact]
    public void RenderToHtml_BlankLineSeparatesParagraphs()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("para one\n\npara two");

        Assert.Equal("<p>para one</p><p>para two</p>", html);
    }

    [Fact]
    public void RenderToHtml_Bold_RendersStrong()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("**bold text**");

        Assert.Contains("<strong>bold text</strong>", html);
    }

    [Fact]
    public void RenderToHtml_BoldDoesNotInterfereWithItalic()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("**bold** and *italic*");

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
    }

    [Theory]
    [InlineData("- item one")]
    [InlineData("* item one")]
    [InlineData("+ item one")]
    public void RenderToHtml_BulletPrefixes_RenderAsList(string markdown)
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml(markdown);

        Assert.Equal("<ul><li>item one</li></ul>", html);
    }

    [Fact]
    public void RenderToHtml_CodePlaceholderSentinelInInput_DoesNotConfuseParser()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("text with \u00010\u0001 sentinel and `actual code`");

        Assert.Contains("<code>actual code</code>", html);
        Assert.False(html.Contains('\u0001'), "rendered HTML must not contain the internal placeholder sentinel");
    }

    [Fact]
    public void RenderToHtml_CodeSpanContents_NotProcessedAsMarkdown()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("`**not bold**`");

        Assert.Contains("<code>**not bold**</code>", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void RenderToHtml_CrlfLineEndings_HandledLikeLf()
    {
        var lf = ReleaseNotesMarkdownRenderer.RenderToHtml("# Heading\n\n- item");
        var crlf = ReleaseNotesMarkdownRenderer.RenderToHtml("# Heading\r\n\r\n- item");

        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void RenderToHtml_HashMidLine_NotTreatedAsHeading()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("Some text ## not a heading");

        Assert.DoesNotContain("<h2>", html);
    }

    [Fact]
    public void RenderToHtml_HashWithoutSpace_NotTreatedAsHeading()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("#NotAHeading");

        Assert.DoesNotContain("<h1>", html);
        Assert.Contains("#NotAHeading", html);
    }

    [Theory]
    [InlineData("# Heading", "<h1>Heading</h1>")]
    [InlineData("## Heading", "<h2>Heading</h2>")]
    [InlineData("### Heading", "<h3>Heading</h3>")]
    [InlineData("#### Heading", "<h4>Heading</h4>")]
    public void RenderToHtml_Headings(string markdown, string expected)
    {
        Assert.Equal(expected, ReleaseNotesMarkdownRenderer.RenderToHtml(markdown));
    }

    [Fact]
    public void RenderToHtml_HttpLink_RendersAnchor()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("[link](http://example.com)");

        Assert.Contains("<a href=\"http://example.com\"", html);
    }

    [Fact]
    public void RenderToHtml_HttpsLink_RendersAnchor()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("see [docs](https://example.com/page)");

        Assert.Contains("<a href=\"https://example.com/page\" target=\"_blank\" rel=\"noopener noreferrer\">docs</a>", html);
    }

    [Fact]
    public void RenderToHtml_InlineCode_RendersCodeTag()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("use `Foo()` to call");

        Assert.Contains("<code>Foo()</code>", html);
    }

    [Fact]
    public void RenderToHtml_Italic_RendersEm()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("paragraph with *italic* word");

        Assert.Contains("<em>italic</em>", html);
    }

    [Fact]
    public void RenderToHtml_LegacyBulletList_RendersCleanly()
    {
        const string markdown = "## Changes:\n\n- Fixed LF issue in App.xaml\n- Updated Azure yml to .NET 8";

        var html = ReleaseNotesMarkdownRenderer.RenderToHtml(markdown);

        Assert.Contains("<h2>Changes:</h2>", html);
        Assert.Contains("<ul><li>Fixed LF issue in App.xaml</li><li>Updated Azure yml to .NET 8</li></ul>", html);
    }

    [Fact]
    public void RenderToHtml_MultipleBullets_GroupedInOneList()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("- a\n- b\n- c");

        Assert.Equal("<ul><li>a</li><li>b</li><li>c</li></ul>", html);
    }

    [Fact]
    public void RenderToHtml_MultipleCodeSpansOnOneLine_AllRendered()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("call `Foo()` then `Bar()` then `Baz()`");

        Assert.Contains("<code>Foo()</code>", html);
        Assert.Contains("<code>Bar()</code>", html);
        Assert.Contains("<code>Baz()</code>", html);
    }

    [Fact]
    public void RenderToHtml_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ReleaseNotesMarkdownRenderer.RenderToHtml(null!));
        Assert.Equal(string.Empty, ReleaseNotesMarkdownRenderer.RenderToHtml(string.Empty));
        Assert.Equal(string.Empty, ReleaseNotesMarkdownRenderer.RenderToHtml("   \n\n  "));
    }

    [Fact]
    public void RenderToHtml_PlainTextLines_RenderAsParagraph()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("first line\nsecond line");

        Assert.Equal("<p>first line<br />second line</p>", html);
    }

    [Theory]
    [InlineData("[xss](https://example.com\"onload=alert(1))")]
    [InlineData("[xss](https://example.com'onclick='alert(1))")]
    public void RenderToHtml_QuotesInUrl_AreEscapedNotInjected(string markdown)
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml(markdown);

        Assert.DoesNotContain("\" onload=", html);
        Assert.DoesNotContain("\"onload=", html);
        Assert.DoesNotContain("\" onclick=", html);
        Assert.DoesNotContain("'onclick=", html);
    }

    [Fact]
    public void RenderToHtml_RawHtmlInInput_IsEscapedNotRendered()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("<img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void RenderToHtml_RichLayoutSample_RendersAllElements()
    {
        const string markdown = """
            ## What's New in v1.2.3

            ### Features
            - **Column reordering** with persistent sizing
            - Support for [exported logs](https://example.com/docs)

            ### Bug Fixes
            - Fixed crash when opening empty file
            """;

        var html = ReleaseNotesMarkdownRenderer.RenderToHtml(markdown);

        Assert.Contains("<h2>What&#39;s New in v1.2.3</h2>", html);
        Assert.Contains("<h3>Features</h3>", html);
        Assert.Contains("<h3>Bug Fixes</h3>", html);
        Assert.Contains("<strong>Column reordering</strong>", html);
        Assert.Contains("<a href=\"https://example.com/docs\"", html);
        Assert.Contains("<li>Fixed crash when opening empty file</li>", html);
    }

    [Fact]
    public void RenderToHtml_ScriptTagInInput_IsEscaped()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("<script>alert(1)</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void RenderToHtml_TitleIsHtmlEscaped()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("<script>alert(1)</script>", string.Empty);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void RenderToHtml_TitleOnly_RendersH1()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("Release notes for v1.0", string.Empty);

        Assert.Equal("<h1>Release notes for v1.0</h1>", html);
    }

    [Fact]
    public void RenderToHtml_UnmatchedBoldMarkers_LeftAsLiteral()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("**unclosed bold");

        Assert.DoesNotContain("<strong>", html);
        Assert.Contains("**unclosed bold", html);
    }

    [Theory]
    [InlineData("[evil](javascript:alert(1))")]
    [InlineData("[evil](data:text/html,<script>)")]
    [InlineData("[evil](file:///etc/passwd)")]
    [InlineData("[evil](ftp://example.com)")]
    public void RenderToHtml_UnsafeLinkSchemes_NotRenderedAsAnchor(string markdown)
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml(markdown);

        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href=", html);
    }

    [Fact]
    public void RenderToHtml_UrlWithAmpersand_PreservesEscapedEntity()
    {
        var html = ReleaseNotesMarkdownRenderer.RenderToHtml("[link](https://example.com/path?a=1&b=2)");

        Assert.Contains("href=\"https://example.com/path?a=1&amp;b=2\"", html);
    }
}
