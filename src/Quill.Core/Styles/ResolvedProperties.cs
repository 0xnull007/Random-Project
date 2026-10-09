using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Core.Styles;

/// <summary>Fully resolved character formatting: every value is known. Used as a layout cache key, so keep it a value-equal record.</summary>
public sealed record ResolvedRunProperties(
    string FontFamily,
    HalfPoints FontSize,
    bool Bold,
    bool Italic,
    UnderlineStyle Underline,
    bool Strikethrough,
    bool DoubleStrikethrough,
    DocColor Color,
    HighlightColor Highlight,
    VerticalTextAlignment VerticalAlignment,
    bool SmallCaps,
    bool AllCaps,
    bool Hidden,
    string Language);

/// <summary>Fully resolved paragraph formatting: every value is known.</summary>
public sealed record ResolvedParagraphProperties(
    Alignment Alignment,
    Twips LeftIndent,
    Twips RightIndent,
    Twips FirstLineIndent,
    Twips SpaceBefore,
    Twips SpaceAfter,
    LineSpacing LineSpacing,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool PageBreakBefore,
    bool WidowControl,
    bool ContextualSpacing,
    TabStops Tabs,
    int? OutlineLevel);
