using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>One line of a table of contents: heading text, level (0 = Heading 1) and the page it is on.</summary>
public sealed record TocEntry(string Text, int Level, string PageNumber);

/// <summary>
/// A static table of contents built from the document's headings: paragraphs in the "toc 1-3" styles with a
/// right-aligned, dot-leader tab before the page number (what Word writes for an updated TOC field).
/// </summary>
public static class TableOfContents
{
    public const string HeadingStyleId = "TOCHeading";

    public static string EntryStyleId(int level) => "TOC" + (Math.Clamp(level, 0, 2) + 1).ToString(CultureInfo.InvariantCulture);

    public static bool IsTocStyle(string? styleId) =>
        styleId is not null && (styleId == HeadingStyleId || (styleId.Length == 4 && styleId.StartsWith("TOC", StringComparison.Ordinal) && char.IsDigit(styleId[3])));

    /// <summary>The styles a table of contents uses, built against the sheet's Normal and Heading 1 styles.</summary>
    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        string normal = sheet.DefaultParagraphStyleId;
        string headingBase = sheet.Contains(DefaultStyleSheet.Heading1Id) ? DefaultStyleSheet.Heading1Id : normal;
        yield return new Style
        {
            Id = HeadingStyleId,
            Name = "TOC Heading",
            BasedOn = headingBase,
            Next = normal,
            Priority = 39,
            ParagraphProperties = new ParagraphProperties { OutlineLevel = 9, KeepWithNext = true, SpaceAfter = Twips.FromPoints(6) },
        };
        for (int level = 0; level < 3; level++)
        {
            yield return new Style
            {
                Id = EntryStyleId(level),
                Name = "toc " + (level + 1).ToString(CultureInfo.InvariantCulture),
                BasedOn = normal,
                Next = normal,
                Priority = 39,
                ParagraphProperties = new ParagraphProperties { LeftIndent = Twips.FromInches(0.22 * level), SpaceAfter = Twips.FromPoints(5) },
            };
        }
    }

    public static ImmutableArray<Paragraph> Build(IReadOnlyList<TocEntry> entries, Twips columnWidth, string? title = "Contents")
    {
        ArgumentNullException.ThrowIfNull(entries);
        var tabs = TabStops.Create(new TabStop(columnWidth, TabAlignment.Right, TabLeader.Dot));
        var paragraphs = ImmutableArray.CreateBuilder<Paragraph>(entries.Count + 2);
        if (!string.IsNullOrEmpty(title))
        {
            paragraphs.Add(new Paragraph([new Run(title)], HeadingStyleId));
        }

        if (entries.Count == 0)
        {
            paragraphs.Add(new Paragraph([new Run("No headings found. Apply the Heading 1-3 styles, then insert the table of contents again.")], EntryStyleId(0)));
        }

        foreach (TocEntry entry in entries)
        {
            paragraphs.Add(new Paragraph([new Run(entry.Text), new Run("\t"), new Run(entry.PageNumber)], EntryStyleId(entry.Level), new ParagraphProperties { Tabs = tabs }));
        }

        return paragraphs.ToImmutable();
    }

    /// <summary>The existing table of contents: the first run of consecutive TOC-styled paragraphs in any body.</summary>
    public static (StoryId Story, int First, int Last)? FindExisting(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int s = 0; s < document.Sections.Count; s++)
        {
            ImmutableList<Block> body = document.Sections[s].Body;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i] is Paragraph paragraph && IsTocStyle(paragraph.StyleId))
                {
                    int last = i;
                    while (last + 1 < body.Count && body[last + 1] is Paragraph next && IsTocStyle(next.StyleId))
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
