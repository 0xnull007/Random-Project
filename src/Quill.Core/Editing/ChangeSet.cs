using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>
/// Describes where a document changed so layout can resume from the first affected block.
/// Unchanged paragraphs are detected by reference identity, so block counts are not needed.
/// </summary>
public readonly record struct ChangeSet(StoryId Story, int FirstBlock, bool StructureChanged = false)
{
    /// <summary>Blocks from <paramref name="firstBlock"/> onward in <paramref name="story"/> may have changed.</summary>
    public static ChangeSet From(StoryId story, int firstBlock) => new(story, firstBlock);

    /// <summary>Sections, page setup or styles changed: everything must be re-laid out.</summary>
    public static ChangeSet Structural() => new(StoryId.Body(0), 0, StructureChanged: true);

    /// <summary>The smallest change set that covers both.</summary>
    public ChangeSet Union(ChangeSet other)
    {
        if (StructureChanged || other.StructureChanged || Story != other.Story)
        {
            return Structural();
        }

        return new ChangeSet(Story, Math.Min(FirstBlock, other.FirstBlock));
    }
}

/// <summary>The outcome of a pure edit operation.</summary>
public sealed record EditResult(Document Document, Selection Selection, ChangeSet Change)
{
    public static EditResult NoOp(Document document, Selection selection) =>
        new(document, selection, new ChangeSet(selection.Story, int.MaxValue));

    public bool IsNoOp => Change.FirstBlock == int.MaxValue && !Change.StructureChanged;
}

public enum EditKind
{
    Other,
    Typing,
    Backspace,
    ForwardDelete,
    Formatting,
    Paste,
}

/// <summary>One undoable step: the document and selection before and after.</summary>
public sealed record EditRecord(
    Document Before,
    Selection SelectionBefore,
    Document After,
    Selection SelectionAfter,
    ChangeSet Change,
    EditKind Kind,
    long Timestamp,
    bool StartsNewGroup = false)
{
    public bool IsCoalescable => Kind is EditKind.Typing or EditKind.Backspace or EditKind.ForwardDelete;
}
