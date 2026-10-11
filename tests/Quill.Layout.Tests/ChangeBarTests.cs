using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

/// <summary>Change bars beside changed lines, and hidden deletions in the final-text views. Column: 100 DIPs wide at x=20, y=20; lines 20 tall.</summary>
public class ChangeBarTests
{
    private static readonly Revision Inserted = Revision.Insertion("Ada", new DateTime(2026, 10, 11, 9, 0, 0));
    private static readonly Revision Deleted = Revision.Deletion("Bob", new DateTime(2026, 10, 11, 9, 0, 0));

    private static SectionProperties Page() => new()
    {
        PageWidth = Twips.FromDips(140),
        PageHeight = Twips.FromDips(400),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    private static LayoutDocument Layout(RevisionView view, params Block[] blocks)
    {
        var document = new Document(ImmutableList.Create(new Section(Page(), blocks.ToImmutableList())), StyleSheet.Empty);
        return new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles, revisionView: view));
    }

    [Fact]
    public void Lines_containing_a_tracked_change_get_one_merged_bar_in_the_left_margin()
    {
        // Line 0: "aaa bbb " (plain), line 1: "ccc " + inserted "ddd ", line 2: inserted "eee " + "fff", line 3: "ggg" plain.
        var paragraph = new Paragraph([new Run("aaa bbb ccc "), new Run("ddd eee ", new RunProperties { Revision = Inserted }), new Run("fff ggg")]);
        LayoutDocument layout = Layout(RevisionView.AllMarkup, paragraph, Paragraph.FromText("plain"));
        PageLayout page = layout.Pages[0];
        ParagraphFragment fragment = page.Body.OfType<ParagraphFragment>().First();
        Assert.Equal(4, fragment.Layout.LineCount);

        ChangeBar bar = Assert.Single(page.ChangeBars);
        Assert.Equal(page.BodyArea.Left - PageLayout.ChangeBarOffset, bar.X);
        Assert.Equal(fragment.LineTop(1), bar.Top);
        Assert.Equal(fragment.LineBottom(2), bar.Bottom);
    }

    [Fact]
    public void Simple_markup_keeps_the_bars_but_hides_deleted_text()
    {
        var paragraph = new Paragraph([new Run("aaa "), new Run("bbb ", new RunProperties { Revision = Deleted }), new Run("ccc")]);
        LayoutDocument layout = Layout(RevisionView.SimpleMarkup, paragraph);
        PageLayout page = layout.Pages[0];
        ParagraphFragment fragment = page.Body.OfType<ParagraphFragment>().Single();

        Assert.Single(page.ChangeBars);
        Assert.Equal(RunKind.Hidden, fragment.Layout.Input.Runs[1].Kind);
        Assert.Equal(40, fragment.Layout.Lines[0].GetCaretX(8)); // "aaa " then the hidden "bbb " takes no width
    }

    [Theory]
    [InlineData(RevisionView.NoMarkup)]
    [InlineData(RevisionView.Original)]
    public void No_markup_and_original_show_no_bars(RevisionView view)
    {
        var paragraph = new Paragraph([new Run("aaa "), new Run("bbb ", new RunProperties { Revision = Inserted }), new Run("ccc", new RunProperties { Revision = Deleted })]);
        LayoutDocument layout = Layout(view, paragraph);
        Assert.Empty(layout.Pages[0].ChangeBars);
        ParagraphFragment fragment = layout.Pages[0].Body.OfType<ParagraphFragment>().Single();
        Assert.Equal(view == RevisionView.Original ? RunKind.Hidden : RunKind.Text, fragment.Layout.Input.Runs[1].Kind);
        Assert.Equal(view == RevisionView.Original ? RunKind.Text : RunKind.Hidden, fragment.Layout.Input.Runs[2].Kind);
    }
}
