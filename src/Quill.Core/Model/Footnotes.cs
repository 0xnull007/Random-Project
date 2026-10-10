using System.Collections.Immutable;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>Footnote numbering and the styles footnotes use.</summary>
public static class FootnoteSupport
{
    public const string TextStyleId = "FootnoteText";
    public const string ReferenceStyleId = "FootnoteReference";

    /// <summary>Note ids in the order their references appear in the body, each once.</summary>
    public static IReadOnlyList<int> ReferencedNoteIds(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = new List<int>();
        var seen = new HashSet<int>();
        foreach (Section section in document.Sections)
        {
            foreach (Block block in section.Body)
            {
                foreach (Paragraph paragraph in Paragraphs(block))
                {
                    foreach (Inline inline in paragraph.Inlines)
                    {
                        if (inline is FootnoteReference { IsNoteMark: false } note && document.Footnotes.ContainsKey(note.NoteId) && seen.Add(note.NoteId))
                        {
                            ids.Add(note.NoteId);
                        }
                    }
                }
            }
        }

        return ids;
    }

    /// <summary>Note id to displayed number (1, 2, 3... in reference order).</summary>
    public static IReadOnlyDictionary<int, int> Numbers(Document document)
    {
        var numbers = new Dictionary<int, int>();
        int n = 1;
        foreach (int id in ReferencedNoteIds(document))
        {
            numbers[id] = n++;
        }

        return numbers;
    }

    /// <summary>The "footnote text" and "footnote reference" styles, built against the sheet's Normal style.</summary>
    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        yield return new Style
        {
            Id = TextStyleId,
            Name = "footnote text",
            BasedOn = sheet.DefaultParagraphStyleId,
            Priority = 99,
            ParagraphProperties = new ParagraphProperties { SpaceAfter = Twips.Zero, LineSpacing = LineSpacing.Single },
            RunProperties = new RunProperties { FontSize = HalfPoints.FromPoints(10) },
        };
        yield return new Style
        {
            Id = ReferenceStyleId,
            Name = "footnote reference",
            Type = StyleType.Character,
            BasedOn = sheet.DefaultCharacterStyleId,
            Priority = 99,
            RunProperties = new RunProperties { VerticalAlignment = VerticalTextAlignment.Superscript },
        };
    }

    private static IEnumerable<Paragraph> Paragraphs(Block block) => block switch
    {
        Paragraph paragraph => [paragraph],
        Table table => table.AllParagraphs(),
        _ => [],
    };
}
