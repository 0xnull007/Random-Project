using Quill.Core.Units;

namespace Quill.Core.Model;

public enum DropCapPosition
{
    None,

    /// <summary>The first letter hangs into the paragraph's first lines, which flow around it (Word's "Dropped").</summary>
    Dropped,

    /// <summary>The first letter sits in the left margin beside the paragraph ("In margin").</summary>
    InMargin,
}

/// <summary>A drop cap on a paragraph: its first letter enlarged to span <see cref="Lines"/> lines.</summary>
public sealed record DropCap(DropCapPosition Position, int Lines = 3, Twips Distance = default)
{
    /// <summary>Explicitly no drop cap (clears one inherited from a style).</summary>
    public static readonly DropCap None = new(DropCapPosition.None);

    public static DropCap Dropped(int lines = 3) => new(DropCapPosition.Dropped, Math.Clamp(lines, 1, 10));

    public static DropCap InMargin(int lines = 3) => new(DropCapPosition.InMargin, Math.Clamp(lines, 1, 10));

    public bool IsNone => Position == DropCapPosition.None;
}
