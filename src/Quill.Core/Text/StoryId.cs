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
    Footnote,
}

/// <summary>Identifies one editable story: the body of a section, one of its header/footer variants, or a footnote (by note id).</summary>
public readonly record struct StoryId(int SectionIndex, StoryKind Kind, int Note = 0) : IComparable<StoryId>
{
    public static StoryId Body(int sectionIndex) => new(sectionIndex, StoryKind.Body);

    public static StoryId Footnote(int noteId) => new(0, StoryKind.Footnote, noteId);

    public bool IsBody => Kind == StoryKind.Body;

    public bool IsHeader => Kind is StoryKind.HeaderDefault or StoryKind.HeaderFirst or StoryKind.HeaderEven;

    public bool IsFooter => Kind is StoryKind.FooterDefault or StoryKind.FooterFirst or StoryKind.FooterEven;

    public bool IsFootnote => Kind == StoryKind.Footnote;

    public int CompareTo(StoryId other)
    {
        int bySection = SectionIndex.CompareTo(other.SectionIndex);
        if (bySection != 0)
        {
            return bySection;
        }

        int byKind = Kind.CompareTo(other.Kind);
        return byKind != 0 ? byKind : Note.CompareTo(other.Note);
    }

    public override string ToString() => Kind == StoryKind.Footnote ? $"Footnote#{Note}" : $"{Kind}@{SectionIndex}";
}
