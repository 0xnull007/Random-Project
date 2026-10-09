using DocumentFormat.OpenXml.Packaging;
using Quill.Core.Units;
using A = DocumentFormat.OpenXml.Drawing;

namespace Quill.Docx;

/// <summary>Theme fonts and colors, needed because most Word documents reference fonts and colors by theme slot.</summary>
internal sealed class ThemeInfo
{
    public static readonly ThemeInfo Default = new("Calibri Light", "Calibri", new Dictionary<string, DocColor>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, DocColor> _colors;

    private ThemeInfo(string majorFont, string minorFont, Dictionary<string, DocColor> colors)
    {
        MajorFont = majorFont;
        MinorFont = minorFont;
        _colors = colors;
    }

    public string MajorFont { get; }

    public string MinorFont { get; }

    public static ThemeInfo From(ThemePart? part)
    {
        A.ThemeElements? elements = part?.Theme?.ThemeElements;
        if (elements is null)
        {
            return Default;
        }

        string major = elements.FontScheme?.MajorFont?.LatinFont?.Typeface?.Value is { Length: > 0 } m ? m : Default.MajorFont;
        string minor = elements.FontScheme?.MinorFont?.LatinFont?.Typeface?.Value is { Length: > 0 } n ? n : Default.MinorFont;

        var colors = new Dictionary<string, DocColor>(StringComparer.OrdinalIgnoreCase);
        A.ColorScheme? scheme = elements.ColorScheme;
        if (scheme is not null)
        {
            Add(colors, "dark1", scheme.Dark1Color);
            Add(colors, "light1", scheme.Light1Color);
            Add(colors, "dark2", scheme.Dark2Color);
            Add(colors, "light2", scheme.Light2Color);
            Add(colors, "accent1", scheme.Accent1Color);
            Add(colors, "accent2", scheme.Accent2Color);
            Add(colors, "accent3", scheme.Accent3Color);
            Add(colors, "accent4", scheme.Accent4Color);
            Add(colors, "accent5", scheme.Accent5Color);
            Add(colors, "accent6", scheme.Accent6Color);
            Add(colors, "hyperlink", scheme.Hyperlink);
            Add(colors, "followedHyperlink", scheme.FollowedHyperlinkColor);
        }

        return new ThemeInfo(major, minor, colors);
    }

    /// <summary>Resolves a <c>w:asciiTheme</c>-style value ("minorHAnsi", "majorAscii", ...).</summary>
    public string Font(string themeFont) => themeFont.StartsWith("major", StringComparison.OrdinalIgnoreCase) ? MajorFont : MinorFont;

    /// <summary>Resolves a <c>w:themeColor</c> value; text1/background1 are aliases of dark1/light1.</summary>
    public DocColor? Color(string themeColor)
    {
        string key = themeColor switch
        {
            "text1" => "dark1",
            "text2" => "dark2",
            "background1" => "light1",
            "background2" => "light2",
            _ => themeColor,
        };
        return _colors.TryGetValue(key, out DocColor color) ? color : null;
    }

    private static void Add(Dictionary<string, DocColor> colors, string key, A.Color2Type? slot)
    {
        if (slot is null)
        {
            return;
        }

        string? hex = slot.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value ?? slot.GetFirstChild<A.SystemColor>()?.LastColor?.Value;
        if (hex is { Length: 6 })
        {
            colors[key] = DocColor.Parse(hex);
        }
    }
}
