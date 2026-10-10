using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class CommentTests
{
    [Fact]
    public void Add_edit_find_and_delete_a_comment()
    {
        var session = new EditingSession(WithParagraphs("Hello brave world"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 6), extend: false);
        session.MoveCaret(Pos(0, 11), extend: true);
        int id = session.AddComment("Ada Lovelace", "Really?");

        Comment comment = session.Document.Comments[id];
        Assert.Equal("Ada Lovelace", comment.Author);
        Assert.Equal("AL", comment.Initials);
        Assert.Equal("Really?", comment.Text);
        Assert.Equal(Range(0, 6, 0, 11), session.CommentRange(id));
        Assert.Equal(id, session.Document.Para(0).Inlines[1].Properties.CommentId);
        Assert.Null(session.Document.Para(0).Inlines[0].Properties.CommentId);
        Assert.Equal([id], session.CommentsAtSelection());

        session.SetCommentText(id, "Yes.\nTwo lines");
        Assert.Equal("Yes.\nTwo lines", session.Document.Comments[id].Text);

        session.DeleteComment(id);
        Assert.True(session.Document.Comments.IsEmpty);
        Assert.Single(session.Document.Para(0).Inlines);
        Assert.Null(session.CommentRange(id));

        session.Undo();
        Assert.Single(session.Document.Comments);
    }

    [Fact]
    public void Collapsed_caret_comments_the_word()
    {
        var session = new EditingSession(WithParagraphs("one two"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 5), extend: false);
        int id = session.AddComment("Me");
        Assert.Equal(Range(0, 4, 0, 7), session.CommentRange(id));
    }
}
