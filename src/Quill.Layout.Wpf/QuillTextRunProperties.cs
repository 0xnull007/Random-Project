using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Quill.Core.Model;
using Quill.Core.Styles;

namespace Quill.Layout.Wpf;

/// <summary>Adapts resolved character formatting to WPF's TextFormatter.</summary>
internal sealed class QuillTextRunProperties : TextRunProperties
{
    private const double ScriptScale = 0.65;

    private readonly Typeface _typeface;
    private readonly double _emSize;
    private readonly Brush _foreground;
    private readonly Brush? _background;
    private readonly TextDecorationCollection? _decorations;
    private readonly CultureInfo _culture;
    private readonly BaselineAlignment _baseline;

    public QuillTextRunProperties(ResolvedRunProperties properties, double pixelsPerDip, FontCatalog fonts)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(fonts);
        Properties = properties;
        _typeface = fonts.GetTypeface(properties.FontFamily, properties.Bold, properties.Italic);
        double scale = properties.VerticalAlignment == VerticalTextAlignment.Baseline ? 1 : ScriptScale;
        _emSize = Math.Max(1, properties.FontSize.ToDips() * scale);
        _foreground = fonts.GetBrush(properties.Color);
        _background = fonts.GetHighlightBrush(properties.Highlight);
        _decorations = BuildDecorations(properties);
        _culture = SafeCulture(properties.Language);
        _baseline = properties.VerticalAlignment switch
        {
            VerticalTextAlignment.Superscript => BaselineAlignment.Superscript,
            VerticalTextAlignment.Subscript => BaselineAlignment.Subscript,
            _ => BaselineAlignment.Baseline,
        };
        PixelsPerDip = pixelsPerDip;
    }

    public ResolvedRunProperties Properties { get; }

    public override Typeface Typeface => _typeface;

    public override double FontRenderingEmSize => _emSize;

    public override double FontHintingEmSize => _emSize;

    public override TextDecorationCollection? TextDecorations => _decorations;

    public override Brush ForegroundBrush => _foreground;

    public override Brush? BackgroundBrush => _background;

    public override CultureInfo CultureInfo => _culture;

    public override TextEffectCollection? TextEffects => null;

    public override BaselineAlignment BaselineAlignment => _baseline;

    private static TextDecorationCollection? BuildDecorations(ResolvedRunProperties properties)
    {
        if (properties.Underline == UnderlineStyle.None && !properties.Strikethrough && !properties.DoubleStrikethrough)
        {
            return null;
        }

        var collection = new TextDecorationCollection();
        if (properties.Underline != UnderlineStyle.None)
        {
            collection.Add(System.Windows.TextDecorations.Underline);
        }

        if (properties.Strikethrough || properties.DoubleStrikethrough)
        {
            collection.Add(System.Windows.TextDecorations.Strikethrough);
        }

        collection.Freeze();
        return collection;
    }

    private static CultureInfo SafeCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
