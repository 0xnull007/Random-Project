namespace Quill.Core.Model;

public enum Alignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum UnderlineStyle
{
    None,
    Single,
    Double,
    Dotted,
    Dashed,
    Wavy,
    Words,
}

public enum VerticalTextAlignment
{
    Baseline,
    Superscript,
    Subscript,
}

public enum HighlightColor
{
    None,
    Black,
    Blue,
    Cyan,
    Green,
    Magenta,
    Red,
    Yellow,
    White,
    DarkBlue,
    DarkCyan,
    DarkGreen,
    DarkMagenta,
    DarkRed,
    DarkYellow,
    DarkGray,
    LightGray,
}

public enum TabAlignment
{
    Left,
    Center,
    Right,
    Decimal,
    Bar,
}

public enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underscore,
    MiddleDot,
    Heavy,
}

public enum BreakKind
{
    Line,
    Page,
    Column,
}

public enum FieldKind
{
    Page,
    NumPages,
    SectionPages,

    /// <summary>A SEQ field: the running number of a caption series such as "Figure" or "Table".</summary>
    Seq,

    /// <summary>A REF field: the text of a bookmark (cross-reference).</summary>
    Ref,

    /// <summary>A PAGEREF field: the page a bookmark is on.</summary>
    PageRef,

    /// <summary>DATE: today's date in the picture given by \@.</summary>
    Date,

    /// <summary>TIME: the current time in the picture given by \@.</summary>
    Time,

    /// <summary>AUTHOR, TITLE, SUBJECT or COMMENTS: a document property.</summary>
    DocProperty,
    Unknown,
}

public enum Orientation
{
    Portrait,
    Landscape,
}

public enum SectionStart
{
    NextPage,
    Continuous,
    EvenPage,
    OddPage,
}

public enum PageNumberFormat
{
    Decimal,
    LowerRoman,
    UpperRoman,
    LowerLetter,
    UpperLetter,
}

public enum StyleType
{
    Paragraph,
    Character,
    Table,
    Numbering,
}

public enum HeaderFooterVariant
{
    Default,
    First,
    Even,
}
