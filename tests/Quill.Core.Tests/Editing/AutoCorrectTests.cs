using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class AutoCorrectTests
{
    private static EditingSession Session(string text = "")
    {
        var session = new EditingSession(WithParagraphs(text), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, text.Length), extend: false);
        return session;
    }

    private static void Type(EditingSession session, string text)
    {
        foreach (char c in text)
        {
            session.TypeText(c.ToString());
        }
    }

    [Fact]
    public void Straight_quotes_become_curly()
    {
        EditingSession session = Session();
        session.AutoCorrect = AutoCorrectOptions.Default with { CapitalizeSentences = false };
        Type(session, "say \"hi\" it's (\"ok\")");
        Assert.Equal("say “hi” it’s (“ok”)", session.Document.Text(0));
    }

    [Fact]
    public void Symbols_and_dashes_are_replaced()
    {
        EditingSession session = Session();
        Type(session, "(c) 2026 wait... a-->b 1/2 cup x--y and -- z ");
        Assert.Equal("© 2026 wait… a→b ½ cup x—y and – z ", session.Document.Text(0));

        EditingSession dates = Session();
        Type(dates, "1/25 ");
        Assert.Equal("1/25 ", dates.Document.Text(0));
    }

    [Fact]
    public void Sentence_starts_are_capitalized_and_undo_restores_them()
    {
        EditingSession session = Session();
        Type(session, "hello there. this is it! really? yes e.g. NASA and iPhone. ok");
        Assert.Equal("Hello there. This is it! Really? Yes e.g. NASA and iPhone. ok", session.Document.Text(0));
        session.Undo();
        Assert.Equal("Hello there. This is it! Really? Yes e.g. NASA and iPhone. ", session.Document.Text(0));
    }

    [Fact]
    public void Star_space_starts_a_bullet_list_and_one_dot_space_a_numbered_one()
    {
        EditingSession session = Session();
        Type(session, "* ");
        Assert.Equal("", session.Document.Text(0));
        Assert.True(session.ListKind(session.Document.Para(0)));
        session.Undo();
        Assert.Equal("*", session.Document.Text(0));
        Assert.Null(session.ListKind(session.Document.Para(0)));

        EditingSession numbered = Session();
        Type(numbered, "1. First");
        Assert.False(numbered.ListKind(numbered.Document.Para(0)));
        Assert.Equal("First", numbered.Document.Text(0));
    }

    [Fact]
    public void Rules_can_be_switched_off_and_skip_links()
    {
        EditingSession session = Session();
        session.AutoCorrect = AutoCorrectOptions.Off;
        Type(session, "\"x\" (c) hello. there");
        Assert.Equal("\"x\" (c) hello. there", session.Document.Text(0));

        EditingSession linked = Session();
        linked.InsertLink("see", "https://example.com");
        Type(linked, "\"");
        Assert.Equal("see\"", linked.Document.Text(0));
    }
}
