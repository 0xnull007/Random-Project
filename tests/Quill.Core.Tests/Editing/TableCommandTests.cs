using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TableCommandTests
{
    private static readonly Twips ColumnWidth = Twips.FromInches(6);

    private static Table TableOf(EditingSession session, int index = 1) => (Table)session.Document.Sections[0].Body[index];

    private static TextPosition Cell(int table, int row, int cell, int block = 0, int offset = 0) => new(Body, BlockPath.Of(table).Child(row, cell, block), offset);

    private static string CellText(Table table, int row, int cell) => ((Paragraph)table.Rows[row].Cells[cell].Blocks[0]).FlatText;

    private static EditingSession SessionWithGrid()
    {
        var session = new EditingSession(WithParagraphs("Before", "After"));
        session.MoveCaret(Pos(1, 0), extend: false);
        session.InsertTable(2, 3, ColumnWidth);
        return session;
    }

    [Fact]
    public void Insert_at_a_paragraph_start_puts_the_table_before_it_and_the_caret_in_the_first_cell()
    {
        EditingSession session = SessionWithGrid();
        Assert.Equal(["Before", "<Table(2x3)>", "After"], session.Document.BodyTexts());
        Table table = TableOf(session);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(ColumnWidth.Value, table.ColumnWidths.Sum(w => w.Value));
        Assert.True(table.HasBorders);
        Assert.Equal(Cell(1, 0, 0), session.Selection.Active);
        Assert.Equal(Twips.Zero, ((Paragraph)table.Rows[0].Cells[0].Blocks[0]).Properties.SpaceAfter);

        session.Undo();
        Assert.Equal(["Before", "After"], session.Document.BodyTexts());
    }

    [Fact]
    public void Insert_in_the_middle_splits_the_paragraph_and_at_the_end_keeps_a_paragraph_after_the_table()
    {
        var middle = new EditingSession(WithParagraphs("Hello world"));
        middle.MoveCaret(Pos(0, 5), extend: false);
        middle.InsertTable(1, 1, ColumnWidth);
        Assert.Equal(["Hello", "<Table(1x1)>", " world"], middle.Document.BodyTexts());

        var end = new EditingSession(WithParagraphs("Last"));
        end.MoveCaret(Pos(0, 4), extend: false);
        end.InsertTable(1, 2, ColumnWidth);
        Assert.Equal(["Last", "<Table(1x2)>", ""], end.Document.BodyTexts());
    }

    [Fact]
    public void Rows_are_inserted_above_and_below_and_tab_order_reaches_them()
    {
        EditingSession session = SessionWithGrid();
        session.MoveCaret(Cell(1, 1, 2), extend: false);
        session.InsertText("x");
        session.InsertRow(below: true);
        Assert.Equal(3, TableOf(session).Rows.Length);
        Assert.Equal(Cell(1, 2, 2), session.Selection.Active);
        Assert.Equal("", CellText(TableOf(session), 2, 2));
        Assert.Equal("x", CellText(TableOf(session), 1, 2));

        session.InsertRow(below: false);
        Assert.Equal(4, TableOf(session).Rows.Length);
        Assert.Equal(Cell(1, 2, 2), session.Selection.Active);
        Assert.Equal("x", CellText(TableOf(session), 1, 2));
        Assert.Null(TableOf(session).OuterXml);
    }

    [Fact]
    public void Columns_are_inserted_keeping_the_table_width()
    {
        EditingSession session = SessionWithGrid();
        session.MoveCaret(Cell(1, 0, 1), extend: false);
        session.InsertText("mid");
        session.InsertColumn(right: true);
        Table table = TableOf(session);
        Assert.Equal(4, table.ColumnCount);
        Assert.All(table.Rows, r => Assert.Equal(4, r.Cells.Length));
        Assert.InRange(table.ColumnWidths.Sum(w => w.Value), ColumnWidth.Value - 4, ColumnWidth.Value + 4);
        Assert.Equal("mid", CellText(table, 0, 1));
        Assert.Equal("", CellText(table, 0, 2));
        Assert.Equal(Cell(1, 0, 2), session.Selection.Active);

        session.InsertColumn(right: false);
        Assert.Equal(5, TableOf(session).ColumnCount);
        Assert.Equal(Cell(1, 0, 2), session.Selection.Active);
        Assert.Equal("mid", CellText(TableOf(session), 0, 1));
    }

    [Fact]
    public void A_spanning_cell_grows_instead_of_splitting_when_a_column_is_added_under_it()
    {
        var head = new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("head")), gridSpan: 2);
        var table = new Table(
            [new TableRow([head]), new TableRow([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("a"))), new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("b")))])],
            [Twips.FromInches(3), Twips.FromInches(3)],
            hasBorders: true,
            Twips.Zero);
        var session = new EditingSession(WithBlocks(Paragraph.FromText("p"), table));
        session.MoveCaret(Cell(1, 1, 0), extend: false);
        session.InsertColumn(right: true);
        Table updated = TableOf(session);
        Assert.Equal(3, updated.ColumnCount);
        Assert.Single(updated.Rows[0].Cells);
        Assert.Equal(3, updated.Rows[0].Cells[0].GridSpan);
        Assert.Equal(3, updated.Rows[1].Cells.Length);
        Assert.Equal(["a", "", "b"], updated.Rows[1].Cells.Select(c => ((Paragraph)c.Blocks[0]).FlatText));

        session.DeleteColumn();
        Assert.Equal(2, TableOf(session).ColumnCount);
        Assert.Equal(2, TableOf(session).Rows[0].Cells[0].GridSpan);
        Assert.Equal(["a", "b"], TableOf(session).Rows[1].Cells.Select(c => ((Paragraph)c.Blocks[0]).FlatText));
    }

    [Fact]
    public void Deleting_rows_columns_and_the_table_moves_the_caret_sensibly()
    {
        EditingSession session = SessionWithGrid();
        session.MoveCaret(Cell(1, 1, 1), extend: false);
        session.InsertText("keep");
        session.MoveCaret(Cell(1, 0, 1), extend: false);
        session.DeleteRow();
        Assert.Single(TableOf(session).Rows);
        Assert.Equal("keep", CellText(TableOf(session), 0, 1));
        Assert.Equal(Cell(1, 0, 1), session.Selection.Active);

        session.DeleteColumn();
        Assert.Equal(2, TableOf(session).ColumnCount);
        Assert.Equal(Cell(1, 0, 1), session.Selection.Active);

        session.DeleteTable();
        Assert.Equal(["Before", "After"], session.Document.BodyTexts());
        Assert.Equal(Pos(1, 0), session.Selection.Active);

        var only = new EditingSession(WithParagraphs(""));
        only.InsertTable(1, 1, ColumnWidth);
        only.DeleteRow();
        Assert.Equal([""], only.Document.BodyTexts());
        Assert.Equal(Pos(0, 0), only.Selection.Active);
    }

    [Fact]
    public void Borders_toggle_and_commands_do_nothing_outside_tables()
    {
        EditingSession session = SessionWithGrid();
        session.ToggleTableBorders();
        Assert.False(TableOf(session).HasBorders);
        Assert.Equal(Cell(1, 0, 0), session.Selection.Active);

        Document before = session.Document;
        session.MoveCaret(Pos(0, 0), extend: false);
        Assert.False(session.IsInTable);
        session.InsertRow(below: true);
        session.InsertColumn(right: true);
        session.DeleteRow();
        session.DeleteColumn();
        session.DeleteTable();
        session.ToggleTableBorders();
        Assert.Same(before, session.Document);
    }

    [Fact]
    public void Header_rows_shading_and_column_widths_are_table_formatting()
    {
        EditingSession session = SessionWithGrid();
        Assert.False(session.IsHeaderRowAtCaret);
        session.ToggleHeaderRow();
        Assert.True(TableOf(session).Rows[0].IsHeader);
        Assert.True(session.IsHeaderRowAtCaret);
        Assert.Equal(Cell(1, 0, 0), session.Selection.Active);

        session.SetCellShading(DocColor.Parse("FFFF00"));
        Assert.Equal(DocColor.Parse("FFFF00"), session.CellShadingAtCaret);
        Assert.Null(TableOf(session).Rows[0].Cells[1].Shading);
        session.SetCellShading(null);
        Assert.Null(session.CellShadingAtCaret);

        ImmutableArray<Twips> widths = [Twips.FromInches(1), Twips.FromInches(2), Twips.FromInches(3)];
        Document before = session.Document;
        session.PreviewColumnWidths(Body, BlockPath.Of(1), [Twips.FromInches(2), Twips.FromInches(2), Twips.FromInches(2)]);
        session.PreviewColumnWidths(Body, BlockPath.Of(1), widths);
        Assert.Equal(widths, TableOf(session).ColumnWidths);
        session.EndColumnResize();
        Assert.Equal(widths, TableOf(session).ColumnWidths);
        session.Undo();
        Assert.Same(before, session.Document);
        session.Redo();
        Assert.Equal(widths, TableOf(session).ColumnWidths);

        session.SetColumnWidths(Body, BlockPath.Of(1), [Twips.FromInches(3), Twips.FromInches(2), Twips.FromInches(1)]);
        Assert.Equal(Twips.FromInches(3), TableOf(session).ColumnWidths[0]);
    }
}
