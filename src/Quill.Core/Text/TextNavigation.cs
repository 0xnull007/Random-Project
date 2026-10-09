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

        return StartOfNextParagraph(document, position);
    }

    /// <summary>Position one grapheme cluster to the left, crossing into the previous paragraph; null at the start of the story.</summary>
    public static TextPosition? PreviousCharacter(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        Paragraph paragraph = document.GetParagraph(position);
        if (position.Offset > 0)
        {
            return position.WithOffset(Graphemes.Previous(paragraph.FlatText, position.Offset));
        }

        return EndOfPreviousParagraph(document, position);
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

    /// <summary>Start of the next paragraph in the story, skipping non-paragraph blocks; null at the end.</summary>
    public static TextPosition? StartOfNextParagraph(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetStory(position.Story);
        for (int i = position.Block.TopIndex + 1; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph)
            {
                return new TextPosition(position.Story, BlockPath.Of(i), 0);
            }
        }

        return null;
    }

    /// <summary>End of the previous paragraph in the story, skipping non-paragraph blocks; null at the start.</summary>
    public static TextPosition? EndOfPreviousParagraph(Document document, TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImmutableList<Block> blocks = document.GetStory(position.Story);
        for (int i = position.Block.TopIndex - 1; i >= 0; i--)
        {
            if (blocks[i] is Paragraph previous)
            {
                return new TextPosition(position.Story, BlockPath.Of(i), previous.Length);
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
