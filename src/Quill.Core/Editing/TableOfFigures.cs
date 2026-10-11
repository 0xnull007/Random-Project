using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>A caption paragraph found in the body: where it is and its text with the number resolved.</summary>
public sealed record CaptionEntry(StoryId Story, BlockPath Path, string Text);

/// <summary>
/// A static table of figures (or tables, equations...): one "table of figures" styled paragraph per caption of a
/// series, with a dot-leader tab before the page number, like Word's updated TOC \c field.
/// </summary>
public static class TableOfFigures
{
    public const string StyleId = "TableofFigures";

    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        yield return new Style
        {
            Id = StyleId,
            Name = "table of figures",
            BasedOn = sheet.DefaultParagraphStyleId,
            Next = sheet.DefaultParagraphStyleId,
            Priority = 99,
            ParagraphProperties = new ParagraphProperties { SpaceAfter = Twips.FromPoints(5) },
        };
    }

    /// <summary>The captions of <paramref name="label"/> in document order, text with the running number filled in.</summary>
    public static IReadOnlyList<CaptionEntry> Captions(Document document, string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        IReadOnlyDictionary<Field, int> numbers = SequenceFields.Numbers(document);
        var entries = new List<CaptionEntry>();
        for (int s = 0; s < document.Sections.Count; s++)
        {
            foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
            {
                if (paragraph.StyleId == StyleId || !paragraph.Inlines.Any(i => i is Field f && string.Equals(SequenceFields.Identifier(f), label, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string text = string.Concat(paragraph.Inlines.Select(i => i switch
                {
                    Run run => run.Text,
                    Field field => SequenceFields.Text(field, numbers),
                    _ => string.Empty,
                })).Trim();
                if (text.Length > 0)
                {
                    entries.Add(new CaptionEntry(StoryId.Body(s), path, text));
                }
            }
        }

        return entries;
    }

    public static ImmutableArray<Paragraph> Build(IReadOnlyList<TocEntry> entries, Twips columnWidth, string label)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var tabs = TabStops.Create(new TabStop(columnWidth, TabAlignment.Right, TabLeader.Dot));
        var paragraphs = ImmutableArray.CreateBuilder<Paragraph>(Math.Max(1, entries.Count));
        if (entries.Count == 0)
        {
            paragraphs.Add(new Paragraph([new Run("No " + label.ToLowerInvariant() + " captions found. Insert captions (References > Insert Caption), then insert the table again.")], StyleId));
        }

        foreach (TocEntry entry in entries)
        {
            paragraphs.Add(new Paragraph([new Run(entry.Text), new Run("\t"), new Run(entry.PageNumber)], StyleId, new ParagraphProperties { Tabs = tabs }));
        }

        return paragraphs.ToImmutable();
    }

    /// <summary>The existing table of figures: the first run of consecutive "table of figures" paragraphs in any body.</summary>
    public static (StoryId Story, int First, int Last)? FindExisting(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int s = 0; s < document.Sections.Count; s++)
        {
            ImmutableList<Block> body = document.Sections[s].Body;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i] is Paragraph { StyleId: StyleId })
                {
                    int last = i;
                    while (last + 1 < body.Count && body[last + 1] is Paragraph { StyleId: StyleId })
                    {
                        last++;
                    }

                    return (StoryId.Body(s), i, last);
                }
            }
        }

        return null;
    }
}
