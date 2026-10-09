using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

public class ListLayoutTests
{
    private static SectionProperties Page() => new()
    {
        PageWidth = Twips.FromDips(400),
        PageHeight = Twips.FromDips(400),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    private static (LayoutDocument Layout, Document Document) Run(ImmutableArray<ListLevel> levels, params (string Text, int Level)[] items)
    {
        (ListStore store, int id) = ListStore.Empty.AddList(levels);
        ImmutableList<Block> blocks = items
            .Select(i => (Block)new Paragraph([new Run(i.Text)], properties: new ParagraphProperties { List = new ListFormat(id, i.Level) }))
            .ToImmutableList();
        var document = new Document(ImmutableList.Create(new Section(Page(), blocks)), StyleSheet.Empty, lists: store);
        var paginator = new Paginator(new FakeLineFormatter(), new LayoutCache());
        return (paginator.Layout(document, new StyleResolver(document.Styles, document.Lists)), document);
    }

    [Fact]
    public void Numbered_paragraphs_get_markers_at_the_number_position_and_text_after_them()
    {
        (LayoutDocument layout, _) = Run(DefaultLists.NumberedLevels(), ("one", 0), ("two", 0), ("sub", 1));
        ParagraphFragment[] fragments = layout.Pages[0].Body.OfType<ParagraphFragment>().ToArray();
        Assert.Equal(3, fragments.Length);

        // Level 0: left indent 48, hanging 24 -> number position 24; "1." is 20 wide in the fake, plus a 6 gap -> text at 50.
        Assert.NotNull(fragments[0].Layout.MarkerLine);
        Assert.Equal(24, fragments[0].Layout.MarkerX);
        Assert.Equal(50, fragments[0].Layout.LineStarts[0]);
        Assert.Equal(2, fragments[0].Layout.MarkerLine!.Length);

        // Level 1 is lower-letter with a one-inch left indent: "a." at 96 - 24 = 72; it does not fit in the
        // hanging space with the fake's 10-DIP glyphs, so the text is pushed to 72 + 20 + 6 = 98.
        Assert.Equal(72, fragments[2].Layout.MarkerX);
        Assert.Equal(98, fragments[2].Layout.LineStarts[0]);
    }

    [Fact]
    public void Bullets_fit_in_the_hanging_space_and_continuation_lines_use_the_left_indent()
    {
        (LayoutDocument layout, _) = Run(DefaultLists.BulletLevels(), (string.Concat(Enumerable.Repeat("word ", 20)), 0));
        ParagraphFragment fragment = layout.Pages[0].Body.OfType<ParagraphFragment>().Single();
        Assert.True(fragment.Layout.LineCount > 1);
        Assert.Equal(24, fragment.Layout.MarkerX);
        Assert.Equal(48, fragment.Layout.LineStarts[0]);
        Assert.Equal(48, fragment.Layout.LineStarts[1]);
        Assert.Equal("•".Length, fragment.Layout.MarkerLine!.Length);
    }

    [Fact]
    public void Marker_changes_invalidate_the_cached_layout()
    {
        (ListStore store, int id) = ListStore.Empty.AddList(DefaultLists.NumberedLevels());
        var first = new Paragraph([new Run("a")], properties: new ParagraphProperties { List = new ListFormat(id, 0) });
        var second = new Paragraph([new Run("b")], properties: new ParagraphProperties { List = new ListFormat(id, 0) });
        var doc = new Document(ImmutableList.Create(new Section(Page(), ImmutableList.Create<Block>(first, second))), StyleSheet.Empty, lists: store);
        var cache = new LayoutCache();
        var paginator = new Paginator(new FakeLineFormatter(), cache);
        LayoutDocument before = paginator.Layout(doc, new StyleResolver(doc.Styles, doc.Lists));
        Assert.Equal(2, ((ParagraphFragment)before.Pages[0].Body[1]).Layout.MarkerLine!.Length); // "2."

        // Remove the first item: "b" becomes "1." and must not reuse the "2." layout.
        Document edited = doc.WithStory(Core.Text.StoryId.Body(0), ImmutableList.Create<Block>(second));
        LayoutDocument after = paginator.Layout(edited, new StyleResolver(edited.Styles, edited.Lists));
        Assert.NotSame(((ParagraphFragment)before.Pages[0].Body[1]).Layout, ((ParagraphFragment)after.Pages[0].Body[0]).Layout);
    }
}
