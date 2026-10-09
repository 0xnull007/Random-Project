namespace Quill.Core.Text;

/// <summary>Which side of a soft line wrap a caret at a wrap offset belongs to.</summary>
public enum CaretAffinity
{
    /// <summary>Caret belongs to the line that starts at the offset (the default).</summary>
    Downstream,

    /// <summary>Caret belongs to the line that ends at the offset (after pressing End on a wrapped line).</summary>
    Upstream,
}

/// <summary>A caret position: a story, a block within it and a UTF-16 offset into the paragraph's flat text.</summary>
public readonly record struct TextPosition(StoryId Story, BlockPath Block, int Offset) : IComparable<TextPosition>
{
    public static TextPosition StartOf(StoryId story) => new(story, BlockPath.Of(0), 0);

    public TextPosition WithOffset(int offset) => this with { Offset = offset };

    public TextPosition WithBlock(BlockPath block, int offset) => new(Story, block, offset);

    public int CompareTo(TextPosition other)
    {
        int byStory = Story.CompareTo(other.Story);
        if (byStory != 0)
        {
            return byStory;
        }

        int byBlock = Block.CompareTo(other.Block);
        return byBlock != 0 ? byBlock : Offset.CompareTo(other.Offset);
    }

    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Story}{Block}:{Offset}";
}

/// <summary>A normalized, half-open range of positions within one story.</summary>
public readonly record struct TextRange
{
    public TextRange(TextPosition start, TextPosition end)
    {
        if (start.Story != end.Story)
        {
            throw new ArgumentException("A text range cannot span stories.", nameof(end));
        }

        if (start <= end)
        {
            Start = start;
            End = end;
        }
        else
        {
            Start = end;
            End = start;
        }
    }

    public TextPosition Start { get; }

    public TextPosition End { get; }

    public StoryId Story => Start.Story;

    public bool IsEmpty => Start == End;

    public bool IsWithinOneParagraph => Start.Block == End.Block;

    public bool Contains(TextPosition position) => position >= Start && position < End;
}

/// <summary>
/// A selection: the anchor is where the drag or shift-selection started, the active end moves with the caret.
/// A collapsed selection is the caret. Both ends always lie in the same story.
/// </summary>
public readonly record struct Selection
{
    public Selection(TextPosition anchor, TextPosition active, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        if (anchor.Story != active.Story)
        {
            throw new ArgumentException("A selection cannot span stories.", nameof(active));
        }

        Anchor = anchor;
        Active = active;
        Affinity = affinity;
    }

    public static Selection Caret(TextPosition position, CaretAffinity affinity = CaretAffinity.Downstream) =>
        new(position, position, affinity);

    public TextPosition Anchor { get; }

    public TextPosition Active { get; }

    public CaretAffinity Affinity { get; }

    public StoryId Story => Active.Story;

    public bool IsCollapsed => Anchor == Active;

    public bool IsForward => Active >= Anchor;

    public TextPosition Start => IsForward ? Anchor : Active;

    public TextPosition End => IsForward ? Active : Anchor;

    public TextRange Range => new(Anchor, Active);

    public Selection WithActive(TextPosition active, CaretAffinity affinity = CaretAffinity.Downstream) =>
        new(Anchor, active, affinity);

    public Selection CollapseToStart() => Caret(Start);

    public Selection CollapseToEnd() => Caret(End);

    public override string ToString() => IsCollapsed ? $"Caret({Active})" : $"Selection({Start}..{End})";
}
