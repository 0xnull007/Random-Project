using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>Captions: "Figure 1", "Table 2"... paragraphs with a SEQ field, numbered by document order.</summary>
public sealed partial class EditingSession
{
    /// <summary>The caption series a caption at the caret would default to: "Table" inside a table, otherwise "Figure".</summary>
    public string DefaultCaptionLabel => IsInTable ? "Table" : "Figure";

    /// <summary>The number the next caption of <paramref name="label"/> would get if it came after every existing one.</summary>
    public int NextCaptionNumber(string label) => SequenceFields.Count(Document, label) + 1;

    /// <summary>
    /// Inserts a caption paragraph ("Figure 3: text") above or below the table at the caret, or the paragraph at the
    /// caret (the one holding a selected picture), in the Caption style; the caret lands at its end.
    /// </summary>
    public void InsertCaption(string label, string? text, bool above)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        label = label.Trim();
        Document document = Document;
        foreach (Style style in CaptionSupport.RequiredStyles(document.Styles))
        {
            if (!document.Styles.Contains(style.Id))
            {
                document = document.WithStyles(document.Styles.With(style));
            }
        }

        StoryId story = Selection.Story;
        BlockPath anchor = TableAtCaret() is { } table ? table.Path : Selection.Start.Block;
        ImmutableList<Block> blocks = document.GetContainer(story, anchor);
        int index = anchor.Last + (above ? 0 : 1);

        var inlines = ImmutableArray.CreateBuilder<Inline>();
        inlines.Add(new Run(label + " "));
        inlines.Add(new Field(FieldKind.Seq, SequenceFields.Instruction(label), "1"));
        if (!string.IsNullOrWhiteSpace(text))
        {
            inlines.Add(new Run(": " + text.Trim()));
        }

        var caption = new Paragraph(inlines.ToImmutable(), CaptionSupport.StyleId);
        document = SequenceFields.RefreshResults(document.WithContainer(story, anchor, blocks.Insert(index, caption)));
        var caret = new TextPosition(story, anchor.WithLast(index), caption.Length);
        Commit(new EditResult(document, Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Inserts (or replaces) the table of figures for a caption series at the caret, like <see cref="InsertTableOfContents"/>.</summary>
    public void InsertTableOfFigures(IReadOnlyList<TocEntry> entries, Twips columnWidth, string label)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Document document = Document;
        foreach (Style style in TableOfFigures.RequiredStyles(document.Styles))
        {
            if (!document.Styles.Contains(style.Id))
            {
                document = document.WithStyles(document.Styles.With(style));
            }
        }

        ImmutableArray<Paragraph> paragraphs = TableOfFigures.Build(entries, columnWidth, label);
        StoryId story;
        int index;
        ImmutableList<Block> blocks;
        if (TableOfFigures.FindExisting(document) is { } existing)
        {
            story = existing.Story;
            index = existing.First;
            blocks = document.GetStory(story).RemoveRange(index, existing.Last - existing.First + 1);
        }
        else
        {
            story = StoryId.Body(Selection.Story.SectionIndex);
            index = Selection.Story == story ? Selection.Range.Start.Block.TopIndex : 0;
            blocks = document.GetStory(story);
        }

        blocks = blocks.InsertRange(index, paragraphs);
        document = document.WithStory(story, blocks);
        int after = Math.Min(index + paragraphs.Length, blocks.Count - 1);
        Commit(new EditResult(document, Selection.Caret(new TextPosition(story, BlockPath.Of(after), 0)), ChangeSet.Structural()), EditKind.Paste, startsNewGroup: true);
    }
}
