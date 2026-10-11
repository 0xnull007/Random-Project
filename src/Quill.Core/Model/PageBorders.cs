using Quill.Core.Units;

namespace Quill.Core.Model;

public enum PageBorderStyle
{
    Single,
    Double,
    Dotted,
    Dashed,
    Thick,
}

/// <summary>A border drawn around every page of a section (Word's Page Borders), the same on all four sides.</summary>
public sealed record PageBorders
{
    /// <summary>Line width in eighths of a point (Word's w:sz), 2..96.</summary>
    public int Width { get; init; } = 4;

    public PageBorderStyle Style { get; init; } = PageBorderStyle.Single;

    public DocColor Color { get; init; } = DocColor.Black;

    /// <summary>Distance in points between the border and the page edge (or the text when <see cref="FromText"/>).</summary>
    public int Space { get; init; } = 24;

    /// <summary>True when the border sits <see cref="Space"/> points outside the text area instead of inside the page edge.</summary>
    public bool FromText { get; init; }

    /// <summary>Line thickness in DIPs.</summary>
    public double ThicknessDips => Width / 8.0 * 96 / 72;

    public double SpaceDips => Space * 96.0 / 72;
}
