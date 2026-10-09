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
