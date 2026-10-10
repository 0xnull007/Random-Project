using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;

namespace Quill.Core.Tests.Editing;

public class HtmlConverterTests
{
    [Fact]
    public void Imports_paragraphs_inline_formatting_links_and_lists()
    {
        const string html = "Version:0.9\r\nStartHTML:0000000105\r\n<html><body><!--StartFragment--><h1>Title</h1>\n"
            + "<p style=\"text-align:center\">Hello <b>bold</b> and <i>it</i>, <a href=\"https://example.com\">a link</a><br>second line</p>"
            + "<ul><li>one</li><li>two &amp; <span style=\"color:#ff0000;font-weight:700\">red</span></li></ul>"
            + "<ol><li>first</li></ol><table><tr><td>a</td><td>b</td></tr></table><!--EndFragment--></body></html>";
        DocumentFragment fragment = HtmlConverter.FromHtml(html);
        Paragraph[] paragraphs = fragment.Paragraphs.ToArray();
        Assert.Equal(["Title", "Hello bold and it, a link￼second line", "•\tone", "•\ttwo & red", "1.\tfirst", "a\tb"], paragraphs.Select(p => p.FlatText));
        Assert.Equal(DefaultStyleSheet.Heading1Id, paragraphs[0].StyleId);
        Assert.Equal(Alignment.Center, paragraphs[1].Properties.Alignment);
        Run bold = paragraphs[1].Inlines.OfType<Run>().First(r => r.Text == "bold");
        Assert.True(bold.Properties.Bold);
        Run link = paragraphs[1].Inlines.OfType<Run>().First(r => r.Text == "a link");
        Assert.Equal("https://example.com", link.Properties.Link);
        Assert.Contains(paragraphs[1].Inlines, i => i is Break { Kind: BreakKind.Line });
        Run red = paragraphs[3].Inlines.OfType<Run>().First(r => r.Text == "red");
        Assert.Equal(DocColor.Parse("FF0000"), red.Properties.Color);
        Assert.True(red.Properties.Bold);
        Assert.Equal(Twips.FromInches(0.5), paragraphs[2].Properties.LeftIndent);
    }

    [Fact]
    public void Collapses_whitespace_and_handles_entities_and_unknown_tags()
    {
        DocumentFragment fragment = HtmlConverter.FromHtml("<div>  Some\n   text &nbsp;here&#169; <custom>inside</custom> <script>alert(1)</script></div><p></p>");
        Assert.Equal("Some text  here© inside", fragment.Paragraphs[0].FlatText);
    }

    [Fact]
    public void Exports_html_with_formatting_and_the_clipboard_header()
    {
        var paragraph = new Paragraph([new Run("Hi "), new Run("there", new RunProperties { Bold = true, Link = "https://x.y" })], null, new ParagraphProperties { Alignment = Alignment.Right });
        var fragment = new DocumentFragment([paragraph]);
        StyleSheet styles = DefaultStyleSheet.Create();
        string html = HtmlConverter.ToClipboardHtml(fragment, new StyleResolver(styles), ListStore.Empty);
        Assert.StartsWith("Version:0.9", html, StringComparison.Ordinal);
        Assert.Contains("<p style=\"text-align:right;\">", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://x.y\"", html, StringComparison.Ordinal);
        Assert.Contains("font-weight:bold", html, StringComparison.Ordinal);

        DocumentFragment back = HtmlConverter.FromHtml(html);
        Assert.Equal("Hi there", back.Paragraphs[0].FlatText);
        Assert.Equal(Alignment.Right, back.Paragraphs[0].Properties.Alignment);
    }
}
