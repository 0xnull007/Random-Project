using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>One side of a paragraph border: style, width in eighths of a point, colour and the gap to the text in points.</summary>
public sealed record BorderLine(PageBorderStyle Style = PageBorderStyle.Single, int Width = 4, DocColor? Color = null, int Space = 1)
{
    public static readonly BorderLine Default = new();

    public DocColor EffectiveColor => Color ?? DocColor.Black;

    public double ThicknessDips => Width / 8.0 * 96 / 72;

    public double SpaceDips => Space * 96.0 / 72;
}

/// <summary>Paragraph borders (Word's w:pBdr); a null side has no line. <see cref="None"/> switches inherited borders off.</summary>
public sealed record ParagraphBorders(BorderLine? Top = null, BorderLine? Bottom = null, BorderLine? Left = null, BorderLine? Right = null)
{
    public static readonly ParagraphBorders None = new();

    public bool IsNone => Top is null && Bottom is null && Left is null && Right is null;

    public static ParagraphBorders Box(BorderLine? line = null)
    {
        BorderLine side = line ?? BorderLine.Default;
        return new ParagraphBorders(side, side, side, side);
    }

    public BorderLine? Get(string side) => side switch
    {
        "Top" => Top,
        "Bottom" => Bottom,
        "Left" => Left,
        "Right" => Right,
        _ => null,
    };

    public ParagraphBorders With(string side, BorderLine? line) => side switch
    {
        "Top" => this with { Top = line },
        "Bottom" => this with { Bottom = line },
        "Left" => this with { Left = line },
        "Right" => this with { Right = line },
        _ => this,
    };
}
