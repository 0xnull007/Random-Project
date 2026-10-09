using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>
/// Pure edit operations: each takes a <see cref="Document"/> and returns a new one plus the resulting
/// selection. Nothing here mutates; undo is a matter of keeping the previous <see cref="Document"/>.
/// </summary>
public static class DocumentEditor
{
    /// <summary>
    /// Inserts single-paragraph text at <paramref name="at"/>. When <paramref name="format"/> is null the
    /// text takes the formatting of the character before the caret (or the paragraph mark in an empty paragraph).
    /// </summary>
    public static EditResult InsertText(Document document, TextPosition at, string text, RunProperties? format = null, string? characterStyleId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return EditResult.NoOp(document, Selection.Caret(at));
        }

        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException("Text must not contain line separators; use InsertFragment.", nameof(text));
        }

        Paragraph paragraph = RequireParagraph(document, at);
        (string? styleId, RunProperties properties) = format is null
            ? paragraph.GetTypingFormatAt(at.Offset)
            : (characterStyleId, format);

        var run = new Run(text, properties, styleId);
        (ImmutableArray<Inline> head, ImmutableArray<Inline> tail) = InlineOps.Split(paragraph, at.Offset);
        Paragraph updated = paragraph.WithInlines(InlineOps.Concat(head, [run], tail));

        return new EditResult(
            ReplaceBlock(document, at.Story, at.Block, updated),
            Selection.Caret(at.WithOffset(at.Offset + text.Length)),
            ChangeSet.From(at.Story, at.Block.TopIndex));
    }

    /// <summary>Inserts a non-text inline (break or field) at <paramref name="at"/>, taking the typing format there unless the inline already carries formatting.</summary>
    public static EditResult InsertInline(Document document, TextPosition at, Inline inline)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inline);
        Paragraph paragraph = RequireParagraph(document, at);
        if (inline.Properties.IsEmpty && inline.StyleId is null)
        {
            (string? styleId, RunProperties properties) = paragraph.GetTypingFormatAt(at.Offset);
            inline = inline.WithProperties(properties).WithStyle(styleId);
        }

        (ImmutableArray<Inline> head, ImmutableArray<Inline> tail) = InlineOps.Split(paragraph, at.Offset);
        Paragraph updated = paragraph.WithInlines(InlineOps.Concat(head, [inline], tail));
        return new EditResult(
            ReplaceBlock(document, at.Story, at.Block, updated),
            Selection.Caret(at.WithOffset(at.Offset + inline.Length)),
            ChangeSet.From(at.Story, at.Block.TopIndex));
    }

    /// <summary>
    /// Deletes a range. Across paragraphs the survivors are joined into one paragraph that keeps the first
    /// paragraph's formatting, unless nothing of the first paragraph remains (then the last one's), as Word does.
    /// </summary>
    public static EditResult DeleteRange(Document document, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (range.IsEmpty)
        {
            return EditResult.NoOp(document, Selection.Caret(range.Start));
        }

        StoryId story = range.Story;
        ImmutableList<Block> blocks = document.GetStory(story);
        int firstIndex = range.Start.Block.TopIndex;
        int lastIndex = range.End.Block.TopIndex;
        Paragraph first = RequireParagraph(blocks, firstIndex);
        Paragraph last = RequireParagraph(blocks, lastIndex);
        ValidateOffset(first, range.Start.Offset);
        ValidateOffset(last, range.End.Offset);

        ImmutableArray<Inline> head = InlineOps.Slice(first, 0, range.Start.Offset);
        ImmutableArray<Inline> tail = InlineOps.Slice(last, range.End.Offset, last.Length);
        Paragraph template = firstIndex != lastIndex && head.IsEmpty ? last : first;
        Paragraph merged = template.WithInlines(InlineOps.Concat(head, tail));

        ImmutableList<Block> updated = blocks.RemoveRange(firstIndex, lastIndex - firstIndex + 1).Insert(firstIndex, merged);
        return new EditResult(
            document.WithStory(story, updated),
            Selection.Caret(range.Start),
            ChangeSet.From(story, firstIndex));
    }

    /// <summary>
    /// Splits a paragraph at <paramref name="at"/> (Enter). At the end of a paragraph the new paragraph takes
    /// the style's Next style; elsewhere both halves keep the paragraph's formatting.
    /// </summary>
    public static EditResult SplitParagraph(Document document, TextPosition at, StyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);
        Paragraph paragraph = RequireParagraph(document, at);
        ValidateOffset(paragraph, at.Offset);

        (string? _, RunProperties typingProperties) = paragraph.GetTypingFormatAt(at.Offset);
        Paragraph head;
        Paragraph tail;
        if (at.Offset == paragraph.Length)
        {
            head = paragraph;
            string? nextStyle = resolver.NextStyleAfter(paragraph.StyleId);
            bool styleChanges = !string.Equals(nextStyle, paragraph.StyleId, StringComparison.Ordinal);
            tail = styleChanges
                ? new Paragraph(ImmutableArray<Inline>.Empty, nextStyle)
                : new Paragraph(ImmutableArray<Inline>.Empty, paragraph.StyleId, paragraph.Properties, typingProperties);
        }
        else
        {
            (ImmutableArray<Inline> headInlines, ImmutableArray<Inline> tailInlines) = InlineOps.Split(paragraph, at.Offset);
            head = paragraph.WithInlines(headInlines).WithMarkProperties(typingProperties);
            tail = paragraph.WithInlines(tailInlines);
        }

        ImmutableList<Block> blocks = document.GetStory(at.Story);
        int index = at.Block.TopIndex;
        ImmutableList<Block> updated = blocks.SetItem(index, head).Insert(index + 1, tail);
        return new EditResult(
            document.WithStory(at.Story, updated),
            Selection.Caret(at.WithBlock(at.Block.Next(), 0)),
            ChangeSet.From(at.Story, index));
    }

    /// <summary>Merges direct character formatting into every inline in the range; paragraph marks inside the range are included.</summary>
    public static EditResult ApplyRunFormat(Document document, TextRange range, RunProperties delta)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(delta);
        if (range.IsEmpty || delta.IsEmpty)
        {
            return EditResult.NoOp(document, new Selection(range.Start, range.End));
        }

        return TransformParagraphs(document, range, (paragraph, start, end, includesMark) =>
        {
            ImmutableArray<Inline> inlines = InlineOps.Transform(paragraph, start, end, inline => inline.WithProperties(inline.Properties.Merge(delta)));
            Paragraph updated = paragraph.WithInlines(inlines);
            return includesMark ? updated.WithMarkProperties(paragraph.MarkProperties.Merge(delta)) : updated;
        });
    }

    /// <summary>
    /// Rewrites the selected text of each paragraph with <paramref name="transform"/> (case changes), keeping every
    /// run's formatting. The transform must return text of the same length; otherwise that paragraph is left alone.
    /// </summary>
    public static EditResult TransformText(Document document, TextRange range, Func<string, string> transform)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transform);
        if (range.IsEmpty)
        {
            return EditResult.NoOp(document, new Selection(range.Start, range.End));
        }

        return TransformParagraphs(document, range, (paragraph, start, end, _) =>
        {
            string flat = paragraph.FlatText;
            end = Math.Min(end, flat.Length);
            if (end <= start)
            {
                return paragraph;
            }

            string replacement = transform(flat.Substring(start, end - start));
            if (replacement.Length != end - start || flat.AsSpan(start, end - start).SequenceEqual(replacement))
            {
                return paragraph;
            }

            var inlines = ImmutableArray.CreateBuilder<Inline>(paragraph.Inlines.Length);
            foreach (InlineSpan span in paragraph.Spans())
            {
                if (span.Inline is Run run && span.End > start && span.Start < end)
                {
                    int from = Math.Max(start, span.Start);
                    int to = Math.Min(end, span.End);
                    char[] chars = run.Text.ToCharArray();
                    for (int i = from; i < to; i++)
                    {
                        chars[i - span.Start] = replacement[i - start];
                    }

                    inlines.Add(run.WithText(new string(chars)));
                }
                else
                {
                    inlines.Add(span.Inline);
                }
            }

            return paragraph.WithInlines(inlines.MoveToImmutable());
        });
    }

    /// <summary>Sets the character style of every inline in the range (null clears it).</summary>

    public static EditResult ApplyCharacterStyle(Document document, TextRange range, string? characterStyleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (range.IsEmpty)
        {
            return EditResult.NoOp(document, new Selection(range.Start, range.End));
        }

        return TransformParagraphs(document, range, (paragraph, start, end, _) =>
            paragraph.WithInlines(InlineOps.Transform(paragraph, start, end, inline => inline.WithStyle(characterStyleId))));
    }

    /// <summary>Formats the paragraph mark of the paragraph at <paramref name="at"/> (what a collapsed-selection toggle in an empty paragraph does).</summary>
    public static EditResult ApplyParagraphMarkFormat(Document document, TextPosition at, RunProperties delta)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(delta);
        Paragraph paragraph = RequireParagraph(document, at);
        Paragraph updated = paragraph.WithMarkProperties(paragraph.MarkProperties.Merge(delta));
        return new EditResult(ReplaceBlock(document, at.Story, at.Block, updated), Selection.Caret(at), ChangeSet.From(at.Story, at.Block.TopIndex));
    }

    /// <summary>Merges direct paragraph formatting into every paragraph the range touches.</summary>
    public static EditResult ApplyParagraphFormat(Document document, TextRange range, ParagraphProperties delta)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.IsEmpty)
        {
            return EditResult.NoOp(document, new Selection(range.Start, range.End));
        }

        return TransformParagraphs(document, range, (paragraph, _, _, _) => paragraph.WithProperties(paragraph.Properties.Merge(delta)), touchWholeParagraphs: true);
    }

    /// <summary>Applies a paragraph style to every paragraph the range touches, clearing direct paragraph formatting like Word.</summary>
    public static EditResult SetParagraphStyle(Document document, TextRange range, string? paragraphStyleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return TransformParagraphs(document, range, (paragraph, _, _, _) => paragraph.WithStyle(paragraphStyleId).WithProperties(ParagraphProperties.Empty), touchWholeParagraphs: true);
    }

    /// <summary>Copies the range into a fragment. Partial paragraphs keep their paragraph formatting.</summary>
    public static DocumentFragment ExtractFragment(Document document, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (range.IsEmpty)
        {
            return DocumentFragment.Empty;
        }

        ImmutableList<Block> blocks = document.GetStory(range.Story);
        int firstIndex = range.Start.Block.TopIndex;
        int lastIndex = range.End.Block.TopIndex;
        var paragraphs = ImmutableArray.CreateBuilder<Paragraph>();
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            int start = i == firstIndex ? range.Start.Offset : 0;
            int end = i == lastIndex ? range.End.Offset : paragraph.Length;
            paragraphs.Add(paragraph.WithInlines(InlineOps.Slice(paragraph, start, end)));
        }

        ImmutableArray<Paragraph> extracted = paragraphs.ToImmutable();
        return new DocumentFragment(extracted, CollectImages(document, extracted));
    }

    private static ImmutableDictionary<string, ImageData>? CollectImages(Document document, ImmutableArray<Paragraph> paragraphs)
    {
        ImmutableDictionary<string, ImageData>.Builder? images = null;
        foreach (Paragraph paragraph in paragraphs)
        {
            foreach (Inline inline in paragraph.Inlines)
            {
                if (inline is InlineImage image && document.Images.Get(image.ImageId) is { } data)
                {
                    images ??= ImmutableDictionary.CreateBuilder<string, ImageData>(StringComparer.Ordinal);
                    images[image.ImageId] = data;
                }
            }
        }

        return images?.ToImmutable();
    }

    /// <summary>Adds the fragment's pictures to the document store (ids already present are kept as they are).</summary>
    private static Document WithFragmentImages(Document document, DocumentFragment fragment)
    {
        ImageStore store = document.Images;
        foreach ((string id, ImageData data) in fragment.Images)
        {
            if (store.Get(id) is null)
            {
                store = store.With(id, data);
            }
        }

        return ReferenceEquals(store, document.Images) ? document : document.WithImages(store);
    }


    /// <summary>
    /// Inserts a fragment at <paramref name="at"/>. The first fragment paragraph joins the target paragraph
    /// and the last one joins the remainder of the target, both keeping the target's paragraph formatting.
    /// </summary>
    public static EditResult InsertFragment(Document document, TextPosition at, DocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Paragraphs.IsEmpty)
        {
            return EditResult.NoOp(document, Selection.Caret(at));
        }

        document = WithFragmentImages(document, fragment);
        Paragraph target = RequireParagraph(document, at);
        ValidateOffset(target, at.Offset);
        (ImmutableArray<Inline> head, ImmutableArray<Inline> tail) = InlineOps.Split(target, at.Offset);
        ImmutableList<Block> blocks = document.GetStory(at.Story);
        int index = at.Block.TopIndex;

        if (fragment.IsSingleParagraph)

        {
            Paragraph only = fragment.Paragraphs[0];
            Paragraph updated = target.WithInlines(InlineOps.Concat(head, only.Inlines, tail));
            return new EditResult(
                document.WithStory(at.Story, blocks.SetItem(index, updated)),
                Selection.Caret(at.WithOffset(at.Offset + only.Length)),
                ChangeSet.From(at.Story, index));
        }

        var inserted = new List<Block>(fragment.Paragraphs.Length);
        Paragraph firstFragment = fragment.Paragraphs[0];
        inserted.Add(target.WithInlines(InlineOps.Concat(head, firstFragment.Inlines)));
        for (int i = 1; i < fragment.Paragraphs.Length - 1; i++)
        {
            inserted.Add(fragment.Paragraphs[i]);
        }

        Paragraph lastFragment = fragment.Paragraphs[^1];
        inserted.Add(target.WithInlines(InlineOps.Concat(lastFragment.Inlines, tail)));

        ImmutableList<Block> updatedBlocks = blocks.RemoveAt(index).InsertRange(index, inserted);
        BlockPath caretBlock = BlockPath.Of(index + inserted.Count - 1);
        return new EditResult(
            document.WithStory(at.Story, updatedBlocks),
            Selection.Caret(at.WithBlock(caretBlock, lastFragment.Length)),
            ChangeSet.From(at.Story, index));
    }

    /// <summary>Replaces the paragraph at <paramref name="path"/> (helper for callers that build paragraphs themselves).</summary>
    public static Document ReplaceBlock(Document document, StoryId story, BlockPath path, Block block)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        ImmutableList<Block> blocks = document.GetStory(story);
        return document.WithStory(story, blocks.SetItem(path.TopIndex, block));
    }

    /// <summary>
    /// Inserts a section break at <paramref name="at"/>: paragraphs from the caret's paragraph onward move
    /// into a new section with the same page setup. The caret's paragraph is split first when mid-paragraph.
    /// </summary>
    public static EditResult InsertSectionBreak(Document document, TextPosition at, SectionStart start, StyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!at.Story.IsBody)
        {
            throw new InvalidOperationException("Section breaks can only be inserted in the body.");
        }

        Document working = document;
        Paragraph paragraph = RequireParagraph(working, at);
        int splitIndex = at.Block.TopIndex;
        if (at.Offset > 0 && at.Offset < paragraph.Length || (at.Offset == paragraph.Length && paragraph.Length > 0))
        {
            working = SplitParagraph(working, at, resolver).Document;
            splitIndex++;
        }

        Section section = working.Sections[at.Story.SectionIndex];
        ImmutableList<Block> body = section.Body;
        ImmutableList<Block> headBlocks = body.GetRange(0, splitIndex);
        ImmutableList<Block> tailBlocks = body.GetRange(splitIndex, body.Count - splitIndex);
        if (headBlocks.Count == 0)
        {
            headBlocks = ImmutableList.Create<Block>(Paragraph.Empty(paragraph.StyleId));
        }

        if (tailBlocks.Count == 0)
        {
            tailBlocks = ImmutableList.Create<Block>(Paragraph.Empty(resolver.NextStyleAfter(paragraph.StyleId)));
        }

        // The original section properties stay with the *tail* (WordprocessingML stores sectPr at the end of a
        // section), so the new first section copies them and the new break type applies to the second.
        var tailSection = new Section(section.Properties with { Start = start }, tailBlocks, HeaderFooterSet.Empty, HeaderFooterSet.Empty);
        Section headSection = section.WithBody(headBlocks);
        ImmutableList<Section> sections = working.Sections.SetItem(at.Story.SectionIndex, headSection).Insert(at.Story.SectionIndex + 1, tailSection);

        return new EditResult(
            working.WithSections(sections),
            Selection.Caret(TextPosition.StartOf(StoryId.Body(at.Story.SectionIndex + 1))),
            ChangeSet.Structural());
    }

    public static EditResult SetSectionProperties(Document document, int sectionIndex, SectionProperties properties, Selection selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(properties);
        Section section = document.Sections[sectionIndex];
        if (section.Properties == properties)
        {
            return EditResult.NoOp(document, selection);
        }

        return new EditResult(document.WithSection(sectionIndex, section.WithProperties(properties)), selection, ChangeSet.Structural());
    }

    private delegate Paragraph ParagraphTransform(Paragraph paragraph, int start, int end, bool includesMark);

    private static EditResult TransformParagraphs(Document document, TextRange range, ParagraphTransform transform, bool touchWholeParagraphs = false)
    {
        StoryId story = range.Story;
        ImmutableList<Block> blocks = document.GetStory(story);
        int firstIndex = range.Start.Block.TopIndex;
        int lastIndex = range.End.Block.TopIndex;
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        bool changed = false;
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            int start = i == firstIndex ? range.Start.Offset : 0;
            int end = i == lastIndex ? range.End.Offset : paragraph.Length;
            bool includesMark = i < lastIndex;
            if (!touchWholeParagraphs && start == end && !includesMark)
            {
                continue; // range ends exactly at the start of this paragraph
            }

            Paragraph updated = transform(paragraph, start, end, includesMark);
            if (!ReferenceEquals(updated, paragraph))
            {
                builder[i] = updated;
                changed = true;
            }
        }

        var selection = new Selection(range.Start, range.End);
        if (!changed)
        {
            return EditResult.NoOp(document, selection);
        }

        return new EditResult(document.WithStory(story, builder.ToImmutable()), selection, ChangeSet.From(story, firstIndex));
    }

    private static Paragraph RequireParagraph(Document document, TextPosition at)
    {
        Block? block = document.TryGetBlock(at.Story, at.Block)
            ?? throw new ArgumentException($"No block at {at}.", nameof(at));
        return block as Paragraph ?? throw new InvalidOperationException($"Block at {at} is not an editable paragraph.");
    }

    private static Paragraph RequireParagraph(ImmutableList<Block> blocks, int index)
    {
        if (index < 0 || index >= blocks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return blocks[index] as Paragraph ?? throw new InvalidOperationException($"Block {index} is not an editable paragraph.");
    }

    private static void ValidateOffset(Paragraph paragraph, int offset)
    {
        if (offset < 0 || offset > paragraph.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), $"Offset {offset} is outside paragraph of length {paragraph.Length}.");
        }
    }
}
