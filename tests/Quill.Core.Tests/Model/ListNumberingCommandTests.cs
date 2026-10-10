using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Model;

public class ListNumberingCommandTests
{
    private static string[] Markers(EditingSession session)
    {
        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(session.Document.Sections[0].Body, session.Document.Lists, session.Resolver);
        return Enumerable.Range(0, session.Document.Sections[0].Body.Count).Select(i => markers.TryGetValue(i, out ListMarker m) ? m.Text : string.Empty).ToArray();
    }

    private static EditingSession NumberedList(params string[] items)
    {
        var session = new EditingSession(WithParagraphs(items), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(items.Length - 1, items[^1].Length), extend: true);
        session.ToggleList(bulleted: false);
        return session;
    }

    [Fact]
    public void Restart_and_continue_numbering()
    {
        EditingSession session = NumberedList("a", "b", "c", "d");
        Assert.Equal(["1.", "2.", "3.", "4."], Markers(session));

        session.MoveCaret(Pos(2, 0), extend: false);
        session.RestartNumbering();
        Assert.Equal(["1.", "2.", "1.", "2."], Markers(session));

        session.MoveCaret(Pos(3, 0), extend: false);
        session.ContinueNumbering();
        Assert.Equal(["1.", "2.", "1.", "2."], Markers(session)); // c and d already share a list

        session.MoveCaret(Pos(2, 0), extend: false);
        session.ContinueNumbering();
        Assert.Equal(["1.", "2.", "3.", "4."], Markers(session));

        session.Undo();
        Assert.Equal(["1.", "2.", "1.", "2."], Markers(session));
    }

    [Fact]
    public void List_style_changes_the_current_level_for_the_whole_list()
    {
        EditingSession session = NumberedList("a", "b", "c");
        session.MoveCaret(Pos(1, 0), extend: false);
        session.SetListStyle(NumberFormat.LowerLetter, "%1)");
        Assert.Equal(["a)", "b)", "c)"], Markers(session));

        session.SetListStyle(NumberFormat.Bullet, "–");
        Assert.Equal(["–", "–", "–"], Markers(session));
        Assert.True(session.ListKind(session.Document.Para(0)));
    }
}
