using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

/// <summary>Word's Display for Review: the same tracked changes resolve differently per view.</summary>
public class RevisionViewTests
{
    private static readonly Revision Inserted = Revision.Insertion("Ada", new DateTime(2026, 10, 11, 9, 0, 0));
    private static readonly Revision Deleted = Revision.Deletion("Bob", new DateTime(2026, 10, 11, 9, 0, 0));

    private static Paragraph Changed() => new(
    [
        new Run("plain "),
        new Run("added", new RunProperties { Revision = Inserted }),
        new Run("removed", new RunProperties { Revision = Deleted }),
    ]);

    private static (ResolvedRunProperties Plain, ResolvedRunProperties Added, ResolvedRunProperties Removed) Resolve(RevisionView view)
    {
        Paragraph paragraph = Changed();
        var resolver = new StyleResolver(DefaultStyleSheet.Create(), revisionView: view);
        return (resolver.ResolveRun(paragraph, paragraph.Inlines[0]), resolver.ResolveRun(paragraph, paragraph.Inlines[1]), resolver.ResolveRun(paragraph, paragraph.Inlines[2]));
    }

    [Fact]
    public void All_markup_underlines_insertions_and_strikes_deletions_in_author_colours()
    {
        (ResolvedRunProperties plain, ResolvedRunProperties added, ResolvedRunProperties removed) = Resolve(RevisionView.AllMarkup);
        Assert.Equal(UnderlineStyle.Single, added.Underline);
        Assert.Equal(Revision.ColorFor("Ada"), added.Color);
        Assert.True(removed.Strikethrough);
        Assert.Equal(Revision.ColorFor("Bob"), removed.Color);
        Assert.False(added.Hidden);
        Assert.False(removed.Hidden);
        Assert.Equal(UnderlineStyle.None, plain.Underline);
    }

    [Theory]
    [InlineData(RevisionView.SimpleMarkup)]
    [InlineData(RevisionView.NoMarkup)]
    public void Final_text_views_hide_deletions_and_show_insertions_as_plain_text(RevisionView view)
    {
        (ResolvedRunProperties plain, ResolvedRunProperties added, ResolvedRunProperties removed) = Resolve(view);
        Assert.True(removed.Hidden);
        Assert.False(added.Hidden);
        Assert.Equal(UnderlineStyle.None, added.Underline);
        Assert.Equal(plain.Color, added.Color);
        Assert.NotNull(added.Revision); // the change is still known, for change bars and Accept/Reject
    }

    [Fact]
    public void Original_view_hides_insertions_and_shows_deletions_as_plain_text()
    {
        (ResolvedRunProperties plain, ResolvedRunProperties added, ResolvedRunProperties removed) = Resolve(RevisionView.Original);
        Assert.True(added.Hidden);
        Assert.False(removed.Hidden);
        Assert.False(removed.Strikethrough);
        Assert.Equal(plain.Color, removed.Color);
    }

    [Fact]
    public void Switching_the_view_swaps_the_resolver_and_announces_a_structural_change_without_touching_the_document()
    {
        var session = new EditingSession(WithParagraphs("abc"));
        Document before = session.Document;
        ChangeSet? announced = null;
        session.DocumentChanged += (_, e) => announced = e.Change;

        session.SetRevisionView(RevisionView.NoMarkup);
        Assert.Equal(RevisionView.NoMarkup, session.RevisionView);
        Assert.Equal(RevisionView.NoMarkup, session.Resolver.RevisionView);
        Assert.True(announced!.Value.StructureChanged);
        Assert.Same(before, session.Document);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);

        // Edits that replace the style sheet keep the view.
        Style normal = session.Document.Styles.Get(session.Document.Styles.DefaultParagraphStyleId)!;
        session.ModifyStyle(normal with { RunProperties = normal.RunProperties with { Bold = true } });
        Assert.Equal(RevisionView.NoMarkup, session.Resolver.RevisionView);

        announced = null;
        session.SetRevisionView(RevisionView.NoMarkup);
        Assert.Null(announced);
    }
}
