using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>Home > Paragraph > Sort and Insert > Pages (Blank Page, Cover Page).</summary>
public sealed partial class EditingSession
{
    /// <summary>
    /// Sorts the selected paragraphs (the whole block list at the caret when nothing is selected) by their text,
    /// numerically when every paragraph starts with a number; paragraphs move with their formatting. One undo step.
    /// </summary>
    public void SortParagraphs(bool descending = false)
    {
        StoryId story = Selection.Story;
        ImmutableList<Block> blocks = Document.GetContainer(story, Selection.Active.Block);
        int first;
        int last;
        if (Selection.IsCollapsed)
        {
            first = 0;
            last = blocks.Count - 1;
        }
        else
        {
            TextRange range = Selection.Range;
            if (!range.Start.Block.SameContainer(range.End.Block))
            {
                return;
            }

            first = range.Start.Block.Last;
            last = range.End.Block.Last;
            if (last > first && range.End.Offset == 0)
            {
                last--; // a paragraph counts only when some of it is selected, like Word
            }
        }

        if (last <= first)
        {
            return;
        }

        var paragraphs = new List<Paragraph>();
        for (int i = first; i <= last; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                return; // tables do not sort
            }

            paragraphs.Add(paragraph);
        }

        bool numeric = paragraphs.All(p => LeadingNumber(p.FlatText) is not null);
        IEnumerable<Paragraph> ordered = numeric
            ? paragraphs.OrderBy(p => LeadingNumber(p.FlatText)!.Value)
            : paragraphs.OrderBy(p => p.FlatText.Trim(), StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true));
        List<Paragraph> sorted = (descending ? ordered.Reverse() : ordered).ToList();
        if (sorted.SequenceEqual(paragraphs))
        {
            return;
        }

        ImmutableList<Block> updated = blocks;
        for (int i = 0; i < sorted.Count; i++)
        {
            updated = updated.SetItem(first + i, sorted[i]);
        }

        BlockPath anchor = Selection.Active.Block;
        var selection = new Selection(new TextPosition(story, anchor.WithLast(first), 0), new TextPosition(story, anchor.WithLast(last), sorted[^1].Length));
        Commit(new EditResult(Document.WithContainer(story, anchor, updated), selection, ChangeSet.From(story, anchor.TopIndex)), EditKind.Other, startsNewGroup: true);
    }

    private static double? LeadingNumber(string text)
    {
        string trimmed = text.TrimStart();
        int end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '.' or ',' or '-' or '+') && !(end > 0 && trimmed[end] is '-' or '+'))
        {
            end++;
        }

        return end > 0 && double.TryParse(trimmed[..end], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double value) ? value : null;
    }

    /// <summary>Word's Blank Page: a page break, an empty paragraph and another page break at the caret; the caret ends up after them.</summary>
    public void InsertBlankPage()
    {
        var fragment = new DocumentFragment(
        [
            new Paragraph([new Break(BreakKind.Page)]),
            Paragraph.Empty(),
            new Paragraph([new Break(BreakKind.Page)]),
        ]);
        Paste(fragment);
    }

    /// <summary>
    /// Word's Cover Page: title, subtitle, author and date paragraphs followed by a page break at the very start of
    /// the document; the first section gets a different (empty) first-page header and footer. The title is selected
    /// so typing replaces it.
    /// </summary>
    public void InsertCoverPage(string author)
    {
        ArgumentNullException.ThrowIfNull(author);
        Document document = Document;
        foreach (Style style in CoverPageSupport.RequiredStyles(document.Styles))
        {
            if (!document.Styles.Contains(style.Id))
            {
                document = document.WithStyles(document.Styles.With(style));
            }
        }

        string title = string.IsNullOrWhiteSpace(document.Metadata.Title) ? "Title" : document.Metadata.Title!;
        string? titleStyle = document.Styles.Contains(DefaultStyleSheet.TitleId) ? DefaultStyleSheet.TitleId : null;
        var cover = ImmutableList.Create<Block>(
            new Paragraph([new Run(title)], titleStyle, new ParagraphProperties { SpaceBefore = Twips.FromInches(2.5) }),
            new Paragraph([new Run("Subtitle")], CoverPageSupport.SubtitleStyleId),
            new Paragraph([new Run(author)], null, new ParagraphProperties { SpaceBefore = Twips.FromInches(2) }),
            new Paragraph([new Run(DateTime.Today.ToString("D", CultureInfo.CurrentCulture))]),
            new Paragraph([new Break(BreakKind.Page)]));
        var story = StoryId.Body(0);
        Section first = document.Sections[0];
        document = document.WithStory(story, first.Body.InsertRange(0, cover));
        document = document.WithSection(0, document.Sections[0].WithProperties(first.Properties with { TitlePage = true }));

        var selection = new Selection(new TextPosition(story, BlockPath.Of(0), 0), new TextPosition(story, BlockPath.Of(0), title.Length));
        Commit(new EditResult(document, selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }
}

/// <summary>The "Subtitle" paragraph style the cover page uses.</summary>
public static class CoverPageSupport
{
    public const string SubtitleStyleId = "Subtitle";

    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        yield return new Style
        {
            Id = SubtitleStyleId,
            Name = "Subtitle",
            BasedOn = sheet.DefaultParagraphStyleId,
            Next = sheet.DefaultParagraphStyleId,
            Priority = 11,
            QuickFormat = true,
            ParagraphProperties = new ParagraphProperties { SpaceAfter = Twips.FromPoints(8) },
            RunProperties = new RunProperties { FontSize = HalfPoints.FromPoints(14), Color = DocColor.Parse("595959") },
        };
    }
}
