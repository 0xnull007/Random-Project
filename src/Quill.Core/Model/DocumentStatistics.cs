using System.Collections.Immutable;

namespace Quill.Core.Model;

/// <summary>Word, character and paragraph counts the way Word reports them.</summary>
public readonly record struct DocumentStatistics(int Words, int Characters, int CharactersWithSpaces, int Paragraphs)
{
    public static DocumentStatistics Compute(IEnumerable<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        int words = 0;
        int characters = 0;
        int withSpaces = 0;
        int count = 0;
        foreach (Paragraph paragraph in paragraphs)
        {
            string text = VisibleText(paragraph);
            bool hasContent = false;
            bool inWord = false;
            foreach (char c in text)
            {
                if (c == Paragraph.ObjectReplacementChar)
                {
                    inWord = false;
                    continue;
                }

                withSpaces++;
                if (char.IsWhiteSpace(c))
                {
                    inWord = false;
                    continue;
                }

                characters++;
                hasContent = true;
                if (!inWord)
                {
                    inWord = true;
                    words++;
                }
            }

            if (hasContent)
            {
                count++;
            }
        }

        return new DocumentStatistics(words, characters, withSpaces, count);
    }

    /// <summary>The paragraph text without runs marked as tracked deletions (Word does not count deleted text).</summary>
    private static string VisibleText(Paragraph paragraph)
    {
        string flat = paragraph.FlatText;
        if (!paragraph.Inlines.Any(i => i.Properties.Revision is { IsDeletion: true }))
        {
            return flat;
        }

        var builder = new System.Text.StringBuilder(flat.Length);
        foreach (InlineSpan span in paragraph.Spans())
        {
            if (span.Inline.Properties.Revision is not { IsDeletion: true })
            {
                builder.Append(flat, span.Start, span.Length);
            }
        }

        return builder.ToString();
    }


    /// <summary>Statistics for every body paragraph of the document.</summary>
    public static DocumentStatistics Compute(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Compute(document.Sections.SelectMany(s => BlockWalker.Paragraphs(s.Body)).Select(p => p.Paragraph));
    }

    public static DocumentStatistics Compute(ImmutableArray<Paragraph> paragraphs) => Compute((IEnumerable<Paragraph>)paragraphs);
}
