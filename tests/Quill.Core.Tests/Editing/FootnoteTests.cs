using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class FootnoteTests
{
    [Fact]
    public void Inserting_a_footnote_adds_a_reference_and_a_note_story_and_moves_the_caret_there()
    {
        var session = new EditingSession(WithParagraphs("Hello world"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 5), extend: false);
        session.InsertFootnote();

        Paragraph body = session.Document.Para(0);
        var reference = Assert.IsType<FootnoteReference>(body.Inlines[1]);
        Assert.False(reference.IsNoteMark);
        Assert.Equal(FootnoteSupport.ReferenceStyleId, reference.StyleId);
        Assert.Equal("Hello￼ world", body.FlatText);

        StoryId note = StoryId.Footnote(reference.NoteId);
        Assert.Equal(note, session.Selection.Story);
        Assert.Equal(2, session.Selection.Active.Offset);
        var noteParagraph = (Paragraph)session.Document.GetStory(note)[0];
        Assert.Equal(FootnoteSupport.TextStyleId, noteParagraph.StyleId);
        Assert.True(((FootnoteReference)noteParagraph.Inlines[0]).IsNoteMark);

        session.InsertText("Note text");
        Assert.Equal("￼ Note text", ((Paragraph)session.Document.GetStory(note)[0]).FlatText);
        Assert.Equal(1, FootnoteSupport.Numbers(session.Document)[reference.NoteId]);
        Assert.Equal(Pos(0, 6), session.FootnoteReferencePosition());

        session.MoveCaret(Pos(0, 11), extend: false);
        session.InsertFootnote();
        Assert.Equal(2, FootnoteSupport.ReferencedNoteIds(session.Document).Count);
        Assert.Equal([1, 2], FootnoteSupport.ReferencedNoteIds(session.Document).Select(id => FootnoteSupport.Numbers(session.Document)[id]));

        session.Undo();
        session.Undo();
        session.Undo();
        Assert.Equal("Hello world", session.Document.Text(0));
        Assert.True(session.Document.Footnotes.IsEmpty);
    }
}
