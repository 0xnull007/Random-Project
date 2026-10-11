using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class BookmarkTests
{
    [Fact]
    public void Bookmarks_mark_a_range_can_be_found_listed_jumped_to_and_removed()
    {
        var session = new EditingSession(WithParagraphs("Alpha beta gamma", "Second"));
        Assert.False(session.AddBookmark("1bad"));
        Assert.False(session.AddBookmark("has space"));

        session.MoveCaret(Pos(0, 6), extend: false);
        session.MoveCaret(Pos(0, 10), extend: true);
        Assert.True(session.AddBookmark("Mark1"));
        Assert.Equal(Range(0, 6, 0, 10), BookmarkSupport.Find(session.Document, "mark1"));
        Assert.Equal("beta", BookmarkSupport.TextOf(session.Document, "Mark1"));
        Assert.Equal(["Mark1"], session.BookmarkNames);

        // Typing right after the bookmark stays outside it.
        session.MoveCaret(Pos(0, 10), extend: false);
        session.InsertText("X");
        Assert.Equal("beta", BookmarkSupport.TextOf(session.Document, "Mark1"));

        // A collapsed selection bookmarks the word at the caret; a reused name moves the bookmark.
        session.MoveCaret(Pos(1, 2), extend: false);
        Assert.True(session.AddBookmark("Mark1"));
        Assert.Equal("Second", BookmarkSupport.TextOf(session.Document, "Mark1"));
        Assert.Single(BookmarkSupport.Names(session.Document));

        session.MoveCaret(Pos(0, 0), extend: false);
        Assert.True(session.GoToBookmark("Mark1"));
        Assert.Equal(Range(1, 0, 1, 6), session.Selection.Range);
        Assert.False(session.GoToBookmark("nope"));

        session.RemoveBookmark("Mark1");
        Assert.Empty(session.BookmarkNames);
        session.Undo();
        Assert.Equal(["Mark1"], session.BookmarkNames);
    }

    [Fact]
    public void Cross_references_insert_ref_and_pageref_fields_and_update_with_the_bookmark()
    {
        var session = new EditingSession(WithBlocks(Paragraph.FromText("Chapter One", DefaultStyleSheet.Heading1Id), Paragraph.FromText("See ")));
        string? hidden = session.EnsureParagraphBookmark(Body, BlockPath.Of(0));
        Assert.NotNull(hidden);
        Assert.StartsWith("_Ref", hidden, StringComparison.Ordinal);
        Assert.Empty(session.BookmarkNames);
        Assert.Equal(hidden, session.EnsureParagraphBookmark(Body, BlockPath.Of(0)));

        session.MoveCaret(Pos(1, 4), extend: false);
        session.InsertCrossReference(hidden!, pageNumber: false, asLink: true);
        session.InsertText(" on page ");
        session.InsertCrossReference(hidden!, pageNumber: true, asLink: false, pageText: "7");
        Paragraph paragraph = session.Document.Para(1);
        var text = (Field)paragraph.Inlines[1];
        Assert.Equal(FieldKind.Ref, text.Kind);
        Assert.Equal("Chapter One", text.CachedResult);
        Assert.Equal(hidden, BookmarkSupport.Target(text));
        Assert.Equal("#" + hidden, text.Properties.Link);
        var page = (Field)paragraph.Inlines[^1];
        Assert.Equal(FieldKind.PageRef, page.Kind);
        Assert.Equal("7", page.CachedResult);
        Assert.Null(page.Properties.Link);

        session.MoveCaret(Pos(0, 11), extend: false);
        session.InsertText(" Revised");
        session.UpdateReferenceFields(new Dictionary<string, string> { [hidden!] = "9" });
        paragraph = session.Document.Para(1);
        Assert.Equal("Chapter One Revised", ((Field)paragraph.Inlines[1]).CachedResult);
        Assert.Equal("9", ((Field)paragraph.Inlines[^1]).CachedResult);
        Document before = session.Document;
        session.UpdateReferenceFields(new Dictionary<string, string> { [hidden!] = "9" });
        Assert.Same(before, session.Document);
    }
}
