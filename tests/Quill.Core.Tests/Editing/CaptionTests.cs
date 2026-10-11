using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class CaptionTests
{
    private static string CaptionText(Document document, int index)
    {
        IReadOnlyDictionary<Field, int> numbers = SequenceFields.Numbers(document);
        return string.Concat(document.Para(index).Inlines.Select(i => i is Field f ? SequenceFields.Text(f, numbers) : i is Run r ? r.Text : string.Empty));
    }

    [Fact]
    public void Captions_go_below_pictures_and_above_tables_and_are_numbered_in_document_order()
    {
        var session = new EditingSession(WithParagraphs("First picture", "Second picture", "Tail"));
        session.MoveCaret(Pos(1, 3), extend: false);
        session.InsertCaption("Figure", "Second", above: false);
        Assert.Equal(["First picture", "Second picture", "Figure 1: Second", "Tail"], session.Document.BodyTexts().Select((t, i) => i == 2 ? CaptionText(session.Document, 2) : t));
        Assert.Equal(CaptionSupport.StyleId, session.Document.Para(2).StyleId);
        Assert.True(session.Document.Styles.Contains(CaptionSupport.StyleId));
        Assert.Equal(Pos(2, session.Document.Para(2).Length), session.Selection.Active);

        // A caption added earlier in the document takes number 1 and the later one becomes 2.
        session.MoveCaret(Pos(0, 0), extend: false);
        session.InsertCaption("Figure", null, above: false);
        Assert.Equal("Figure 1", CaptionText(session.Document, 1));
        Assert.Equal("Figure 2: Second", CaptionText(session.Document, 3));
        Assert.Equal("2", ((Field)session.Document.Para(3).Inlines[1]).CachedResult);
        Assert.Equal(3, session.NextCaptionNumber("Figure"));
        Assert.Equal(1, session.NextCaptionNumber("Table"));

        session.MoveCaret(Pos(4, 0), extend: false);
        session.InsertTable(1, 1, Twips.FromInches(6));
        Assert.Equal("Table", session.DefaultCaptionLabel);
        session.InsertCaption("Table", "Data", above: true);
        Assert.Equal("Table 1: Data", CaptionText(session.Document, 4));
        Assert.IsType<Table>(session.Document.Sections[0].Body[5]);
        Assert.Equal("SEQ Table \\* ARABIC", ((Field)session.Document.Para(4).Inlines[1]).Instruction);

        session.Undo();
        Assert.Equal(6, session.Document.Sections[0].Body.Count);
    }

    [Fact]
    public void Sequence_numbers_count_captions_inside_table_cells_and_identifiers_come_from_the_instruction()
    {
        var field = new Field(FieldKind.Seq, "SEQ Figure \\* ARABIC", "9");
        Assert.Equal("Figure", SequenceFields.Identifier(field));
        Assert.Null(SequenceFields.Identifier(Field.Page()));
        Assert.Equal("My Series", SequenceFields.Identifier(new Field(FieldKind.Seq, SequenceFields.Instruction("My Series"), "1")));

        var cellCaption = new Paragraph([new Run("Figure "), new Field(FieldKind.Seq, "SEQ Figure \\* ARABIC", "1")], CaptionSupport.StyleId);
        var table = new Table([new TableRow([new TableCell(ImmutableList.Create<Block>(cellCaption))])], [Twips.FromInches(2)], hasBorders: true, Twips.Zero);
        var after = new Paragraph([new Run("Figure "), field]);
        Document document = WithBlocks(table, after);
        IReadOnlyDictionary<Field, int> numbers = SequenceFields.Numbers(document);
        Assert.Equal(1, numbers[(Field)cellCaption.Inlines[1]]);
        Assert.Equal(2, numbers[field]);
        Assert.Equal("2", SequenceFields.Text(field, numbers));
        Assert.Equal("9", SequenceFields.Text(field, null));

        Document refreshed = SequenceFields.RefreshResults(document);
        Assert.Equal("2", ((Field)refreshed.Para(1).Inlines[1]).CachedResult);
        Assert.Same(refreshed, SequenceFields.RefreshResults(refreshed));
    }
}
