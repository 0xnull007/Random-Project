using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
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
        Assert.Equal(["Title", "Hello bold and it, a link￼second line", "•\tone", "•\ttwo & red", "1.\tfirst"], paragraphs.Select(p => p.FlatText));
        var table = Assert.IsType<Table>(fragment.Blocks[^1]);
        Assert.Equal(["a", "b"], table.Rows[0].Cells.Select(c => ((Paragraph)c.Blocks[0]).FlatText));
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
    public void Pictures_export_as_data_uris_and_import_back_with_their_bytes_and_size()
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var image = new InlineImage("pic", Twips.FromDips(120), Twips.FromDips(60), altText: "A dot");
        var fragment = new DocumentFragment(
            System.Collections.Immutable.ImmutableArray.Create<Block>(new Paragraph([new Run("Before "), image, new Run(" after")])),
            System.Collections.Immutable.ImmutableDictionary<string, ImageData>.Empty.Add("pic", new ImageData(png, "image/png", 1, 1)));
        var resolver = new StyleResolver(DefaultStyleSheet.Create());

        string html = HtmlConverter.ToHtml(fragment, resolver, ListStore.Empty);
        Assert.Contains("<img src=\"data:image/png;base64,iVBORw0KGgo", html, StringComparison.Ordinal);
        Assert.Contains("width=\"120\" height=\"60\" alt=\"A dot\"", html, StringComparison.Ordinal);

        DocumentFragment back = HtmlConverter.FromHtml(html);
        Paragraph paragraph = back.Paragraphs.Single();
        Assert.Equal("Before ￼ after", paragraph.FlatText);
        var imported = Assert.IsType<InlineImage>(paragraph.Inlines[1]);
        Assert.Equal(Twips.FromDips(120), imported.Width);
        Assert.Equal(Twips.FromDips(60), imported.Height);
        Assert.Equal("A dot", imported.AltText);
        Assert.Equal(png, back.Images[imported.ImageId].Bytes);
        Assert.Equal("image/png", back.Images[imported.ImageId].ContentType);

        // Pictures that live on the web are not fetched; they are dropped rather than left as broken boxes.
        Assert.Empty(HtmlConverter.FromHtml("<p><img src=\"https://example.com/a.png\"></p>").Paragraphs.Single().Inlines);
    }

    [Fact]
    public void Whole_page_export_is_a_complete_html_document_with_the_title_and_tables()
    {
        var table = new Table([new TableRow([new TableCell(System.Collections.Immutable.ImmutableList.Create<Block>(Paragraph.FromText("cell")))])], [Twips.FromInches(2)], hasBorders: true, Twips.Zero);
        var fragment = new DocumentFragment(System.Collections.Immutable.ImmutableArray.Create<Block>(Paragraph.FromText("Hello & welcome"), table));
        string page = HtmlConverter.ToDocumentHtml(fragment, new StyleResolver(DefaultStyleSheet.Create()), ListStore.Empty, "My <Report>");

        Assert.StartsWith("<!DOCTYPE html>", page, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", page, StringComparison.Ordinal);
        Assert.Contains("<title>My &lt;Report&gt;</title>", page, StringComparison.Ordinal);
        Assert.Contains("Hello &amp; welcome", page, StringComparison.Ordinal);
        Assert.Contains("<table", page, StringComparison.Ordinal);
        Assert.Contains("cell", page, StringComparison.Ordinal);
        Assert.EndsWith("</html>\r\n", page, StringComparison.Ordinal);
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

    [Fact]
    public void Imports_tables_with_spans_shading_and_cell_paragraphs_and_exports_them_again()
    {
        const string html = "<p>Before</p><table border=\"0\"><tr><th colspan=\"2\" bgcolor=\"#FFFF00\">Head</th></tr>"
            + "<tr><td style=\"border:1px solid black\"><p>a1</p><p>a2</p></td><td>b</td></tr></table><p>After</p>";
        DocumentFragment fragment = HtmlConverter.FromHtml(html);
        Assert.Equal(3, fragment.Blocks.Length);
        Assert.Equal("Before", ((Paragraph)fragment.Blocks[0]).FlatText);
        var table = Assert.IsType<Table>(fragment.Blocks[1]);
        Assert.Equal(2, table.Rows.Length);
        Assert.Equal(2, table.ColumnCount);
        Assert.True(table.HasBorders);
        TableCell head = table.Rows[0].Cells[0];
        Assert.Equal(2, head.GridSpan);
        Assert.Equal(DocColor.Parse("FFFF00"), head.Shading);
        Assert.True(((Paragraph)head.Blocks[0]).Inlines[0].Properties.Bold);
        Assert.Equal(["a1", "a2"], table.Rows[1].Cells[0].Blocks.Select(b => ((Paragraph)b).FlatText));
        Assert.Equal(Twips.Zero, ((Paragraph)table.Rows[1].Cells[1].Blocks[0]).Properties.SpaceAfter);
        Assert.Equal("Before\nHead\na1 a2\tb\nAfter", fragment.ToPlainText());

        var sheet = DefaultStyleSheet.Create();
        string exported = HtmlConverter.ToHtml(fragment, new StyleResolver(sheet), ListStore.Empty);
        Assert.Contains("<table border=\"1\"", exported, StringComparison.Ordinal);
        Assert.Contains("colspan=\"2\"", exported, StringComparison.Ordinal);
        Assert.Contains("background-color:#FFFF00", exported, StringComparison.Ordinal);
        Assert.Contains("<td style=\"width:50%", exported, StringComparison.Ordinal);
        DocumentFragment again = HtmlConverter.FromHtml(exported);
        Assert.IsType<Table>(again.Blocks[1]);
        Assert.Equal("a1 a2", again.ToPlainText().Split('\n')[2].Split('\t')[0]);
    }
}
