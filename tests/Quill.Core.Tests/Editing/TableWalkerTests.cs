using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TableWalkerTests
{
    private static EditingSession SessionWithGrid()
    {
        var session = new EditingSession(WithParagraphs("Before", "After"));
        session.MoveCaret(Pos(1, 0), extend: false);
        session.InsertTable(1, 2, Twips.FromInches(6));
        return session;
    }

    private static TextPosition Cell(int row, int cell, int offset) => new(Body, BlockPath.Of(1).Child(row, cell, 0), offset);

    [Fact]
    public void Walker_visits_cell_paragraphs_with_nested_paths_and_maps_them()
    {
        EditingSession session = SessionWithGrid();
        session.InsertText("one");
        session.MoveCaret(Cell(0, 1, 0), extend: false);
        session.InsertText("two");

        var visited = BlockWalker.Paragraphs(session.Document.Sections[0].Body).Select(p => (p.Path.ToString(), p.Paragraph.FlatText)).ToList();
        Assert.Equal(["Before", "one", "two", "After"], visited.Select(v => v.Item2));
        Assert.Equal(BlockPath.Of(1).Child(0, 1, 0).ToString(), visited[2].Item1);
        Assert.Equal(4, DocumentEditor.AllParagraphs(session.Document).Count());

        ImmutableList<Block> upper = BlockWalker.MapParagraphs(session.Document.Sections[0].Body, p => p.FlatText == "two" ? Paragraph.FromText("TWO") : p);
        Assert.Same(session.Document.Sections[0].Body[0], upper[0]);
        Assert.Equal("TWO", ((Paragraph)((Table)upper[1]).Rows[0].Cells[1].Blocks[0]).FlatText);
        Assert.Null(((Table)upper[1]).OuterXml);
        Assert.Equal(4, DocumentStatistics.Compute(session.Document).Words);
    }

    [Fact]
    public void Comments_and_footnotes_inside_cells_are_found_by_their_nested_paths()
    {
        EditingSession session = SessionWithGrid();
        session.InsertText("noted");
        session.MoveCaret(Cell(0, 0, 0), extend: false);
        session.MoveCaret(Cell(0, 0, 5), extend: true);
        int id = session.AddComment("Ada");
        Assert.Equal(new TextRange(Cell(0, 0, 0), Cell(0, 0, 5)), session.CommentRange(id));

        session.DeleteComment(id);
        Assert.Null(session.CommentRange(id));
        Assert.Empty(session.Document.Comments);

        session.MoveCaret(Cell(0, 1, 0), extend: false);
        session.InsertText("x");
        session.InsertFootnote();
        Assert.True(session.Selection.Story.IsFootnote);
        Assert.Equal(Cell(0, 1, 2), session.FootnoteReferencePosition());
        Assert.Single(FootnoteSupport.ReferencedNoteIds(session.Document));
    }
}
