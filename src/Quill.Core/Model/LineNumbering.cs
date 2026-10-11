using Quill.Core.Units;

namespace Quill.Core.Model;

public enum LineNumberRestart
{
    Continuous,
    EachPage,
    EachSection,
}

/// <summary>Word's Line Numbers (w:lnNumType): numbers in the left margin beside the body lines of a section.</summary>
public sealed record LineNumbering
{
    /// <summary>Show every nth number (all lines still count).</summary>
    public int CountBy { get; init; } = 1;

    public int Start { get; init; } = 1;

    /// <summary>Gap between the numbers and the text (0.25 in by default).</summary>
    public Twips Distance { get; init; } = Twips.FromInches(0.25);

    public LineNumberRestart Restart { get; init; } = LineNumberRestart.Continuous;
}
