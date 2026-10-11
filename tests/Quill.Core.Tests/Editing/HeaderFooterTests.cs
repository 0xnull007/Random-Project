using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Xunit;

namespace Quill.Core.Tests.Editing;

public class HeaderFooterTests
{
    private static Document TwoSections()
    {
        var headers = new HeaderFooterSet(ImmutableList.Create<Block>(Paragraph.FromText("Shared header")), null, null);
        var first = new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("one")), headers);
        var second = new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("two")));
        return new Document(ImmutableList.Create(first, second), DefaultStyleSheet.Create());
    }

    [Fact]
    public void Unlinking_copies_the_inherited_header_into_the_section_and_linking_drops_it_again()
    {
        var session = new EditingSession(TwoSections());
        var inherited = new StoryId(0, StoryKind.HeaderDefault);
        session.MoveCaret(new TextPosition(inherited, BlockPath.Of(0), 6), extend: false);
        Assert.True(session.IsInHeaderFooter);
        Assert.True(session.IsLinkedToPrevious(pageSection: 1));
        Assert.False(session.IsLinkedToPrevious(pageSection: 0));

        session.UnlinkHeaderFooterFromPrevious(1);
        var own = new StoryId(1, StoryKind.HeaderDefault);
        Assert.Equal("Shared header", ((Paragraph)session.Document.GetStory(own)[0]).FlatText);
        Assert.Equal(new TextPosition(own, BlockPath.Of(0), 6), session.Selection.Active);
        Assert.False(session.IsLinkedToPrevious(1));

        session.InsertText(" of two");
        Assert.Equal("Shared of two header", ((Paragraph)session.Document.GetStory(own)[0]).FlatText);
        Assert.Equal("Shared header", ((Paragraph)session.Document.GetStory(inherited)[0]).FlatText);

        session.LinkHeaderFooterToPrevious(1);
        Assert.Null(session.Document.TryGetStory(own));
        Assert.Equal(inherited, session.Selection.Story);
        Assert.True(session.IsLinkedToPrevious(1));

        session.Undo();
        Assert.NotNull(session.Document.TryGetStory(own));
    }

    [Fact]
    public void First_page_and_odd_even_switches_are_undoable()
    {
        var session = new EditingSession(TwoSections());
        session.SetDifferentFirstPage(1, true);
        Assert.True(session.HasDifferentFirstPage(1));
        Assert.False(session.HasDifferentFirstPage(0));
        session.SetDifferentOddEven(true);
        Assert.True(session.HasDifferentOddEven);
        session.Undo();
        Assert.False(session.HasDifferentOddEven);
        session.Undo();
        Assert.False(session.HasDifferentFirstPage(1));

        // Linking in the first section or from the body does nothing.
        Document before = session.Document;
        session.LinkHeaderFooterToPrevious(0);
        session.UnlinkHeaderFooterFromPrevious(1);
        Assert.Same(before, session.Document);
    }
}
