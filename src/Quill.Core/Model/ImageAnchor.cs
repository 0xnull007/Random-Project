using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>How body text treats a floating picture (Word's Wrap Text menu).</summary>
public enum ImageWrap
{
    /// <summary>Text flows down the side of the picture that has more room.</summary>
    Square,

    /// <summary>Lines stop above the picture and resume below it.</summary>
    TopAndBottom,

    /// <summary>The picture is drawn under the text, which ignores it.</summary>
    BehindText,

    /// <summary>The picture is drawn over the text, which ignores it.</summary>
    InFrontOfText,
}

/// <summary>What a floating picture's horizontal position is measured from.</summary>
public enum HorizontalAnchor
{
    Column,
    Margin,
    Page,
}

/// <summary>What a floating picture's vertical position is measured from.</summary>
public enum VerticalAnchor
{
    Paragraph,
    Margin,
    Page,
}

public enum ImageHorizontalAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Placement of a floating picture (docx <c>wp:anchor</c>). The picture stays attached to an anchor character
/// in its paragraph but is drawn at an offset from the paragraph, the margins or the page, and text wraps
/// around it according to <see cref="Wrap"/>. A picture without an anchor is inline.
/// </summary>
public sealed record ImageAnchor
{
    /// <summary>Square wrap at the top-left corner of the anchor paragraph with Word's default side gaps.</summary>
    public static readonly ImageAnchor Default = new();

    /// <summary>Word's default gap between a square-wrapped picture and the text beside it (0.13 inch).</summary>
    public static readonly Twips DefaultSideDistance = new(180);

    public ImageWrap Wrap { get; init; } = ImageWrap.Square;

    public HorizontalAnchor HorizontalRelativeTo { get; init; } = HorizontalAnchor.Column;

    /// <summary>Aligned placement within the horizontal reference; null means <see cref="OffsetX"/> applies.</summary>
    public ImageHorizontalAlignment? HorizontalAlignment { get; init; }

    public Twips OffsetX { get; init; } = Twips.Zero;

    public VerticalAnchor VerticalRelativeTo { get; init; } = VerticalAnchor.Paragraph;

    public Twips OffsetY { get; init; } = Twips.Zero;

    /// <summary>Gap kept between the picture and text to its left and right.</summary>
    public Twips DistanceX { get; init; } = DefaultSideDistance;

    /// <summary>Gap kept between the picture and text above and below it.</summary>
    public Twips DistanceY { get; init; } = Twips.Zero;

    /// <summary>True when lines of text are shortened or pushed by the picture.</summary>
    public bool AffectsText => Wrap is ImageWrap.Square or ImageWrap.TopAndBottom;

    /// <summary>Square wrap aligned within the text column, with the picture's top at the paragraph's top.</summary>
    public static ImageAnchor Aligned(ImageHorizontalAlignment alignment) => new() { HorizontalAlignment = alignment };
}
