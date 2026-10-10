using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Model;

public class HeadingNumberingTests
{
    [Fact]
    public void Headings_get_outline_numbers_through_their_styles()
    {
        var blocks = new Block[]
        {
            new Paragraph([new Run("One")], DefaultStyleSheet.Heading1Id),
            new Paragraph([new Run("One A")], DefaultStyleSheet.Heading2Id),
            new Paragraph([new Run("One A i")], DefaultStyleSheet.Heading3Id),
            new Paragraph([new Run("body")]),
            new Paragraph([new Run("Two")], DefaultStyleSheet.Heading1Id),
        };
        var session = new EditingSession(WithBlocks(blocks), new UndoStack(time: new FakeTime()));
        Assert.False(session.HeadingsAreNumbered);
        session.NumberHeadings(true);
        Assert.True(session.HeadingsAreNumbered);

        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(session.Document.Sections[0].Body, session.Document.Lists, session.Resolver);
        Assert.Equal("1", markers[0].Text);
        Assert.Equal("1.1", markers[1].Text);
        Assert.Equal("1.1.1", markers[2].Text);
        Assert.False(markers.ContainsKey(3));
        Assert.Equal("2", markers[4].Text);

        session.NumberHeadings(false);
        Assert.False(session.HeadingsAreNumbered);
        Assert.Empty(ListNumbering.Compute(session.Document.Sections[0].Body, session.Document.Lists, session.Resolver));
    }
}
