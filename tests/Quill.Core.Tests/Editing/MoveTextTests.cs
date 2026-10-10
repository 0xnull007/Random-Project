using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class MoveTextTests
{
    [Fact]
    public void Moves_within_a_paragraph_forward_and_backward()
    {
        var session = new EditingSession(WithParagraphs("one two three"), new UndoStack(time: new FakeTime()));
        session.MoveText(Range(0, 0, 0, 4), Pos(0, 13), copy: false);
        Assert.Equal("two threeone ", session.Document.Text(0));
        Assert.Equal(Range(0, 9, 0, 13), session.Selection.Range);

        session.MoveText(Range(0, 9, 0, 13), Pos(0, 0), copy: false);
        Assert.Equal("one two three", session.Document.Text(0));
        session.Undo();
        Assert.Equal("two threeone ", session.Document.Text(0));
    }

    [Fact]
    public void Moves_across_paragraphs_and_copies_when_asked()
    {
        var session = new EditingSession(WithParagraphs("ab", "cd", "ef"), new UndoStack(time: new FakeTime()));
        session.MoveText(Range(0, 1, 1, 1), Pos(2, 1), copy: false);
        Assert.Equal(["ad", "eb", "cf"], session.Document.BodyTexts());

        session.MoveText(Range(0, 0, 0, 1), Pos(2, 2), copy: true);
        Assert.Equal(["ad", "eb", "cfa"], session.Document.BodyTexts());
        Assert.Equal(Range(2, 2, 2, 3), session.Selection.Range);
    }

    [Fact]
    public void Dropping_onto_the_selection_itself_does_nothing()
    {
        var session = new EditingSession(WithParagraphs("hello"), new UndoStack(time: new FakeTime()));
        session.MoveText(Range(0, 1, 0, 3), Pos(0, 2), copy: false);
        Assert.Equal("hello", session.Document.Text(0));
        Assert.False(session.IsDirty);
    }
}
