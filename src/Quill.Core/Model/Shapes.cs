using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>The drawn shapes Quill supports (Word's Insert > Shapes basics).</summary>
public enum ShapeKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,

    /// <summary>A straight line from the top-left to the bottom-right of its box (a horizontal rule when the box is flat).</summary>
    Line,
}

/// <summary>
/// What makes an <see cref="InlineImage"/> a drawn shape instead of a picture: its geometry and fill. The outline
/// is the image's <see cref="InlineImage.Border"/>, and size, anchoring and wrapping work exactly as for pictures.
/// </summary>
public sealed record ShapeStyle(ShapeKind Kind, DocColor? Fill)
{
    /// <summary>Word's default shape fill (theme accent 1).</summary>
    public static readonly DocColor DefaultFill = DocColor.FromRgb(0x44, 0x72, 0xC4);

    /// <summary>Word's default shape outline colour (a darker accent 1).</summary>
    public static readonly DocColor DefaultOutline = DocColor.FromRgb(0x2F, 0x52, 0x8F);

    /// <summary>Word's defaults for a new shape of this kind: blue fill with a darker 1 pt outline; lines are blue ¾ pt strokes.</summary>
    public static (ShapeStyle Style, BorderLine Outline) Default(ShapeKind kind) => kind == ShapeKind.Line
        ? (new ShapeStyle(kind, null), new BorderLine(PageBorderStyle.Single, 6, DefaultFill, 0))
        : (new ShapeStyle(kind, DefaultFill), new BorderLine(PageBorderStyle.Single, 8, DefaultOutline, 0));
}
