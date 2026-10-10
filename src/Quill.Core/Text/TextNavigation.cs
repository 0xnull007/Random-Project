using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;

namespace Quill.Core.Text;

/// <summary>
/// Layout-independent caret movement: by grapheme cluster, word and paragraph. Movement by line and page
/// needs layout and lives in the view.
/// </summary>
public static class TextNavigation
{
    /// <summary>Position one grapheme cluster to the right, crossing into the next paragraph; null at the end of the story.</summary>
    public static TextPosition? NextCharacter(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        if (position.Offset < paragraph.Length)
        {
            return position.WithOffset(Graphemes.Next(paragraph.FlatText, position.Offset));
        }

        return NextAcrossTables(document, position);
    }

    /// <summary>Position one grapheme cluster to the left, crossing into the previous paragraph or table cell; null at the start of the story.</summary>
    public static TextPosition? PreviousCharacter(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        if (position.Offset > 0)
        {
            return position.WithOffset(Graphemes.Previous(paragraph.FlatText, position.Offset));
        }

        return PreviousAcrossTables(document, position);
    }

    /// <summary>Ctrl+Right: start of the next word, or the paragraph end, or the next paragraph start.</summary>
    public static TextPosition? NextWord(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        string text = paragraph.FlatText;
        if (position.Offset >= text.Length)
        {
            return StartOfNextParagraph(document, position);
        }

        return position.WithOffset(Words.NextWordStart(text, position.Offset));
    }

    /// <summary>Ctrl+Left: start of the current or previous word, or the previous paragraph end.</summary>
    public static TextPosition? PreviousWord(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        if (position.Offset == 0)
        {
            return EndOfPreviousParagraph(document, position);
        }

        return position.WithOffset(Words.PreviousWordStart(paragraph.FlatText, position.Offset));
    }

    public static TextPosition ParagraphStart(TextPosition position) => position.WithOffset(0);

    public static TextPosition ParagraphEnd(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        return position.WithOffset(document.GetParagraph(position).Length);
    }

    public static TextPosition StoryStart(Document document, StoryId story)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetStory(story);
        int index = blocks.FindIndex(b => b is Paragraph);
        return new TextPosition(story, BlockPath.Of(Math.Max(0, index)), 0);
    }

    public static TextPosition StoryEnd(Document document, StoryId story)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetStory(story);
        int index = blocks.FindLastIndex(b => b is Paragraph);
        if (index < 0)
        {
            return new TextPosition(story, BlockPath.Of(0), 0);
        }

        return new TextPosition(story, BlockPath.Of(index), ((Paragraph)blocks[index]).Length);
    }

    /// <summary>The word under <paramref name="position"/> plus its trailing spaces (double-click selection).</summary>
    public static TextRange WordAt(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        (int start, int end) = Words.WordSpanAt(paragraph.FlatText, position.Offset);
        return new TextRange(position.WithOffset(start), position.WithOffset(end));
    }

    /// <summary>The whole paragraph including its mark: from its start to the start of the next paragraph (triple-click selection).</summary>
    public static TextRange ParagraphAt(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        TextPosition start = position.WithOffset(0);
        TextPosition end = StartOfNextParagraph(document, position) ?? ParagraphEnd(document, position);
        return new TextRange(start, end);
    }

    public static TextRange WholeStory(Document document, StoryId story)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new TextRange(StoryStart(document, story), StoryEnd(document, story));
    }

    /// <summary>Start of the next paragraph in the same block list (story or table cell), skipping non-paragraph blocks; null at the end.</summary>
    public static TextPosition? StartOfNextParagraph(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetContainer(position.Story, position.Block);
        for (int i = position.Block.Last + 1; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph)
            {
                return new TextPosition(position.Story, position.Block.WithLast(i), 0);
            }
        }

        return null;
    }

    /// <summary>End of the previous paragraph in the same block list, skipping non-paragraph blocks; null at the start.</summary>
    public static TextPosition? EndOfPreviousParagraph(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetContainer(position.Story, position.Block);
        for (int i = position.Block.Last - 1; i >= 0; i--)
        {
            if (blocks[i] is Paragraph previous)
            {
                return new TextPosition(position.Story, position.Block.WithLast(i), previous.Length);
            }
        }

        return null;
    }

    /// <summary>After the last paragraph of a block list: into the next table of the story, out of the current cell, or null.</summary>
    public static TextPosition? NextAcrossTables(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetContainer(position.Story, position.Block);
        for (int i = position.Block.Last + 1; i < blocks.Count; i++)
        {
            if (blocks[i] is Table && EnterTable(document, position.Story, position.Block.WithLast(i), fromEnd: false) is { } inside)
            {
                return inside;
            }

            if (blocks[i] is Paragraph)
            {
                return new TextPosition(position.Story, position.Block.WithLast(i), 0);
            }
        }

        return LeaveContainerForward(document, position);
    }

    public static TextPosition? PreviousAcrossTables(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetContainer(position.Story, position.Block);
        for (int i = position.Block.Last - 1; i >= 0; i--)
        {
            if (blocks[i] is Table && EnterTable(document, position.Story, position.Block.WithLast(i), fromEnd: true) is { } inside)
            {
                return inside;
            }

            if (blocks[i] is Paragraph previous)
            {
                return new TextPosition(position.Story, position.Block.WithLast(i), previous.Length);
            }
        }

        return LeaveContainerBackward(document, position);
    }

    /// <summary>First paragraph position inside a table cell (or null when the cell has no paragraph).</summary>

    public static TextPosition? CellStart(Document document, StoryId story, BlockPath tablePath, int row, int cell)
    {
        ArgumentNullException.ThrowIfNull(document);
        BlockPath first = tablePath.Child(row, cell, 0);
        ImmutableList<Block>? blocks = document.TryGetContainer(story, first);
        if (blocks is null)
        {
            return null;
        }

        int index = blocks.FindIndex(b => b is Paragraph);
        return index < 0 ? null : new TextPosition(story, first.WithLast(index), 0);
    }

    /// <summary>End of the last paragraph in the block list that holds <paramref name="position"/> (the cell, for a table position).</summary>
    public static TextPosition ContainerEnd(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetContainer(position.Story, position.Block);
        int last = blocks.FindLastIndex(b => b is Paragraph);
        return last < 0 ? position : new TextPosition(position.Story, position.Block.WithLast(last), ((Paragraph)blocks[last]).Length);
    }

    /// <summary>Tab in a table: the start of the next cell (row by row), or null in the last cell.</summary>

    public static TextPosition? NextCell(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (position.Block.TablePath is not { } tablePath || position.Block.CellIndices is not { } at || document.TryGetBlock(position.Story, tablePath) is not Table table)
        {
            return null;
        }

        int row = at.Row;
        int cell = at.Cell + 1;
        while (row < table.Rows.Length)
        {
            if (cell < table.Rows[row].Cells.Length)
            {
                return CellStart(document, position.Story, tablePath, row, cell) ?? NextCellAfter(document, position.Story, tablePath, table, row, cell);
            }

            row++;
            cell = 0;
        }

        return null;
    }

    private static TextPosition? NextCellAfter(Document document, StoryId story, BlockPath tablePath, Table table, int row, int cell)
    {
        for (int r = row; r < table.Rows.Length; r++)
        {
            for (int c = r == row ? cell + 1 : 0; c < table.Rows[r].Cells.Length; c++)
            {
                if (CellStart(document, story, tablePath, r, c) is { } start)
                {
                    return start;
                }
            }
        }

        return null;
    }

    /// <summary>Shift+Tab in a table: the start of the previous cell, or null in the first cell.</summary>
    public static TextPosition? PreviousCell(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (position.Block.TablePath is not { } tablePath || position.Block.CellIndices is not { } at || document.TryGetBlock(position.Story, tablePath) is not Table table)
        {
            return null;
        }

        int row = at.Row;
        int cell = at.Cell - 1;
        while (row >= 0)
        {
            if (cell >= 0 && cell < table.Rows[row].Cells.Length && CellStart(document, position.Story, tablePath, row, cell) is { } start)
            {
                return start;
            }

            if (cell > 0)
            {
                cell--;
                continue;
            }

            row--;
            cell = row >= 0 ? table.Rows[row].Cells.Length - 1 : -1;
        }

        return null;
    }

    /// <summary>Right arrow at the end of a cell: the next cell, or the paragraph after the table; Left arrow mirrors it.</summary>
    public static TextPosition? LeaveContainerForward(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (position.Block.TablePath is not { } tablePath)
        {
            return null;
        }

        if (NextCell(document, position) is { } next)
        {
            return next;
        }

        var afterTable = new TextPosition(position.Story, tablePath, 0);
        return StartOfNextParagraph(document, afterTable) ?? LeaveContainerForward(document, afterTable);
    }

    public static TextPosition? LeaveContainerBackward(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (position.Block.TablePath is not { } tablePath)
        {
            return null;
        }

        if (PreviousCell(document, position) is { } previous)
        {
            return ContainerEnd(document, previous);
        }

        var beforeTable = new TextPosition(position.Story, tablePath, 0);
        return EndOfPreviousParagraph(document, beforeTable) ?? LeaveContainerBackward(document, beforeTable);
    }

    /// <summary>The first paragraph inside a table at <paramref name="tablePath"/> (entering from the paragraph before it).</summary>
    public static TextPosition? EnterTable(Document document, StoryId story, BlockPath tablePath, bool fromEnd)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.TryGetBlock(story, tablePath) is not Table table || table.Rows.IsEmpty)
        {
            return null;
        }

        if (!fromEnd)
        {
            return NextCellAfter(document, story, tablePath, table, 0, -1);
        }

        for (int r = table.Rows.Length - 1; r >= 0; r--)
        {
            for (int c = table.Rows[r].Cells.Length - 1; c >= 0; c--)
            {
                if (CellStart(document, story, tablePath, r, c) is { } start)
                {
                    ImmutableList<Block> blocks = document.GetContainer(story, start.Block);
                    int last = blocks.FindLastIndex(b => b is Paragraph);
                    return new TextPosition(story, start.Block.WithLast(last), ((Paragraph)blocks[last]).Length);
                }
            }
        }

        return null;
    }
}

/// <summary>Grapheme-cluster stepping over UTF-16 text (UAX #29 via <see cref="StringInfo"/>).</summary>
public static class Graphemes
{
    public static int Next(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset >= text.Length)
        {
            return text.Length;
        }

        return offset + StringInfo.GetNextTextElementLength(text.AsSpan(offset));
    }

    public static int Previous(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset <= 0)
        {
            return 0;
        }

        offset = Math.Min(offset, text.Length);
        int position = 0;
        int previous = 0;
        while (position < offset)
        {
            previous = position;
            position += StringInfo.GetNextTextElementLength(text.AsSpan(position));
        }

        return previous;
    }

    /// <summary>Snaps an arbitrary offset to the start of the grapheme cluster containing it.</summary>
    public static int SnapToBoundary(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset <= 0 || offset >= text.Length)
        {
            return Math.Clamp(offset, 0, text.Length);
        }

        int position = 0;
        while (position < offset)
        {
            int next = position + StringInfo.GetNextTextElementLength(text.AsSpan(position));
            if (next > offset)
            {
                return position;
            }

            position = next;
        }

        return position;
    }
}

/// <summary>Word boundaries with Word-like rules: punctuation runs are words, trailing spaces belong to the preceding word.</summary>
public static class Words
{
    private enum CharClass
    {
        Space,
        Word,
        Punctuation,
        Object,
    }

    public static int NextWordStart(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        int i = Math.Clamp(offset, 0, text.Length);
        if (i >= text.Length)
        {
            return text.Length;
        }

        CharClass cls = Classify(text[i]);
        if (cls != CharClass.Space)
        {
            i = SkipClass(text, i, cls);
        }

        return SkipClass(text, i, CharClass.Space);
    }

    public static int PreviousWordStart(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        int i = Math.Clamp(offset, 0, text.Length);
        while (i > 0 && Classify(text[i - 1]) == CharClass.Space)
        {
            i--;
        }

        if (i == 0)
        {
            return 0;
        }

        CharClass cls = Classify(text[i - 1]);
        while (i > 0 && Classify(text[i - 1]) == cls)
        {
            i--;
        }

        return i;
    }

    /// <summary>The word containing <paramref name="offset"/> plus trailing spaces; on spaces, the preceding word plus those spaces.</summary>
    public static (int Start, int End) WordSpanAt(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return (0, 0);
        }

        int i = Math.Clamp(offset, 0, text.Length);
        if (i == text.Length || Classify(text[i]) == CharClass.Space)
        {
            // Back up onto the preceding token; the trailing spaces come along.
            int probe = i;
            while (probe > 0 && Classify(text[probe - 1]) == CharClass.Space)
            {
                probe--;
            }

            if (probe == 0)
            {
                return (0, SkipClass(text, 0, CharClass.Space));
            }

            i = probe - 1;
        }

        CharClass cls = Classify(text[i]);
        int start = i;
        while (start > 0 && Classify(text[start - 1]) == cls)
        {
            start--;
        }

        int end = SkipClass(text, i, cls);
        end = SkipClass(text, end, CharClass.Space);
        return (start, end);
    }

    /// <summary>True for letters, digits and the apostrophes that join words; used for whole-word matching.</summary>
    public static bool IsWordCharacter(char c) => Classify(c) == CharClass.Word;

    private static int SkipClass(string text, int i, CharClass cls)
    {
        while (i < text.Length && Classify(text[i]) == cls)
        {
            i++;
        }

        return i;
    }

    private static CharClass Classify(char c)
    {
        if (c == Paragraph.ObjectReplacementChar)
        {
            return CharClass.Object;
        }

        if (char.IsWhiteSpace(c))
        {
            return CharClass.Space;
        }

        if (char.IsLetterOrDigit(c) || c == '_' || c == '\'' || c == '’' || char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
        {
            return CharClass.Word;
        }

        return CharClass.Punctuation;
    }
}
