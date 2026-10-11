using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>Insert > Links: bookmarks and cross-references (REF and PAGEREF fields).</summary>
public sealed partial class EditingSession
{
    /// <summary>Bookmarks the user can see (Word hides its own _Ref, _Toc... names), in document order.</summary>
    public IReadOnlyList<string> BookmarkNames => BookmarkSupport.Names(Document).Where(n => !BookmarkSupport.IsHidden(n)).ToList();

    /// <summary>Marks the selection (or the word at the caret) as bookmark <paramref name="name"/>; false for an invalid name or no text.</summary>
    public bool AddBookmark(string name)
    {
        if (!BookmarkSupport.IsValidName(name))
        {
            return false;
        }

        if (Selection.IsCollapsed)
        {
            SelectWordAt(Selection.Active);
        }

        TextRange range = Selection.Range;
        if (range.IsEmpty || !range.Start.Block.SameContainer(range.End.Block))
        {
            return false;
        }

        Document document = BookmarkSupport.Find(Document, name) is not null ? ClearBookmark(Document, name) : Document;
        EditResult marked = DocumentEditor.ApplyRunFormat(document, range, new RunProperties { Bookmark = name });
        Commit(new EditResult(marked.Document, Selection, marked.Change), EditKind.Formatting, startsNewGroup: true);
        return true;
    }

    public void RemoveBookmark(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Document document = ClearBookmark(Document, name);
        if (!ReferenceEquals(document, Document))
        {
            Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>Selects the bookmarked text; false when the name is unknown.</summary>
    public bool GoToBookmark(string name)
    {
        if (BookmarkSupport.Find(Document, name) is not { } range)
        {
            return false;
        }

        MoveCaret(range.Start, extend: false);
        MoveCaret(range.End, extend: true);
        return true;
    }

    /// <summary>
    /// The bookmark covering a whole paragraph (a heading or caption to cross-reference), created as a hidden
    /// "_Ref..." bookmark when the paragraph has none; null for an empty paragraph.
    /// </summary>
    public string? EnsureParagraphBookmark(StoryId story, BlockPath path)
    {
        if (Document.TryGetBlock(story, path) is not Paragraph paragraph || paragraph.Length == 0)
        {
            return null;
        }

        string? existing = paragraph.Inlines[0].Properties.Bookmark;
        if (existing is not null && paragraph.Inlines.All(i => string.Equals(i.Properties.Bookmark, existing, StringComparison.OrdinalIgnoreCase)))
        {
            return existing;
        }

        string name = "_Ref" + (Math.Abs(string.GetHashCode(paragraph.FlatText + path, StringComparison.Ordinal)) % 1_000_000_000).ToString(CultureInfo.InvariantCulture);
        for (int n = 1; BookmarkSupport.Find(Document, name) is not null; n++)
        {
            name = name + n.ToString(CultureInfo.InvariantCulture);
        }

        var range = new TextRange(new TextPosition(story, path, 0), new TextPosition(story, path, paragraph.Length));
        EditResult marked = DocumentEditor.ApplyRunFormat(Document, range, new RunProperties { Bookmark = name });
        Commit(new EditResult(marked.Document, Selection, marked.Change), EditKind.Formatting, startsNewGroup: true);
        return name;
    }

    /// <summary>
    /// Inserts a cross-reference at the caret: a REF field showing the bookmark's text or a PAGEREF field showing its
    /// page (<paramref name="pageText"/>, what the layout reports now); optionally as a link to the bookmark.
    /// </summary>
    public void InsertCrossReference(string bookmark, bool pageNumber, bool asLink, string? pageText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmark);
        string text = pageNumber ? pageText ?? "1" : BookmarkSupport.TextOf(Document, bookmark) ?? bookmark;
        string instruction = (pageNumber ? "PAGEREF " : "REF ") + bookmark + " \\h";
        (string? styleId, RunProperties properties) = TypingFormat(Document, Selection);
        properties = WithTypingMarks(properties) with { Link = asLink ? "#" + bookmark : null };
        var field = new Field(pageNumber ? FieldKind.PageRef : FieldKind.Ref, instruction, text, properties, styleId);
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult inserted = DocumentEditor.InsertInline(working, at, field, inheritFormat: false);
        Commit(WithDeletion(inserted, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>
    /// Word's F9: REF fields take their bookmark's current text and PAGEREF fields the page numbers in
    /// <paramref name="pageNumbers"/> (bookmark name to page text, from the layout). One undo step when anything changed.
    /// </summary>
    public void UpdateReferenceFields(IReadOnlyDictionary<string, string> pageNumbers)
    {
        ArgumentNullException.ThrowIfNull(pageNumbers);
        Document before = Document;
        Document document = DocumentEditor.MapParagraphs(before, paragraph =>
        {
            if (!paragraph.Inlines.Any(i => i is Field { Kind: FieldKind.Ref or FieldKind.PageRef or FieldKind.Date or FieldKind.Time or FieldKind.DocProperty }))
            {
                return paragraph;
            }

            bool changed = false;
            ImmutableArray<Inline> inlines = paragraph.Inlines.Select(inline =>
            {
                if (inline is not Field field)
                {
                    return inline;
                }

                string? fresh = field.Kind switch
                {
                    FieldKind.Date or FieldKind.Time or FieldKind.DocProperty => FieldSupport.Resolve(field, DateTime.Now, before.Metadata),
                    FieldKind.Ref when BookmarkSupport.Target(field) is { } target => BookmarkSupport.TextOf(before, target),
                    FieldKind.PageRef when BookmarkSupport.Target(field) is { } target && pageNumbers.TryGetValue(target, out string? page) => page,
                    _ => null,
                };
                if (fresh is null || fresh == field.CachedResult)
                {
                    return inline;
                }

                changed = true;
                return field.WithCachedResult(fresh);
            }).ToImmutableArray();
            return changed ? paragraph.WithInlines(inlines) : paragraph;
        });

        if (!ReferenceEquals(document, before))
        {
            Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        }
    }

    private static Document ClearBookmark(Document document, string name) =>
        DocumentEditor.MapParagraphs(document, paragraph => paragraph.Inlines.Any(i => string.Equals(i.Properties.Bookmark, name, StringComparison.OrdinalIgnoreCase))
            ? paragraph.WithInlines(InlineOps.Normalize(paragraph.Inlines.Select(i => string.Equals(i.Properties.Bookmark, name, StringComparison.OrdinalIgnoreCase) ? i.WithProperties(i.Properties with { Bookmark = null }) : i)))
            : paragraph);
}
