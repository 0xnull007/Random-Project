using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class HyperlinkTests
{
    [Fact]
    public void Selection_becomes_a_link_and_can_be_found_edited_and_removed()
    {
        var session = new EditingSession(WithParagraphs("Visit the site today"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 6), extend: false);
        session.MoveCaret(Pos(0, 14), extend: true);
        session.InsertLink("the site", "https://example.com");

        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal("Visit the site today", paragraph.FlatText);
        Assert.Equal("https://example.com", paragraph.Inlines[1].Properties.Link);
        Assert.Equal(DefaultStyleSheet.HyperlinkStyleId, paragraph.Inlines[1].StyleId);
        Assert.Null(paragraph.Inlines[0].Properties.Link);
        Assert.Equal(Pos(0, 14), session.Selection.Active);

        session.MoveCaret(Pos(0, 9), extend: false);
        (TextRange range, string url) = session.LinkAtCaret()!.Value;
        Assert.Equal(Range(0, 6, 0, 14), range);
        Assert.Equal("https://example.com", url);

        session.InsertLink("our site", "https://example.org");
        Assert.Equal("Visit our site today", session.Document.Text(0));
        Assert.Equal("https://example.org", session.LinkAt(Pos(0, 8))!.Value.Url);

        session.MoveCaret(Pos(0, 8), extend: false);
        session.RemoveLink();
        Assert.Null(session.LinkAtCaret());
        Assert.Single(session.Document.Para(0).Inlines);
        Assert.Null(session.Document.Para(0).Inlines[0].StyleId);
    }

    [Fact]
    public void Collapsed_caret_inserts_the_link_text()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertLink(string.Empty, "mailto:me@example.com");
        Assert.Equal("amailto:me@example.comb", session.Document.Text(0));
        Assert.Equal("mailto:me@example.com", session.LinkAt(Pos(0, 3))!.Value.Url);
        Assert.Null(session.LinkAt(Pos(0, 0)));
        Assert.Null(session.LinkAt(Pos(0, 23)));
        session.Undo();
        Assert.Equal("ab", session.Document.Text(0));
    }

    [Fact]
    public void Clear_formatting_and_format_painter_keep_links_in_place()
    {
        var session = new EditingSession(WithParagraphs("link here"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 4), extend: true);
        session.InsertLink("link", "https://example.com");
        session.MoveCaret(Pos(0, 2), extend: false);
        FormatSample sample = session.CopyFormat();
        Assert.Null(sample.Run.Link);
        Assert.Null(sample.CharacterStyleId);

        session.ClearFormatting();
        Assert.Equal("https://example.com", session.LinkAt(Pos(0, 2))!.Value.Url);
    }
}
