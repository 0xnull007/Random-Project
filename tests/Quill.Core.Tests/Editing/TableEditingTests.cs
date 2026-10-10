using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TableEditingTests
{
    private static Table Grid(params string[][] rows) => new(
        rows.Select(cells => new TableRow(cells.Select(t => new TableCell(ImmutableList.Create<Block>(Paragraph.FromText(t)))).ToImmutableArray())).ToImmutableArray(),
        [Twips.FromInches(2), Twips.FromInches(2)],
        hasBorders: true,
        Twips.Zero,
        outerXml: "<w:tbl/>");

    private static Document Doc() => WithBlocks(Paragraph.FromText("Before"), Grid(["a", "b"], ["c", "d"]), Paragraph.FromText("After"));

    private static TextPosition Cell(int row, int cell, int block, int offset) => new(Body, BlockPath.Of(1).Child(row, cell, block), offset);

    private static Table TableOf(Document document) => (Table)document.Sections[0].Body[1];

    private static string CellText(Document document, int row, int cell, int block = 0) =>
        ((Paragraph)TableOf(document).Rows[row].Cells[cell].Blocks[block]).FlatText;

    [Fact]
    public void Nested_paths_resolve_to_cell_blocks()
    {
        Document document = Doc();
        Assert.Equal("d", ((Paragraph)document.TryGetBlock(Body, BlockPath.Of(1).Child(1, 1, 0))!).FlatText);
        Assert.Null(document.TryGetBlock(Body, BlockPath.Of(1).Child(2, 0, 0)));
        Assert.Null(document.TryGetBlock(Body, BlockPath.Of(0).Child(0, 0, 0)));
        Assert.True(BlockPath.Of(1).Child(0, 1, 0).SameContainer(BlockPath.Of(1).Child(0, 1, 3)));
        Assert.False(BlockPath.Of(1).Child(0, 1, 0).SameContainer(BlockPath.Of(1).Child(1, 1, 0)));
        Assert.Equal(BlockPath.Of(1), BlockPath.Of(1).Child(0, 1, 0).TablePath);
        Assert.Equal((0, 1), BlockPath.Of(1).Child(0, 1, 0).CellIndices);
    }

    [Fact]
    public void Typing_in_a_cell_changes_only_that_cell_and_drops_the_saved_xml()
    {
        var session = new EditingSession(Doc());
        session.MoveCaret(Cell(0, 1, 0, 1), extend: false);
        session.InsertText("X");

        Assert.Equal("bX", CellText(session.Document, 0, 1));
        Assert.Equal("a", CellText(session.Document, 0, 0));
        Assert.Null(TableOf(session.Document).OuterXml);
        Assert.Equal(3, session.Document.Sections[0].Body.Count);
        Assert.Equal("Before", session.Document.Text(0));
        Assert.Equal(Cell(0, 1, 0, 2), session.Selection.Active);

        session.Undo();
        Assert.Equal("b", CellText(session.Document, 0, 1));
        Assert.NotNull(TableOf(session.Document).OuterXml);
    }

    [Fact]
    public void Enter_splits_the_paragraph_inside_the_cell()
    {
        var session = new EditingSession(Doc());
        session.MoveCaret(Cell(1, 0, 0, 1), extend: false);
        session.InsertParagraphBreak();

        Assert.Equal(2, TableOf(session.Document).Rows[1].Cells[0].Blocks.Count);
        Assert.Equal("c", CellText(session.Document, 1, 0, 0));
        Assert.Equal("", CellText(session.Document, 1, 0, 1));
        Assert.Equal(3, session.Document.Sections[0].Body.Count);
        Assert.Equal(Cell(1, 0, 1, 0), session.Selection.Active);

        session.Backspace();
        Assert.Single(TableOf(session.Document).Rows[1].Cells[0].Blocks);
        Assert.Equal(Cell(1, 0, 0, 1), session.Selection.Active);
    }

    [Fact]
    public void Backspace_and_delete_stop_at_cell_boundaries()
    {
        Document document = Doc();
        var session = new EditingSession(document);
        session.MoveCaret(Cell(0, 1, 0, 0), extend: false);
        session.Backspace();
        Assert.Same(document, session.Document);
        Assert.Equal(Cell(0, 1, 0, 0), session.Selection.Active);

        session.MoveCaret(Cell(0, 0, 0, 1), extend: false);
        session.Delete();
        Assert.Same(document, session.Document);

        session.MoveCaret(Pos(0, 6), extend: false);
        session.Delete();
        Assert.Same(document, session.Document);
    }

    [Fact]
    public void A_range_cannot_span_two_cells()
    {
        Document document = Doc();
        EditResult result = DocumentEditor.DeleteRange(document, new TextRange(Cell(0, 0, 0, 0), Cell(0, 1, 0, 1)));
        Assert.Same(document, result.Document);
        Assert.Same(document, DocumentEditor.ApplyRunFormat(document, new TextRange(Cell(0, 0, 0, 0), Cell(1, 0, 0, 1)), new RunProperties { Bold = true }).Document);
    }

    [Fact]
    public void Tab_order_walks_cells_row_by_row()
    {
        Document document = Doc();
        Assert.Equal(Cell(0, 1, 0, 0), TextNavigation.NextCell(document, Cell(0, 0, 0, 0)));
        Assert.Equal(Cell(1, 0, 0, 0), TextNavigation.NextCell(document, Cell(0, 1, 0, 1)));
        Assert.Null(TextNavigation.NextCell(document, Cell(1, 1, 0, 0)));
        Assert.Equal(Cell(0, 1, 0, 0), TextNavigation.PreviousCell(document, Cell(1, 0, 0, 0)));
        Assert.Null(TextNavigation.PreviousCell(document, Cell(0, 0, 0, 0)));
        Assert.Null(TextNavigation.NextCell(document, Pos(0, 0)));
        Assert.Equal(Cell(1, 1, 0, 1), TextNavigation.ContainerEnd(document, Cell(1, 1, 0, 0)));
    }

    [Fact]
    public void Arrow_keys_cross_table_boundaries()
    {
        Document document = Doc();
        Assert.Equal(Cell(0, 0, 0, 0), TextNavigation.NextCharacter(document, Pos(0, 6)));
        Assert.Equal(Cell(0, 1, 0, 0), TextNavigation.NextCharacter(document, Cell(0, 0, 0, 1)));
        Assert.Equal(Pos(2, 0), TextNavigation.NextCharacter(document, Cell(1, 1, 0, 1)));
        Assert.Equal(Cell(1, 1, 0, 1), TextNavigation.PreviousCharacter(document, Pos(2, 0)));
        Assert.Equal(Cell(0, 0, 0, 1), TextNavigation.PreviousCharacter(document, Cell(0, 1, 0, 0)));
        Assert.Equal(Pos(0, 6), TextNavigation.PreviousCharacter(document, Cell(0, 0, 0, 0)));
    }

    [Fact]
    public void Find_searches_inside_cells()
    {
        IReadOnlyList<TextRange> hits = TextSearch.FindAll(Doc(), "c");
        Assert.Contains(hits, h => h.Start == Cell(1, 0, 0, 0) && h.End == Cell(1, 0, 0, 1));
    }

    [Fact]
    public void Moving_text_into_a_table_that_follows_the_removed_blocks_keeps_the_target()
    {
        Document document = WithBlocks(Paragraph.FromText("One"), Paragraph.FromText("Two"), Grid(["a", "b"]), Paragraph.FromText("After"));
        var session = new EditingSession(document);
        var target = new TextPosition(Body, BlockPath.Of(2).Child(0, 0, 0), 1);
        session.MoveText(new TextRange(Pos(0, 1), Pos(1, 1)), target, copy: false);

        Assert.Equal(["Owo", "<Table(1x2)>", "After"], session.Document.BodyTexts());
        var table = (Table)session.Document.Sections[0].Body[1];
        Assert.Equal(["ane", "T"], table.Rows[0].Cells[0].Blocks.Select(b => ((Paragraph)b).FlatText));
    }
}
