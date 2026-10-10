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
