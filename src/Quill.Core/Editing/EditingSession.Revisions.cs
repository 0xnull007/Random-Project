using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>Track changes: insertions and deletions are recorded on runs, shown as markup, and accepted or rejected later.</summary>
public sealed partial class EditingSession
{
    /// <summary>Name recorded on tracked changes.</summary>
    public string Author { get; set; } = Environment.UserName;

    /// <summary>Clock for revision dates (tests inject a fixed one).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>True while edits are recorded as tracked changes; stored in the document like Word's w:trackRevisions.</summary>
    public bool TrackChanges => Document.Settings.TrackChanges;

    /// <summary>Switches tracking on or off. Not an undo step, like Word, but it does mark the document changed.</summary>
    public void SetTrackChanges(bool on)
    {
        if (on == TrackChanges)
        {
            return;
        }

        ApplyState(Document.WithSettings(Document.Settings with { TrackChanges = on }), Selection, ChangeSet.Structural());
    }

    public bool HasTrackedChanges => TrackedChanges().Count > 0;

    public int TrackedChangeCount => TrackedChanges().Count;

    /// <summary>Every tracked change in document order: a run of contiguous inlines with the same kind of mark.</summary>
    public IReadOnlyList<TextRange> TrackedChanges()
    {
        var ranges = new List<TextRange>();
        foreach ((StoryId story, BlockPath path, Paragraph paragraph) in DocumentEditor.AllParagraphs(Document))
        {
            foreach ((int start, int end) in RevisionSpans(paragraph))
            {
                ranges.Add(new TextRange(new TextPosition(story, path, start), new TextPosition(story, path, end)));
            }
        }

        return ranges;
    }

    /// <summary>The tracked change under <paramref name="position"/> (contiguous inlines of one kind), or null.</summary>
    public TextRange? TrackedChangeAt(TextPosition position)
    {
        Paragraph paragraph = Document.GetParagraph(position);
        foreach ((int start, int end) in RevisionSpans(paragraph))
        {
            if (position.Offset >= start && position.Offset <= end)
            {
                return new TextRange(position.WithOffset(start), position.WithOffset(end));
            }
        }

        return null;
    }

    /// <summary>Selects the next tracked change after the selection (wrapping to the first); false when there are none.</summary>
    public bool SelectNextChange() => SelectChange(forward: true);

    public bool SelectPreviousChange() => SelectChange(forward: false);

    /// <summary>Accepts the selected change (or the one at the caret), then selects the next one.</summary>
    public void AcceptChange() => ResolveAtSelection(accept: true);

    public void RejectChange() => ResolveAtSelection(accept: false);

    /// <summary>Keeps every insertion and removes every deletion.</summary>
    public void AcceptAllChanges() => ResolveAll(accept: true);

    /// <summary>Removes every insertion and restores every deletion.</summary>
    public void RejectAllChanges() => ResolveAll(accept: false);

    // ----- used by the editing commands -----

    /// <summary>
    /// Typing never inherits a neighbour's revision mark (while tracking it carries ours) and stays outside a user's
    /// bookmark, but the hidden paragraph bookmarks behind cross-references grow with their paragraph, like Word's.
    /// </summary>
    private RunProperties WithTypingMarks(RunProperties properties) =>
        properties with
        {
            Revision = TrackChanges ? Revision.Insertion(Author, Now()) : null,
            Bookmark = properties.Bookmark is { } bookmark && BookmarkSupport.IsHidden(bookmark) ? bookmark : null,
        };

    /// <summary>A pasted fragment while tracking: its deleted text is left out and the rest is marked inserted.</summary>
    private DocumentFragment WithInsertionMarks(DocumentFragment fragment)
    {
        if (!TrackChanges)
        {
            return fragment;
        }

        Revision mark = Revision.Insertion(Author, Now());
        Paragraph Mark(Paragraph p) => p.WithInlines(InlineOps.Normalize(
            p.Inlines.Where(i => i.Properties.Revision is not { IsDeletion: true }).Select(i => i.WithProperties(i.Properties with { Revision = mark }))));
        ImmutableList<Block> marked = BlockWalker.MapParagraphs(ImmutableList.CreateRange(fragment.Blocks), Mark);
        return new DocumentFragment(marked.ToImmutableArray(), fragment.Images);

    }

    /// <summary>
    /// Deletes a range, or while tracking marks it deleted: our own fresh insertions simply disappear, text already
    /// marked deleted keeps its mark, and a lone paragraph mark is still joined (paragraph marks are not tracked).
    /// </summary>
    private EditResult DeleteOrMark(Document document, TextRange range, bool caretAfter)
    {
        if (!TrackChanges || range.IsEmpty || IsParagraphMarkOnly(document, range))
        {
            return DocumentEditor.DeleteRange(document, range);
        }

        return MarkDeleted(document, range, caretAfter, out _);
    }

    private static bool IsParagraphMarkOnly(Document document, TextRange range) =>
        range.Start.Block != range.End.Block
        && range.End.Offset == 0
        && range.Start.Offset == document.GetParagraph(range.Start).Length
        && range.Start.Block.SameContainer(range.End.Block)
        && range.End.Block.Last - range.Start.Block.Last == 1;

    private EditResult MarkDeleted(Document document, TextRange range, bool caretAfter, out int droppedInLastParagraph)
    {
        droppedInLastParagraph = 0;
        if (!range.Start.Block.SameContainer(range.End.Block))
        {
            return EditResult.NoOp(document, Selection.Caret(range.Start));
        }

        Revision mark = Revision.Deletion(Author, Now());
        ImmutableList<Block> blocks = document.GetContainer(range.Story, range.Start.Block);
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        int endOffset = range.End.Offset;
        foreach (ParagraphSpan span in TextRanges.Paragraphs(blocks, range))
        {
            Paragraph paragraph = span.Paragraph;
            int end = Math.Min(span.End, paragraph.Length);
            if (span.Start >= end)
            {
                continue;
            }

            var marked = ImmutableArray.CreateBuilder<Inline>();
            int dropped = 0;
            foreach (Inline inline in InlineOps.Slice(paragraph, span.Start, end))
            {
                Revision? existing = inline.Properties.Revision;
                if (existing is { IsInsertion: true } && string.Equals(existing.Author, Author, StringComparison.Ordinal))
                {
                    dropped += inline.Length; // deleting our own insertion: it just goes away
                    continue;
                }

                marked.Add(existing is { IsDeletion: true } ? inline : inline.WithProperties(inline.Properties with { Revision = mark }));
            }

            builder[span.Index] = paragraph.WithInlines(InlineOps.Concat(
                InlineOps.Slice(paragraph, 0, span.Start),
                marked.ToImmutable(),
                InlineOps.Slice(paragraph, end, paragraph.Length)));
            if (span.Index == range.End.Block.Last)
            {
                endOffset = range.End.Offset - dropped;
                droppedInLastParagraph = dropped;
            }
        }

        TextPosition caret = caretAfter ? range.End.WithOffset(endOffset) : range.Start;
        return new EditResult(
            document.WithContainer(range.Story, range.Start.Block, builder.ToImmutable()),
            Selection.Caret(caret),
            ChangeSet.From(range.Story, range.Start.Block.TopIndex));
    }

    // ----- accept / reject -----

    private void ResolveAtSelection(bool accept)
    {
        TextRange? target = Selection.IsCollapsed ? TrackedChangeAt(Selection.Active) : Selection.Range;
        if (target is not { } range || !range.Start.Block.SameContainer(range.End.Block))
        {
            return;
        }

        ImmutableList<Block> blocks = Document.GetContainer(range.Story, range.Start.Block);
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        bool changed = false;
        foreach (ParagraphSpan span in TextRanges.Paragraphs(blocks, range))
        {
            Paragraph resolved = Resolve(span.Paragraph, span.Start, Math.Min(span.End, span.Paragraph.Length), accept);
            if (!ReferenceEquals(resolved, span.Paragraph))
            {
                builder[span.Index] = resolved;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Document document = Document.WithContainer(range.Story, range.Start.Block, builder.ToImmutable());
        Commit(new EditResult(document, Selection.Caret(range.Start), ChangeSet.From(range.Story, range.Start.Block.TopIndex)), EditKind.Other, startsNewGroup: true);
        SelectChange(forward: true);
    }

    private void ResolveAll(bool accept)
    {
        Document document = DocumentEditor.MapParagraphs(Document, p => Resolve(p, 0, p.Length, accept));
        if (ReferenceEquals(document, Document))
        {
            return;
        }

        TextPosition caret = Selection.Active;
        if (!document.IsValid(caret))
        {
            caret = document.TryGetBlock(caret.Story, caret.Block) is Paragraph paragraph
                ? caret.WithOffset(Math.Min(caret.Offset, paragraph.Length))
                : TextNavigation.StoryStart(document, caret.Story);
        }

        Commit(new EditResult(document, Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Resolves the marks inside [start, end): accepting drops deletions and keeps insertions, rejecting the reverse.</summary>
    private static Paragraph Resolve(Paragraph paragraph, int start, int end, bool accept)
    {
        if (start >= end || !paragraph.Inlines.Any(i => i.Properties.Revision is not null))
        {
            return paragraph;
        }

        var kept = ImmutableArray.CreateBuilder<Inline>();
        bool changed = false;
        foreach (Inline inline in InlineOps.Slice(paragraph, start, end))
        {
            if (inline.Properties.Revision is not { } revision)
            {
                kept.Add(inline);
                continue;
            }

            changed = true;
            bool remove = accept ? revision.IsDeletion : revision.IsInsertion;
            if (!remove)
            {
                kept.Add(inline.WithProperties(inline.Properties with { Revision = null }));
            }
        }

        return changed
            ? paragraph.WithInlines(InlineOps.Concat(InlineOps.Slice(paragraph, 0, start), kept.ToImmutable(), InlineOps.Slice(paragraph, end, paragraph.Length)))
            : paragraph;
    }

    private bool SelectChange(bool forward)
    {
        IReadOnlyList<TextRange> changes = TrackedChanges();
        if (changes.Count == 0)
        {
            return false;
        }

        Dictionary<StoryId, int> order = StoryOrder(Document);
        int current = order.GetValueOrDefault(Selection.Story);
        TextRange selection = Selection.Range;
        TextRange? target = null;
        if (forward)
        {
            foreach (TextRange c in changes)
            {
                if (order[c.Story] > current || (order[c.Story] == current && c.Start >= selection.End && c != selection))
                {
                    target = c;
                    break;
                }
            }
        }
        else
        {
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                TextRange c = changes[i];
                if (order[c.Story] < current || (order[c.Story] == current && c.End <= selection.Start && c != selection))
                {
                    target = c;
                    break;
                }
            }
        }

        target ??= forward ? changes[0] : changes[^1];


        MoveCaret(target.Value.Start, extend: false);
        MoveCaret(target.Value.End, extend: true);
        return true;
    }

    // ----- walking -----

    /// <summary>Runs of contiguous inlines carrying the same kind of mark, as [start, end) offsets.</summary>
    private static IEnumerable<(int Start, int End)> RevisionSpans(Paragraph paragraph)
    {
        int? start = null;
        int end = 0;
        RevisionKind kind = default;
        foreach (InlineSpan span in paragraph.Spans())
        {
            Revision? revision = span.Inline.Properties.Revision;
            if (revision is null || (start is not null && revision.Kind != kind))
            {
                if (start is { } s)
                {
                    yield return (s, end);
                    start = null;
                }

                if (revision is null)
                {
                    continue;
                }
            }

            start ??= span.Start;
            kind = revision.Kind;
            end = span.End;
        }

        if (start is { } last)
        {
            yield return (last, end);
        }
    }

    private static Dictionary<StoryId, int> StoryOrder(Document document)

    {
        var order = new Dictionary<StoryId, int>();
        foreach ((StoryId story, _) in DocumentEditor.AllStories(document))
        {
            order.TryAdd(story, order.Count);
        }

        return order;
    }
}
