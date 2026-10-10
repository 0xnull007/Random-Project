using System.Collections.Immutable;
using Quill.Core.Model;

namespace Quill.Core.Text;

/// <summary>One paragraph touched by a range: the selected offsets and whether the paragraph mark counts as selected.</summary>
public readonly record struct ParagraphSpan(int Index, Paragraph Paragraph, int Start, int End, bool IncludesMark)
{
    /// <summary>True when nothing of the paragraph, not even its mark, is inside the range.</summary>
    public bool IsEmpty => Start == End && !IncludesMark;
}

/// <summary>How a range maps onto paragraphs, following Word's conventions.</summary>
public static class TextRanges
{
    /// <summary>
    /// The paragraphs a range touches. A range that ends at offset 0 of a later paragraph (what a triple-click
    /// produces) does not touch that paragraph, and a paragraph whose whole text is selected counts its mark as
    /// selected too, so bullets and the paragraph mark follow the text's formatting like in Word.
    /// </summary>
    public static IEnumerable<ParagraphSpan> Paragraphs(ImmutableList<Block> blocks, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        int first = range.Start.Block.TopIndex;
        int last = LastBlockIndex(range);
        bool crossesEnd = last < range.End.Block.TopIndex;
        for (int i = first; i <= last && i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            int start = i == first ? range.Start.Offset : 0;
            bool isLast = i == last;
            int end = isLast && !crossesEnd ? Math.Min(range.End.Offset, paragraph.Length) : paragraph.Length;
            bool includesMark = !isLast || crossesEnd || (start == 0 && end >= paragraph.Length && paragraph.Length > 0);
            yield return new ParagraphSpan(i, paragraph, start, end, includesMark);
        }
    }

    public static IEnumerable<ParagraphSpan> Paragraphs(Document document, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Paragraphs(document.GetStory(range.Story), range);
    }

    /// <summary>Index of the last block the range touches (see <see cref="Paragraphs(ImmutableList{Block}, TextRange)"/>).</summary>
    public static int LastBlockIndex(TextRange range)
    {
        int first = range.Start.Block.TopIndex;
        int last = range.End.Block.TopIndex;
        return last > first && range.End.Offset == 0 ? last - 1 : last;
    }
}
