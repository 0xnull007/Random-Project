using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>Header &amp; Footer tab: first-page and odd/even variants, and linking a section's header to the previous one.</summary>
public sealed partial class EditingSession
{
    /// <summary>True while the caret is in a header or footer story.</summary>
    public bool IsInHeaderFooter => Selection.Story.IsHeader || Selection.Story.IsFooter;

    /// <summary>
    /// True when the header/footer shown on a page of <paramref name="pageSection"/> belongs to an earlier section
    /// (Word's "Link to Previous"): the caret's story comes from a section before the page's.
    /// </summary>
    public bool IsLinkedToPrevious(int pageSection) => IsInHeaderFooter && Selection.Story.SectionIndex < pageSection;

    public bool HasDifferentFirstPage(int section) => section >= 0 && section < Document.Sections.Count && Document.Sections[section].Properties.TitlePage;

    public bool HasDifferentOddEven => Document.Settings.EvenAndOddHeaders;

    /// <summary>Gives (or takes away) the section its own first-page header and footer; one undo step.</summary>
    public void SetDifferentFirstPage(int section, bool on)
    {
        if (section < 0 || section >= Document.Sections.Count || Document.Sections[section].Properties.TitlePage == on)
        {
            return;
        }

        SetSectionProperties(section, Document.Sections[section].Properties with { TitlePage = on });
    }

    /// <summary>Switches the document's even-page header and footer variants on or off; one undo step.</summary>
    public void SetDifferentOddEven(bool on)
    {
        if (Document.Settings.EvenAndOddHeaders == on)
        {
            return;
        }

        Commit(new EditResult(Document.WithSettings(Document.Settings with { EvenAndOddHeaders = on }), Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>
    /// "Link to Previous" off: <paramref name="pageSection"/> gets its own copy of the header or footer being edited,
    /// and the caret moves into that copy.
    /// </summary>
    public void UnlinkHeaderFooterFromPrevious(int pageSection)
    {
        if (!IsInHeaderFooter || pageSection <= Selection.Story.SectionIndex || pageSection >= Document.Sections.Count)
        {
            return;
        }

        var own = new StoryId(pageSection, Selection.Story.Kind);
        if (Document.TryGetStory(own) is not null)
        {
            return;
        }

        ImmutableList<Block> copy = Document.GetStory(Selection.Story);
        TextPosition caret = new(own, Selection.Active.Block, Selection.Active.Offset);
        Commit(new EditResult(Document.WithStory(own, copy), Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>
    /// "Link to Previous" on: <paramref name="pageSection"/> drops its own header or footer of the kind being edited
    /// and shows the previous section's again; the caret moves there. Nothing happens in the first section.
    /// </summary>
    public void LinkHeaderFooterToPrevious(int pageSection)
    {
        if (!IsInHeaderFooter || pageSection <= 0 || pageSection >= Document.Sections.Count || Selection.Story.SectionIndex != pageSection)
        {
            return;
        }

        StoryKind kind = Selection.Story.Kind;
        StoryId? inherited = null;
        for (int s = pageSection - 1; s >= 0; s--)
        {
            if (Document.TryGetStory(new StoryId(s, kind)) is not null)
            {
                inherited = new StoryId(s, kind);
                break;
            }
        }

        Section section = Document.Sections[pageSection];
        (bool isHeader, HeaderFooterVariant variant) = Slot(kind);
        Section updated = isHeader
            ? section.WithHeaders(section.Headers.With(variant, null))
            : section.WithFooters(section.Footers.With(variant, null));
        Document document = Document.WithSection(pageSection, updated);
        TextPosition caret = inherited is { } story
            ? TextNavigation.StoryStart(document, story)
            : TextNavigation.StoryStart(document, StoryId.Body(pageSection));
        Commit(new EditResult(document, Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    private static (bool IsHeader, HeaderFooterVariant Variant) Slot(StoryKind kind) => kind switch
    {
        StoryKind.HeaderDefault => (true, HeaderFooterVariant.Default),
        StoryKind.HeaderFirst => (true, HeaderFooterVariant.First),
        StoryKind.HeaderEven => (true, HeaderFooterVariant.Even),
        StoryKind.FooterDefault => (false, HeaderFooterVariant.Default),
        StoryKind.FooterFirst => (false, HeaderFooterVariant.First),
        StoryKind.FooterEven => (false, HeaderFooterVariant.Even),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
