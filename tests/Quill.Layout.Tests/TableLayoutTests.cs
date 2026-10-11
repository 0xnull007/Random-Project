using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

public class TableLayoutTests
{
    private static SectionProperties Page(double bodyHeight) => new()
    {
        PageWidth = Twips.FromDips(240),
        PageHeight = Twips.FromDips(bodyHeight + 40),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    private static Table TwoColumnTable(params string[][] rows) => new(
        rows.Select(cells => new TableRow(cells.Select(text => new TableCell(ImmutableList.Create<Block>(Paragraph.FromText(text)))).ToImmutableArray())).ToImmutableArray(),
        [Twips.FromDips(60), Twips.FromDips(140)],
        hasBorders: true,
        Twips.Zero);

    private static LayoutDocument Layout(SectionProperties page, params Block[] blocks)
    {
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.CreateRange(blocks))), StyleSheet.Empty);
        return new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
    }

    [Fact]
    public void Cells_wrap_at_their_width_and_rows_take_the_tallest_cell()
    {
        // Column 1 is 60 wide: 60 - 2 * 7.2 = 45.6 -> four 10-DIP characters per line.
        LayoutDocument layout = Layout(Page(400), TwoColumnTable(["abcdefgh", "x"], ["y", "z"]), Paragraph.FromText("after"));
        PageLayout page = layout.Pages[0];
        var table = Assert.IsType<TableFragment>(page.Body[0]);
        Assert.Equal(2, table.Layout.Rows.Length);
        Assert.Equal(40, table.Layout.Rows[0].Height, 3);
        Assert.Equal(20, table.Layout.Rows[1].Height, 3);
        Assert.Equal(60, table.Layout.ColumnWidths[0], 3);
        Assert.Equal(60, table.Layout.Rows[0].Cells[1].Left, 3);
        Assert.Equal(200, table.Bounds.Width, 3);
        Assert.Equal(20, table.Bounds.Top, 3);
        Assert.Equal(60, table.Bounds.Height, 3);
        Assert.Equal(80, page.Body[1].Bounds.Top, 3);
        Assert.Equal(2, table.Layout.Rows[0].Cells[0].Blocks[0].Paragraph!.LineCount);
    }

    [Fact]
    public void Rows_that_do_not_fit_move_to_the_next_page()
    {
        LayoutDocument layout = Layout(Page(50), TwoColumnTable(["a", "b"], ["c", "d"], ["e", "f"]));
        Assert.Equal(2, layout.Pages.Length);
        var first = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);
        var second = Assert.IsType<TableFragment>(layout.Pages[1].Body[0]);
        Assert.Equal((0, 1), (first.FirstRow, first.LastRow));
        Assert.Equal((2, 2), (second.FirstRow, second.LastRow));
        Assert.Equal(20, second.RowTop(2), 3);
    }

    [Fact]
    public void Wide_grids_are_scaled_to_the_column_and_exact_heights_are_kept()
    {
        var table = new Table(
            [new TableRow([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("a")))], Twips.FromDips(70), exactHeight: true)],
            [Twips.FromDips(400)],
            hasBorders: false,
            Twips.FromDips(10));
        LayoutDocument layout = Layout(Page(400), table);
        var fragment = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);
        Assert.Equal(190, fragment.Layout.Width, 3);
        Assert.Equal(30, fragment.Bounds.Left, 3);
        Assert.Equal(70, fragment.Layout.Rows[0].Height, 3);
    }
}

public class TableFragmentTests
{
    private static Table TwoColumnTable(params string[][] rows) => new(
        rows.Select(cells => new TableRow(cells.Select(text => new TableCell(ImmutableList.Create<Block>(Paragraph.FromText(text)))).ToImmutableArray())).ToImmutableArray(),
        [Twips.FromDips(60), Twips.FromDips(140)],
        hasBorders: true,
        Twips.Zero);

    [Fact]
    public void Cell_paragraphs_get_fragments_with_nested_paths_and_hit_testing_finds_cells()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(440),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(Paragraph.FromText("before"), TwoColumnTable(["abcdefgh", "x"], ["y", "z"])))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        PageLayout first = layout.Pages[0];
        var table = Assert.IsType<TableFragment>(first.Body[1]);

        Assert.Equal(4, table.CellParagraphs.Length);
        ParagraphFragment x = table.CellParagraphs[1];
        Assert.Equal(new BlockPath([1, 0, 1, 0]), x.Path);
        Assert.Equal(table.Bounds.Left + 60 + TableLayout.CellPadding, x.Bounds.Left, 3);
        Assert.InRange(x.Bounds.Top, table.Bounds.Top, table.RowTop(1));
        Assert.Equal(table.RowTop(1), table.CellParagraphs[2].Bounds.Top, 3);
        Assert.Contains(x, first.AllFragments());
        Assert.Equal(1 + 1 + 4, first.AllFragments().Count());

        (TableFragment Table, int Row, int Cell)? hit = table.CellAt(new PointD(table.Bounds.Left + 100, table.Bounds.Top + 5));
        Assert.NotNull(hit);
        Assert.Same(table, hit.Value.Table);
        Assert.Equal((0, 1), (hit.Value.Row, hit.Value.Cell));
        hit = table.CellAt(new PointD(table.Bounds.Left + 10, table.RowTop(1) + 5));
        Assert.Equal((1, 0), (hit!.Value.Row, hit.Value.Cell));
        Assert.Null(table.CellAt(new PointD(table.Bounds.Left - 5, table.Bounds.Top)));
    }
}

public class TableHeaderRowTests
{
    [Fact]
    public void Header_rows_repeat_at_the_top_of_continuation_pages_without_duplicating_cell_fragments()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(90),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        TableRow Row(string text, bool header = false) => new([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText(text)))], isHeader: header);
        var table = new Table([Row("head", header: true), Row("a"), Row("b"), Row("c")], [Twips.FromDips(200)], hasBorders: true, Twips.Zero);
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(table))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));

        // 50 DIP of body per page, 20 per row: page 1 holds head + a; page 2 repeats head (20) and holds b; page 3 head + c.
        Assert.Equal(3, layout.Pages.Length);
        var first = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);
        var second = Assert.IsType<TableFragment>(layout.Pages[1].Body[0]);
        Assert.Equal(0, first.RepeatedHeaderRows);
        Assert.Equal((0, 1), (first.FirstRow, first.LastRow));
        Assert.Equal(1, second.RepeatedHeaderRows);
        Assert.Equal((2, 2), (second.FirstRow, second.LastRow));
        Assert.Equal(20, second.HeaderHeight, 3);
        Assert.Equal(second.Bounds.Top, second.RowTop(0), 3);
        Assert.Equal(second.Bounds.Top + 20, second.RowTop(2), 3);
        Assert.Equal(40, second.Bounds.Height, 3);
        Assert.Single(second.CellParagraphs);
        Assert.Equal(new BlockPath([0, 2, 0, 0]), second.CellParagraphs[0].Path);
        Assert.Equal(second.RowTop(2), second.CellParagraphs[0].Bounds.Top, 3);
        Assert.Equal((second, 0, 0), second.CellAt(new PointD(second.Bounds.Left + 5, second.Bounds.Top + 5)));
        Assert.Equal((second, 2, 0), second.CellAt(new PointD(second.Bounds.Left + 5, second.Bounds.Top + 25)));
    }
}

public class HeaderTableTests
{
    [Fact]
    public void Tables_in_headers_are_laid_out_and_push_the_body_down()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(40),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
            HeaderDistance = Twips.FromDips(10),
        };
        var headerTable = new Table(
            [new TableRow([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("Logo"))), new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("Address")))])],
            [Twips.FromDips(100), Twips.FromDips(100)],
            hasBorders: false,
            Twips.Zero);
        var headers = new HeaderFooterSet(ImmutableList.Create<Block>(headerTable), null, null);
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(Paragraph.FromText("body")), headers)), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        PageLayout first = layout.Pages[0];

        TableFragment table = Assert.Single(first.AllFragments().OfType<TableFragment>());
        Assert.False(table.Story.IsBody);
        Assert.Equal(10, table.Bounds.Top, 3);
        Assert.Equal(2, table.CellParagraphs.Length);
        Assert.Equal(new BlockPath([0, 0, 1, 0]), table.CellParagraphs[1].Path);
        Assert.Contains(first.AllFragments(), f => f is ParagraphFragment { Path.IsTopLevel: false, Story.IsBody: false });
        ParagraphFragment body = first.Body.OfType<ParagraphFragment>().Single();
        Assert.True(body.Bounds.Top >= table.Bounds.Bottom);
        Assert.Equal((table, 0, 1), table.CellAt(new PointD(table.Bounds.Left + 150, table.Bounds.Top + 5)));
    }
}

public class MergedCellLayoutTests
{
    [Fact]
    public void Vertically_merged_cells_know_how_many_rows_they_span()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        TableCell C(string text, VerticalMerge merge = VerticalMerge.None) => new(ImmutableList.Create<Block>(Paragraph.FromText(text)), 1, merge);
        var table = new Table(
            [new TableRow([C("tall", VerticalMerge.Restart), C("b")]), new TableRow([C("", VerticalMerge.Continue), C("d")]), new TableRow([C("e"), C("f")])],
            [Twips.FromDips(100), Twips.FromDips(100)],
            hasBorders: true,
            Twips.Zero);
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(table))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        var fragment = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);

        Assert.Equal(2, fragment.Layout.Rows[0].Cells[0].MergedRows);
        Assert.Equal(1, fragment.Layout.Rows[0].Cells[1].MergedRows);
        Assert.Equal(1, fragment.Layout.Rows[1].Cells[0].MergedRows);
        Assert.Equal(1, fragment.Layout.Rows[2].Cells[0].MergedRows);
        Assert.Equal(5, fragment.CellParagraphs.Length); // the continuation cell has no paragraph fragment
        var hit = fragment.CellAt(new PointD(fragment.Bounds.Left + 10, fragment.RowTop(1) + 5));
        Assert.Equal((0, 0), (hit!.Value.Row, hit.Value.Cell));
    }
}

public class SequenceFieldLayoutTests
{
    private static string? SeqText(BlockFragment fragment) => ((ParagraphFragment)fragment).Layout.Input.Runs.Single(r => r.Kind == RunKind.Field).FieldText;

    [Fact]
    public void Caption_fields_show_their_running_number_and_relayout_when_a_caption_is_added_before_them()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(400),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        Paragraph Caption() => new([new Run("Figure "), new Field(FieldKind.Seq, "SEQ Figure \\* ARABIC", "7")]);
        var cache = new LayoutCache();
        var paginator = new Paginator(new FakeLineFormatter(), cache);
        Paragraph second = Caption();
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(second))), StyleSheet.Empty);
        LayoutDocument layout = paginator.Layout(document, new StyleResolver(document.Styles));
        Assert.Equal("1", SeqText(layout.Pages[0].Body[0]));

        Document grown = document.WithStory(StoryId.Body(0), document.Sections[0].Body.Insert(0, Caption()));
        LayoutDocument relaid = paginator.Layout(grown, new StyleResolver(grown.Styles));
        Assert.Equal("1", SeqText(relaid.Pages[0].Body[0]));
        Assert.Equal("2", SeqText(relaid.Pages[0].Body[1]));
    }
}

public class PageBorderLayoutTests
{
    [Fact]
    public void Pages_carry_their_section_border_and_the_border_rect_follows_the_distance_setting()
    {
        var box = new PageBorders { Space = 18, Width = 8 };
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(300),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(40),
            MarginBottom = Twips.FromDips(40),
            MarginLeft = Twips.FromDips(30),
            MarginRight = Twips.FromDips(30),
            PageBorders = box,
        };
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(Paragraph.FromText("a")))), StyleSheet.Empty, new DocumentSettings { PageColor = DocColor.Parse("FFF2CC") });
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        PageLayout first = layout.Pages[0];

        Assert.Same(box, first.Borders);
        Assert.Equal(DocColor.Parse("FFF2CC"), first.PageColor);
        RectD fromPage = first.BorderRect()!.Value;
        Assert.Equal(24, fromPage.Left, 3);
        Assert.Equal(300 - 24, fromPage.Right, 3);
        Assert.Equal(400 - 24, fromPage.Bottom, 3);

        Document textOffset = document.WithSection(0, document.Sections[0].WithProperties(page with { PageBorders = box with { FromText = true } }));
        PageLayout second = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(textOffset, new StyleResolver(textOffset.Styles)).Pages[0];
        RectD fromText = second.BorderRect()!.Value;
        Assert.Equal(30 - 24, fromText.Left, 3);
        Assert.Equal(300 - 30 + 24, fromText.Right, 3);
    }
}

public class TableAlignmentLayoutTests
{
    [Fact]
    public void Centred_and_right_aligned_tables_are_offset_within_the_column()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        Table Grid(TableAlignment alignment) => new([new TableRow([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("a")))])], [Twips.FromDips(100)], hasBorders: true, Twips.Zero, alignment: alignment);
        foreach ((TableAlignment alignment, double expectedLeft) in new[] { (TableAlignment.Left, 20.0), (TableAlignment.Center, 70.0), (TableAlignment.Right, 120.0) })
        {
            var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(Grid(alignment)))), StyleSheet.Empty);
            LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
            var fragment = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);
            Assert.Equal(expectedLeft, fragment.Bounds.Left, 3);
        }
    }
}

public class PageRefLayoutTests
{
    [Fact]
    public void Pageref_fields_show_the_page_their_bookmark_lands_on()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(80),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        var reference = new Paragraph([new Run("See page "), new Field(FieldKind.PageRef, "PAGEREF Target \\h", "1")]);
        var target = new Paragraph([new Run("Target text", new RunProperties { Bookmark = "Target" })]);
        var blocks = ImmutableList.Create<Block>(reference, Paragraph.FromText("filler"), Paragraph.FromText("filler"), target);
        var document = new Document(ImmutableList.Create(new Section(page, blocks)), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));

        Assert.True(layout.PageCount >= 2);
        var first = (ParagraphFragment)layout.Pages[0].Body[0];
        string pageOfTarget = layout.Find(new TextPosition(StoryId.Body(0), BlockPath.Of(3), 0))!.Value.Page.PageNumberText;
        Assert.NotEqual("1", pageOfTarget);
        Assert.Equal(pageOfTarget, first.Layout.Input.Runs.Single(r => r.Kind == RunKind.Field).FieldText);
    }
}

public class AutoFieldLayoutTests
{
    [Fact]
    public void Date_and_property_fields_show_live_values()
    {
        var page = new SectionProperties
        {
            PageWidth = Twips.FromDips(400),
            PageHeight = Twips.FromDips(400),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(20),
            MarginRight = Twips.FromDips(20),
        };
        var paragraph = new Paragraph([new Field(FieldKind.Date, "DATE \\@ \"yyyy\"", "1999"), new Run(" "), new Field(FieldKind.DocProperty, "TITLE", "old")]);
        var document = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create<Block>(paragraph))), StyleSheet.Empty, metadata: new DocumentMetadata { Title = "Report" });
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        List<string?> texts = ((ParagraphFragment)layout.Pages[0].Body[0]).Layout.Input.Runs.Where(r => r.Kind == RunKind.Field).Select(r => r.FieldText).ToList();
        Assert.Equal([DateTime.Now.Year.ToString(System.Globalization.CultureInfo.CurrentCulture), "Report"], texts);
    }
}

public class LineNumberLayoutTests
{
    [Fact]
    public void Line_numbers_count_body_lines_and_restart_per_page_when_asked()
    {
        LineNumbering Numbering(LineNumberRestart restart) => new() { Restart = restart };
        SectionProperties Page(LineNumberRestart restart) => new()
        {
            PageWidth = Twips.FromDips(240),
            PageHeight = Twips.FromDips(100),
            MarginTop = Twips.FromDips(20),
            MarginBottom = Twips.FromDips(20),
            MarginLeft = Twips.FromDips(40),
            MarginRight = Twips.FromDips(20),
            LineNumbers = Numbering(restart),
        };
        // 60 DIP of body per page and 20 DIP lines: three lines per page; five one-line paragraphs span two pages.
        ImmutableList<Block> blocks = ImmutableList.CreateRange<Block>(Enumerable.Range(1, 5).Select(i => (Block)Paragraph.FromText("line " + i)));
        var continuous = new Document(ImmutableList.Create(new Section(Page(LineNumberRestart.Continuous), blocks)), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(continuous, new StyleResolver(continuous.Styles));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal([1, 2, 3], layout.Pages[0].LineNumbers.Select(n => n.Number));
        Assert.Equal([4, 5], layout.Pages[1].LineNumbers.Select(n => n.Number));
        Assert.Equal(40 - 24, layout.Pages[0].LineNumberX, 3);
        Assert.True(layout.Pages[0].LineNumbers[1].Baseline > layout.Pages[0].LineNumbers[0].Baseline);

        var perPage = new Document(ImmutableList.Create(new Section(Page(LineNumberRestart.EachPage), blocks)), StyleSheet.Empty);
        LayoutDocument restarted = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(perPage, new StyleResolver(perPage.Styles));
        Assert.Equal([1, 2], restarted.Pages[1].LineNumbers.Select(n => n.Number));

        ImmutableList<Block> suppressed = blocks.SetItem(1, Paragraph.FromText("skip me").WithProperties(new ParagraphProperties { SuppressLineNumbers = true }));
        var withSuppressed = new Document(ImmutableList.Create(new Section(Page(LineNumberRestart.Continuous), suppressed)), StyleSheet.Empty);
        LayoutDocument skipped = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(withSuppressed, new StyleResolver(withSuppressed.Styles));
        Assert.Equal([1, 2], skipped.Pages[0].LineNumbers.Select(n => n.Number));

        var none = new Document(ImmutableList.Create(new Section(Page(LineNumberRestart.Continuous) with { LineNumbers = null }, blocks)), StyleSheet.Empty);
        Assert.Empty(new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(none, new StyleResolver(none.Styles)).Pages[0].LineNumbers);
    }
}

public class CellVerticalAlignmentTests
{
    private static SectionProperties Page() => new()
    {
        PageWidth = Twips.FromDips(400),
        PageHeight = Twips.FromDips(400),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    [Fact]
    public void Centre_and_bottom_alignment_shift_cell_content_inside_a_tall_row()
    {
        TableCell Cell(CellVerticalAlignment alignment) => new(ImmutableList.Create<Block>(Paragraph.FromText("x")), verticalAlignment: alignment);
        var table = new Table(
            [new TableRow([Cell(CellVerticalAlignment.Top), Cell(CellVerticalAlignment.Center), Cell(CellVerticalAlignment.Bottom)], Twips.FromDips(100), exactHeight: true)],
            [Twips.FromDips(100), Twips.FromDips(100), Twips.FromDips(100)],
            hasBorders: true,
            Twips.Zero);
        var document = new Document(ImmutableList.Create(new Section(Page(), ImmutableList.Create<Block>(table))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        var fragment = Assert.IsType<TableFragment>(layout.Pages[0].Body[0]);

        ImmutableArray<TableCellLayout> cells = fragment.Layout.Rows[0].Cells;
        double content = cells[0].ContentHeight;
        Assert.Equal(0, cells[0].ContentOffset);
        Assert.Equal((100 - content) / 2, cells[1].ContentOffset, 3);
        Assert.Equal(100 - content, cells[2].ContentOffset, 3);
        Assert.Equal(fragment.Bounds.Top, fragment.CellParagraphs[0].Bounds.Top, 3);
        Assert.Equal(fragment.Bounds.Top + (100 - content) / 2, fragment.CellParagraphs[1].Bounds.Top, 3);
        Assert.Equal(fragment.Bounds.Top + 100 - content, fragment.CellParagraphs[2].Bounds.Top, 3);
    }
}
