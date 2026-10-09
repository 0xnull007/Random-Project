using System.Globalization;
using DocumentFormat.OpenXml;
using Quill.Core.Model;
using Quill.Core.Units;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx;

/// <summary>Conversions between WordprocessingML attribute values and model types.</summary>
internal static class OoxmlValues
{
    public static bool? OnOff(W.OnOffType? element) => element is null ? null : element.Val is null || element.Val.Value;

    public static Twips? TwipsFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int twips))
        {
            return new Twips(twips);
        }

        // Transitional files may carry universal measures such as "0.5in" or "12pt".
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        foreach ((string unit, double perInch) in new[] { ("in", 1.0), ("pt", 72.0), ("cm", 2.54), ("mm", 25.4), ("pc", 6.0), ("pi", 6.0) })
        {
            if (span.EndsWith(unit, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(span[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                return Twips.FromInches(amount / perInch);
            }
        }

        return null;
    }

    public static HalfPoints? HalfPointsFrom(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hp) && hp > 0 ? new HalfPoints(hp) : null;

    public static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static Alignment? AlignmentFrom(W.Justification? jc) => jc?.Val?.InnerText switch
    {
        null => null,
        "center" => Alignment.Center,
        "right" or "end" => Alignment.Right,
        "both" or "distribute" => Alignment.Justify,
        _ => Alignment.Left,
    };

    public static W.JustificationValues AlignmentTo(Alignment alignment) => alignment switch
    {
        Alignment.Center => W.JustificationValues.Center,
        Alignment.Right => W.JustificationValues.Right,
        Alignment.Justify => W.JustificationValues.Both,
        _ => W.JustificationValues.Left,
    };

    public static UnderlineStyle? UnderlineFrom(W.Underline? u)
    {
        if (u is null)
        {
            return null;
        }

        return u.Val?.InnerText switch
        {
            "none" => UnderlineStyle.None,
            "double" => UnderlineStyle.Double,
            "dotted" or "dottedHeavy" => UnderlineStyle.Dotted,
            "dash" or "dashedHeavy" or "dashLong" or "dashLongHeavy" or "dotDash" or "dotDotDash" => UnderlineStyle.Dashed,
            "wave" or "wavyHeavy" or "wavyDouble" => UnderlineStyle.Wavy,
            "words" => UnderlineStyle.Words,
            _ => UnderlineStyle.Single,
        };
    }

    public static W.UnderlineValues UnderlineTo(UnderlineStyle style) => style switch
    {
        UnderlineStyle.None => W.UnderlineValues.None,
        UnderlineStyle.Double => W.UnderlineValues.Double,
        UnderlineStyle.Dotted => W.UnderlineValues.Dotted,
        UnderlineStyle.Dashed => W.UnderlineValues.Dash,
        UnderlineStyle.Wavy => W.UnderlineValues.Wave,
        UnderlineStyle.Words => W.UnderlineValues.Words,
        _ => W.UnderlineValues.Single,
    };

    public static HighlightColor? HighlightFrom(W.Highlight? h) => h?.Val?.InnerText switch
    {
        null => null,
        "black" => HighlightColor.Black,
        "blue" => HighlightColor.Blue,
        "cyan" => HighlightColor.Cyan,
        "green" => HighlightColor.Green,
        "magenta" => HighlightColor.Magenta,
        "red" => HighlightColor.Red,
        "yellow" => HighlightColor.Yellow,
        "white" => HighlightColor.White,
        "darkBlue" => HighlightColor.DarkBlue,
        "darkCyan" => HighlightColor.DarkCyan,
        "darkGreen" => HighlightColor.DarkGreen,
        "darkMagenta" => HighlightColor.DarkMagenta,
        "darkRed" => HighlightColor.DarkRed,
        "darkYellow" => HighlightColor.DarkYellow,
        "darkGray" => HighlightColor.DarkGray,
        "lightGray" => HighlightColor.LightGray,
        _ => HighlightColor.None,
    };

    public static W.HighlightColorValues HighlightTo(HighlightColor color) => color switch
    {
        HighlightColor.Black => W.HighlightColorValues.Black,
        HighlightColor.Blue => W.HighlightColorValues.Blue,
        HighlightColor.Cyan => W.HighlightColorValues.Cyan,
        HighlightColor.Green => W.HighlightColorValues.Green,
        HighlightColor.Magenta => W.HighlightColorValues.Magenta,
        HighlightColor.Red => W.HighlightColorValues.Red,
        HighlightColor.Yellow => W.HighlightColorValues.Yellow,
        HighlightColor.White => W.HighlightColorValues.White,
        HighlightColor.DarkBlue => W.HighlightColorValues.DarkBlue,
        HighlightColor.DarkCyan => W.HighlightColorValues.DarkCyan,
        HighlightColor.DarkGreen => W.HighlightColorValues.DarkGreen,
        HighlightColor.DarkMagenta => W.HighlightColorValues.DarkMagenta,
        HighlightColor.DarkRed => W.HighlightColorValues.DarkRed,
        HighlightColor.DarkYellow => W.HighlightColorValues.DarkYellow,
        HighlightColor.DarkGray => W.HighlightColorValues.DarkGray,
        HighlightColor.LightGray => W.HighlightColorValues.LightGray,
        _ => W.HighlightColorValues.None,
    };

    public static VerticalTextAlignment? VerticalFrom(W.VerticalTextAlignment? v) => v?.Val?.InnerText switch
    {
        null => null,
        "superscript" => VerticalTextAlignment.Superscript,
        "subscript" => VerticalTextAlignment.Subscript,
        _ => VerticalTextAlignment.Baseline,
    };

    public static W.VerticalPositionValues VerticalTo(VerticalTextAlignment v) => v switch
    {
        VerticalTextAlignment.Superscript => W.VerticalPositionValues.Superscript,
        VerticalTextAlignment.Subscript => W.VerticalPositionValues.Subscript,
        _ => W.VerticalPositionValues.Baseline,
    };

    public static TabAlignment TabAlignmentFrom(string? value) => value switch
    {
        "center" => TabAlignment.Center,
        "right" or "end" => TabAlignment.Right,
        "decimal" => TabAlignment.Decimal,
        "bar" => TabAlignment.Bar,
        _ => TabAlignment.Left,
    };

    public static W.TabStopValues TabAlignmentTo(TabAlignment alignment, bool cleared)
    {
        if (cleared)
        {
            return W.TabStopValues.Clear;
        }

        return alignment switch
        {
            TabAlignment.Center => W.TabStopValues.Center,
            TabAlignment.Right => W.TabStopValues.Right,
            TabAlignment.Decimal => W.TabStopValues.Decimal,
            TabAlignment.Bar => W.TabStopValues.Bar,
            _ => W.TabStopValues.Left,
        };
    }

    public static TabLeader TabLeaderFrom(string? value) => value switch
    {
        "dot" => TabLeader.Dot,
        "hyphen" => TabLeader.Hyphen,
        "underscore" => TabLeader.Underscore,
        "middleDot" => TabLeader.MiddleDot,
        "heavy" => TabLeader.Heavy,
        _ => TabLeader.None,
    };

    public static W.TabStopLeaderCharValues TabLeaderTo(TabLeader leader) => leader switch
    {
        TabLeader.Dot => W.TabStopLeaderCharValues.Dot,
        TabLeader.Hyphen => W.TabStopLeaderCharValues.Hyphen,
        TabLeader.Underscore => W.TabStopLeaderCharValues.Underscore,
        TabLeader.MiddleDot => W.TabStopLeaderCharValues.MiddleDot,
        TabLeader.Heavy => W.TabStopLeaderCharValues.Heavy,
        _ => W.TabStopLeaderCharValues.None,
    };

    public static SectionStart SectionStartFrom(string? value) => value switch
    {
        "continuous" => SectionStart.Continuous,
        "evenPage" => SectionStart.EvenPage,
        "oddPage" => SectionStart.OddPage,
        _ => SectionStart.NextPage,
    };

    public static W.SectionMarkValues SectionStartTo(SectionStart start) => start switch
    {
        SectionStart.Continuous => W.SectionMarkValues.Continuous,
        SectionStart.EvenPage => W.SectionMarkValues.EvenPage,
        SectionStart.OddPage => W.SectionMarkValues.OddPage,
        _ => W.SectionMarkValues.NextPage,
    };

    public static PageNumberFormat PageNumberFormatFrom(string? value) => value switch
    {
        "lowerRoman" => PageNumberFormat.LowerRoman,
        "upperRoman" => PageNumberFormat.UpperRoman,
        "lowerLetter" => PageNumberFormat.LowerLetter,
        "upperLetter" => PageNumberFormat.UpperLetter,
        _ => PageNumberFormat.Decimal,
    };

    public static W.NumberFormatValues PageNumberFormatTo(PageNumberFormat format) => format switch
    {
        PageNumberFormat.LowerRoman => W.NumberFormatValues.LowerRoman,
        PageNumberFormat.UpperRoman => W.NumberFormatValues.UpperRoman,
        PageNumberFormat.LowerLetter => W.NumberFormatValues.LowerLetter,
        PageNumberFormat.UpperLetter => W.NumberFormatValues.UpperLetter,
        _ => W.NumberFormatValues.Decimal,
    };

    public static HeaderFooterVariant VariantFrom(string? value) => value switch
    {
        "first" => HeaderFooterVariant.First,
        "even" => HeaderFooterVariant.Even,
        _ => HeaderFooterVariant.Default,
    };

    public static W.HeaderFooterValues VariantTo(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.First => W.HeaderFooterValues.First,
        HeaderFooterVariant.Even => W.HeaderFooterValues.Even,
        _ => W.HeaderFooterValues.Default,
    };

    public static StyleType StyleTypeFrom(string? value) => value switch
    {
        "character" => StyleType.Character,
        "table" => StyleType.Table,
        "numbering" => StyleType.Numbering,
        _ => StyleType.Paragraph,
    };

    public static W.StyleValues StyleTypeTo(StyleType type) => type switch
    {
        StyleType.Character => W.StyleValues.Character,
        StyleType.Table => W.StyleValues.Table,
        StyleType.Numbering => W.StyleValues.Numbering,
        _ => W.StyleValues.Paragraph,
    };

    public static FieldKind FieldKindFrom(string instruction)
    {
        string first = instruction.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.ToUpperInvariant() switch
        {
            "PAGE" => FieldKind.Page,
            "NUMPAGES" => FieldKind.NumPages,
            "SECTIONPAGES" => FieldKind.SectionPages,
            _ => FieldKind.Unknown,
        };
    }
}
