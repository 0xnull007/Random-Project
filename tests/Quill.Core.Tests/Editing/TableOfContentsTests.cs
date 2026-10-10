using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TableOfContentsTests
{
    [Fact]
    public void Inserts_before_the_caret_paragraph_then_replaces_in_place()
    {
        var session = new EditingSession(WithParagraphs("Intro", "Body"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(1, 0), extend: false);
        session.InsertTableOfContents([new TocEntry("Intro", 0, "1"), new TocEntry("Details", 1, "2")], Twips.FromInches(6));

        IReadOnlyList<string> texts = session.Document.BodyTexts();
        Assert.Equal(["Intro", "Contents", "Intro\t1", "Details\t2", "Body"], texts);
        Assert.Equal(TableOfContents.HeadingStyleId, session.Document.Para(1).StyleId);
        Assert.Equal("TOC1", session.Document.Para(2).StyleId);
        Assert.Equal("TOC2", session.Document.Para(3).StyleId);
        Assert.Equal(TabAlignment.Right, session.Document.Para(2).Properties.Tabs![0].Alignment);
        Assert.Equal(TabLeader.Dot, session.Document.Para(2).Properties.Tabs![0].Leader);
        Assert.True(session.Document.Styles.Contains("TOC3"));
        Assert.Equal(9, session.ResolveStyleParagraph(TableOfContents.HeadingStyleId).OutlineLevel);
        Assert.Equal(Pos(4, 0), session.Selection.Active);

        session.MoveCaret(Pos(0, 0), extend: false);
        session.InsertTableOfContents([new TocEntry("Intro", 0, "3")], Twips.FromInches(6));
        Assert.Equal(["Intro", "Contents", "Intro\t3", "Body"], session.Document.BodyTexts());

        session.Undo();
        Assert.Equal(["Intro", "Contents", "Intro\t1", "Details\t2", "Body"], session.Document.BodyTexts());
    }

    [Fact]
    public void Empty_entry_list_explains_what_to_do()
    {
        var session = new EditingSession(WithParagraphs(""), new UndoStack(time: new FakeTime()));
        session.InsertTableOfContents([], Twips.FromInches(6));
        Assert.Contains("No headings", session.Document.Text(1), StringComparison.Ordinal);
        Assert.NotNull(TableOfContents.FindExisting(session.Document));
    }
}
