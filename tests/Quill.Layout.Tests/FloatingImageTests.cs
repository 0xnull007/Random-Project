using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

/// <summary>Floating pictures: placed relative to their paragraph, drawn by the page, with text flowing around them. The column is 100 DIPs wide starting at x=20, y=20.</summary>
public class FloatingImageTests
{
    private static SectionProperties Page() => new()
    {
        PageWidth = Twips.FromDips(140),
        PageHeight = Twips.FromDips(400),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    private static ImageAnchor Anchor(ImageWrap wrap = ImageWrap.Square) => ImageAnchor.Default with { Wrap = wrap, DistanceX = Twips.Zero };

    private static InlineImage Picture(ImageAnchor anchor, double width = 60, double height = 30) => new("img", Twips.FromDips(width), Twips.FromDips(height), anchor: anchor);

    private static LayoutDocument Layout(params Block[] blocks) => Layout(null, blocks);

    private static LayoutDocument Layout(ImmutableList<Block>? header, params Block[] blocks)
    {
        var section = new Section(Page(), blocks.ToImmutableList(), header is null ? HeaderFooterSet.Empty : new HeaderFooterSet(header, null, null), HeaderFooterSet.Empty);
        var document = new Document(ImmutableList.Create(section), StyleSheet.Empty);
        return new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
    }

    [Fact]
    public void Square_wrap_narrows_the_lines_beside_the_picture()
    {
        LayoutDocument layout = Layout(new Paragraph([Picture(Anchor()), new Run("aaa bbb ccc ddd eee fff")]));
        PageLayout page = layout.Pages[0];
        ParagraphFragment fragment = page.Body.OfType<ParagraphFragment>().Single();

        FloatingImage floating = Assert.Single(page.Floats);
        Assert.Equal(new RectD(20, 20, 60, 30), floating.Bounds);
        Assert.Equal(new PointD(20, 20), floating.Reference);
        Assert.Equal(0, floating.Offset);

        // The picture sits at the left, so the two lines beside it start after it and are 40 wide: one word each.
        Assert.Equal(4, fragment.Layout.LineCount);
        Assert.Equal(60, fragment.Layout.LineStarts[0]);
        Assert.Equal(60, fragment.Layout.LineStarts[1]);
        Assert.Equal(0, fragment.Layout.LineStarts[2]);
        Assert.Equal(5, fragment.Layout.Lines[0].End); // anchor character + "aaa "
        Assert.Equal(9, fragment.Layout.Lines[1].End);
        Assert.Equal(17, fragment.Layout.Lines[2].End);
    }

    [Fact]
    public void Top_and_bottom_wrap_pushes_the_lines_below_the_picture()
    {
        LayoutDocument layout = Layout(new Paragraph([Picture(Anchor(ImageWrap.TopAndBottom)), new Run("aaa bbb ccc ddd eee fff")]));
        ParagraphFragment fragment = layout.Pages[0].Body.OfType<ParagraphFragment>().Single();

        Assert.Equal(3, fragment.Layout.LineCount);
        Assert.Equal(30, fragment.Layout.LineTops[0]);
        Assert.Equal(50, fragment.Layout.LineTops[1]);
        Assert.Equal(fragment.ContentTop + 30, fragment.LineTop(0));
        Assert.Equal(90, fragment.Layout.ContentHeight);
        Assert.Equal(90 + fragment.SpaceBeforeUsed + fragment.SpaceAfterUsed, fragment.Bounds.Height);
    }

    [Fact]
    public void Pictures_behind_or_in_front_of_text_leave_the_lines_alone()
    {
        foreach (ImageWrap wrap in new[] { ImageWrap.BehindText, ImageWrap.InFrontOfText })
        {
            LayoutDocument layout = Layout(new Paragraph([Picture(Anchor(wrap)), new Run("aaa bbb ccc")]));
            ParagraphFragment fragment = layout.Pages[0].Body.OfType<ParagraphFragment>().Single();
            Assert.Single(layout.Pages[0].Floats);
            Assert.Equal(2, fragment.Layout.LineCount);
            Assert.All(fragment.Layout.LineStarts, start => Assert.Equal(0, start));
            Assert.Equal(0, fragment.Layout.LineTops[0]);
        }
    }

    [Fact]
    public void A_picture_reaching_into_the_next_paragraph_narrows_its_lines_too()
    {
        LayoutDocument layout = Layout(
            Paragraph.FromText("x"),
            new Paragraph([Picture(Anchor(), height: 50), new Run("aaa bbb")]),
            Paragraph.FromText("ccc ddd eee fff"));
        List<ParagraphFragment> fragments = layout.Pages[0].Body.OfType<ParagraphFragment>().ToList();

        FloatingImage floating = Assert.Single(layout.Pages[0].Floats);
        Assert.Equal(fragments[1].ContentTop, floating.Bounds.Top);
        Assert.Equal(1, floating.Path.TopIndex);

        Assert.Equal(2, fragments[1].Layout.LineCount);
        Assert.Equal(new[] { 60.0, 60.0 }, fragments[1].Layout.LineStarts);

        // The picture ends 10 DIPs into the third paragraph: only its first line is narrowed.
        Assert.Equal(3, fragments[2].Layout.LineCount);
        Assert.Equal(new[] { 60.0, 0.0, 0.0 }, fragments[2].Layout.LineStarts);
        Assert.Equal(4, fragments[2].Layout.Lines[0].End);
    }

    [Fact]
    public void Alignment_and_offsets_place_the_picture_within_the_column_margins_or_page()
    {
        var right = Anchor() with { HorizontalAlignment = ImageHorizontalAlignment.Right, HorizontalRelativeTo = HorizontalAnchor.Margin, OffsetY = Twips.FromDips(10) };
        var pageCorner = Anchor(ImageWrap.InFrontOfText) with { HorizontalRelativeTo = HorizontalAnchor.Page, VerticalRelativeTo = VerticalAnchor.Page, OffsetX = Twips.FromDips(5), OffsetY = Twips.FromDips(7) };
        LayoutDocument layout = Layout(new Paragraph([Picture(right), new Run("aaa"), Picture(pageCorner)]));

        ImmutableArray<FloatingImage> floats = layout.Pages[0].Floats;
        Assert.Equal(2, floats.Length);
        Assert.Equal(new RectD(60, 30, 60, 30), floats[0].Bounds);
        Assert.Equal(new PointD(20, 20), floats[0].Reference);
        Assert.Equal(new RectD(5, 7, 60, 30), floats[1].Bounds);
        Assert.Equal(new PointD(0, 0), floats[1].Reference);
        Assert.Equal(4, floats[1].Offset);
        Assert.Same(floats[1], layout.Pages[0].FloatAt(new PointD(10, 10)));
        Assert.Null(layout.Pages[0].FloatAt(new PointD(10, 100)));
    }

    [Fact]
    public void Inside_a_table_cell_a_floating_picture_is_laid_out_in_the_line()
    {
        var cell = new TableCell(ImmutableList.Create<Block>(new Paragraph([Picture(Anchor()), new Run("ab")])));
        var table = new Table([new TableRow([cell])], [Twips.FromDips(100)], hasBorders: true, Twips.Zero);
        LayoutDocument layout = Layout(table);

        Assert.Empty(layout.Pages[0].Floats);
        TableFragment fragment = layout.Pages[0].Body.OfType<TableFragment>().Single();
        ParagraphLayout paragraph = fragment.CellParagraphs.Single().Layout;
        Assert.Empty(paragraph.Input.Obstacles);
        Assert.False(paragraph.Input.Runs[0].Image!.IsFloating);
        Assert.Equal(60, paragraph.Lines[0].GetCaretX(1)); // the picture takes its 60 DIPs in the line
    }

    [Fact]
    public void Header_pictures_float_too_and_belong_to_the_header_story()
    {
        ImmutableList<Block> header = ImmutableList.Create<Block>(new Paragraph([Picture(Anchor(ImageWrap.BehindText), width: 20, height: 10), new Run("hdr")]));
        LayoutDocument layout = Layout(header, Paragraph.FromText("body"));

        FloatingImage floating = Assert.Single(layout.Pages[0].Floats);
        Assert.False(floating.Story.IsBody);
        Assert.Equal(layout.Pages[0].Header!.Bounds.Left, floating.Bounds.Left);
        Assert.Equal(layout.Pages[0].Header!.Bounds.Top, floating.Bounds.Top);
    }

    [Fact]
    public void A_paragraph_split_across_pages_keeps_the_line_breaks_it_got_beside_the_picture()
    {
        var tall = new SectionProperties
        {
            PageWidth = Twips.FromDips(140),
            PageHeight = Twips.FromDips(100),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        // Column is 60 tall (3 lines). Lines 0 and 1 are narrowed; lines 2+ are full width; the paragraph spills onto page 2.
        var paragraph = new Paragraph([Picture(Anchor()), new Run("aaa bbb ccc ddd eee fff ggg hhh")], properties: new ParagraphProperties { WidowControl = false });
        var document = new Document(ImmutableList.Create(new Section(tall, ImmutableList.Create<Block>(paragraph))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));

        Assert.Equal(2, layout.Pages.Length);
        ParagraphFragment first = layout.Pages[0].Body.OfType<ParagraphFragment>().Single();
        ParagraphFragment second = layout.Pages[1].Body.OfType<ParagraphFragment>().Single();
        Assert.Same(first.Layout, second.Layout);
        Assert.Equal(first.LastLine + 1, second.FirstLine);
        Assert.Single(layout.Pages[0].Floats);
        Assert.Empty(layout.Pages[1].Floats);
    }
}
