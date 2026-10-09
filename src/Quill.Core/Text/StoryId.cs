namespace Quill.Core.Text;

public enum StoryKind
{
    Body,
    HeaderDefault,
    HeaderFirst,
    HeaderEven,
    FooterDefault,
    FooterFirst,
    FooterEven,
}

/// <summary>Identifies one editable story: the body of a section or one of its header/footer variants.</summary>
public readonly record struct StoryId(int SectionIndex, StoryKind Kind) : IComparable<StoryId>
{
    public static StoryId Body(int sectionIndex) => new(sectionIndex, StoryKind.Body);

    public bool IsBody => Kind == StoryKind.Body;

    public bool IsHeader => Kind is StoryKind.HeaderDefault or StoryKind.HeaderFirst or StoryKind.HeaderEven;

    public bool IsFooter => Kind is StoryKind.FooterDefault or StoryKind.FooterFirst or StoryKind.FooterEven;

    public int CompareTo(StoryId other)
    {
        int bySection = SectionIndex.CompareTo(other.SectionIndex);
        return bySection != 0 ? bySection : Kind.CompareTo(other.Kind);
    }

    public override string ToString() => $"{Kind}@{SectionIndex}";
}
