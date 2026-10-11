using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>SEQ fields: "Figure 1", "Table 2"... numbered by their order in the document body.</summary>
public static class SequenceFields
{
    /// <summary>The field instruction Word writes for a caption series.</summary>
    public static string Instruction(string identifier) => "SEQ " + identifier.Trim().Replace(' ', '_') + " \\* ARABIC";

    /// <summary>The series name of a SEQ field ("Figure" for "SEQ Figure \* ARABIC"), or null for other fields.</summary>
    public static string? Identifier(Field field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind != FieldKind.Seq)
        {
            return null;
        }

        string[] parts = field.Instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1].Replace('_', ' ') : null;
    }

    /// <summary>The number of every SEQ field in the body stories, in document order (table cells included), keyed by the field instance.</summary>
    public static IReadOnlyDictionary<Field, int> Numbers(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var numbers = new Dictionary<Field, int>(ReferenceEqualityComparer.Instance);
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Section section in document.Sections)
        {
            foreach ((BlockPath _, Paragraph paragraph) in BlockWalker.Paragraphs(section.Body))
            {
                foreach (Inline inline in paragraph.Inlines)
                {
                    if (inline is Field field && Identifier(field) is { } id)
                    {
                        int next = counters.GetValueOrDefault(id) + 1;
                        counters[id] = next;
                        numbers[field] = next;
                    }
                }
            }
        }

        return numbers;
    }

    /// <summary>How many captions of a series the document already has.</summary>
    public static int Count(Document document, string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Numbers(document).Keys.Count(f => string.Equals(Identifier(f), identifier, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The displayed text of a field: its running number for SEQ fields, otherwise the stored result.</summary>
    public static string Text(Field field, IReadOnlyDictionary<Field, int>? numbers)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Kind == FieldKind.Seq && numbers is not null && numbers.TryGetValue(field, out int number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : field.CachedResult;
    }

    /// <summary>A copy of the document whose SEQ fields store their current numbers (what copies, exports and other apps see).</summary>
    public static Document RefreshResults(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyDictionary<Field, int> numbers = Numbers(document);
        if (numbers.Count == 0)
        {
            return document;
        }

        for (int s = 0; s < document.Sections.Count; s++)
        {
            Section section = document.Sections[s];
            ImmutableList<Block> body = BlockWalker.MapParagraphs(section.Body, paragraph =>
            {
                bool stale = paragraph.Inlines.Any(i => i is Field f && numbers.TryGetValue(f, out int n) && f.CachedResult != n.ToString(CultureInfo.InvariantCulture));
                return stale
                    ? paragraph.WithInlines(paragraph.Inlines.Select(i => i is Field f && numbers.TryGetValue(f, out int n) ? f.WithCachedResult(n.ToString(CultureInfo.InvariantCulture)) : i).ToImmutableArray())
                    : paragraph;
            });
            if (!ReferenceEquals(body, section.Body))
            {
                document = document.WithStory(StoryId.Body(s), body);
            }
        }

        return document;
    }
}

/// <summary>The "Caption" paragraph style captions are inserted with.</summary>
public static class CaptionSupport
{
    public const string StyleId = "Caption";

    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        yield return new Style
        {
            Id = StyleId,
            Name = "caption",
            BasedOn = sheet.DefaultParagraphStyleId,
            Next = sheet.DefaultParagraphStyleId,
            Priority = 35,
            QuickFormat = true,
            ParagraphProperties = new ParagraphProperties { SpaceAfter = Twips.FromPoints(10), LineSpacing = LineSpacing.Single },
            RunProperties = new RunProperties { FontSize = HalfPoints.FromPoints(9), Color = DocColor.Parse("44546A") },
        };
    }
}
