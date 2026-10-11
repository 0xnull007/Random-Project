using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>Design tab: page colour and page borders.</summary>
public sealed partial class EditingSession
{
    /// <summary>The page colour, or null for white.</summary>
    public DocColor? PageColor => Document.Settings.PageColor;

    /// <summary>Page borders of the caret's section, or null.</summary>
    public PageBorders? PageBordersAtCaret => Document.Sections[Selection.Story.SectionIndex].Properties.PageBorders;

    /// <summary>Colours every page (null or automatic clears it); one undo step.</summary>
    public void SetPageColor(DocColor? color)
    {
        DocColor? value = color is { IsAuto: false } ? color : null;
        if (value == Document.Settings.PageColor)
        {
            return;
        }

        Commit(new EditResult(Document.WithSettings(Document.Settings with { PageColor = value }), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>Sets or (with null) removes the watermark drawn behind every page; one undo step.</summary>
    public void SetWatermark(Watermark? watermark)
    {
        Watermark? value = watermark is { Text.Length: > 0 } ? watermark with { Text = watermark.Text.Trim() } : null;
        if (Equals(value, Document.Settings.Watermark))
        {
            return;
        }

        Commit(new EditResult(Document.WithSettings(Document.Settings with { Watermark = value }), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>Line numbers for the caret's section: null removes them; one undo step.</summary>
    public void SetLineNumbers(LineNumbering? numbering)
    {
        int section = Selection.Story.SectionIndex;
        SectionProperties props = Document.Sections[section].Properties;
        if (Equals(props.LineNumbers, numbering))
        {
            return;
        }

        SetSectionProperties(section, props with { LineNumbers = numbering });
    }

    /// <summary>Leaves the selected paragraphs out of (or back into) the line numbering.</summary>
    public void SuppressLineNumbers(bool suppress) => ApplyParagraphFormat(new ParagraphProperties { SuppressLineNumbers = suppress });

    /// <summary>Sets (or with null removes) the page border of the caret's section, or of every section; one undo step.</summary>


    public void SetPageBorders(PageBorders? borders, bool wholeDocument)
    {
        Document document = Document;
        int current = Selection.Story.SectionIndex;
        for (int i = 0; i < document.Sections.Count; i++)
        {
            if (!wholeDocument && i != current)
            {
                continue;
            }

            Section section = document.Sections[i];
            if (!Equals(section.Properties.PageBorders, borders))
            {
                document = document.WithSection(i, section.WithProperties(section.Properties with { PageBorders = borders }));
            }
        }

        if (!ReferenceEquals(document, Document))
        {
            Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
        }
    }
}
