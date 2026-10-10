using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Layout.Wpf;

/// <summary>Caches WPF typefaces and frozen brushes; safe to share across threads.</summary>
public sealed class FontCatalog
{
    public static readonly FontCatalog Shared = new();

    private readonly ConcurrentDictionary<string, FontFamily> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), Typeface> _typefaces = new();
    private readonly ConcurrentDictionary<uint, SolidColorBrush> _brushes = new();

    public FontFamily GetFamily(string name) => _families.GetOrAdd(name, static n => new FontFamily(n));

    public Typeface GetTypeface(string family, bool bold, bool italic) =>
        _typefaces.GetOrAdd((family, bold, italic), key => CreateTypeface(key.Family, key.Bold, key.Italic));

    /// <summary>
    /// Families whose name carries the weight ("Calibri Light", "Segoe UI Semibold") have no bold or italic faces of
    /// their own and WPF does not always simulate them; fall back to the base family ("Calibri") in that case.
    /// </summary>
    private static Typeface CreateTypeface(string family, bool bold, bool italic)
    {
        Typeface typeface = Make(family, bold, italic);
        if ((!bold && !italic) || Satisfies(typeface, bold, italic))
        {
            return typeface;
        }

        if (BaseFamilyName(family) is { } baseFamily)
        {
            Typeface alternative = Make(baseFamily, bold, italic);
            if (Satisfies(alternative, bold, italic))
            {
                return alternative;
            }
        }

        return typeface;
    }

    private static Typeface Make(string family, bool bold, bool italic) => new(
        Shared.GetFamily(family),
        italic ? FontStyles.Italic : FontStyles.Normal,
        bold ? FontWeights.Bold : FontWeights.Normal,
        FontStretches.Normal);

    private static bool Satisfies(Typeface typeface, bool bold, bool italic)
    {
        if (!typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphs) || glyphs is null)
        {
            return true; // unknown family: WPF falls back on its own; nothing better to offer
        }

        bool boldOk = !bold || glyphs.Weight.ToOpenTypeWeight() >= 600 || (glyphs.StyleSimulations & StyleSimulations.BoldSimulation) != 0;
        bool italicOk = !italic || glyphs.Style != FontStyles.Normal || (glyphs.StyleSimulations & StyleSimulations.ItalicSimulation) != 0;
        return boldOk && italicOk;
    }

    private static string? BaseFamilyName(string family)
    {
        foreach (string suffix in WeightSuffixes)
        {
            if (family.Length > suffix.Length && family.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return family[..^suffix.Length].TrimEnd();
            }
        }

        return null;
    }

    private static readonly string[] WeightSuffixes = [" Light", " Semilight", " Semibold", " Medium", " Thin", " Black", " Heavy", " ExtraLight", " Extra Light"];


    public SolidColorBrush GetBrush(DocColor color)
    {
        uint argb = color.IsAuto ? 0xFF000000u : color.Argb;
        return _brushes.GetOrAdd(argb, static a =>
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a));
            brush.Freeze();
            return brush;
        });
    }

    public SolidColorBrush GetBrush(Color color) => GetBrush(new DocColor(((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));

    public SolidColorBrush? GetHighlightBrush(HighlightColor highlight)
    {
        DocColor? color = highlight switch
        {
            HighlightColor.Black => DocColor.FromRgb(0, 0, 0),
            HighlightColor.Blue => DocColor.FromRgb(0, 0, 255),
            HighlightColor.Cyan => DocColor.FromRgb(0, 255, 255),
            HighlightColor.Green => DocColor.FromRgb(0, 255, 0),
            HighlightColor.Magenta => DocColor.FromRgb(255, 0, 255),
            HighlightColor.Red => DocColor.FromRgb(255, 0, 0),
            HighlightColor.Yellow => DocColor.FromRgb(255, 255, 0),
            HighlightColor.White => DocColor.FromRgb(255, 255, 255),
            HighlightColor.DarkBlue => DocColor.FromRgb(0, 0, 128),
            HighlightColor.DarkCyan => DocColor.FromRgb(0, 128, 128),
            HighlightColor.DarkGreen => DocColor.FromRgb(0, 128, 0),
            HighlightColor.DarkMagenta => DocColor.FromRgb(128, 0, 128),
            HighlightColor.DarkRed => DocColor.FromRgb(128, 0, 0),
            HighlightColor.DarkYellow => DocColor.FromRgb(128, 128, 0),
            HighlightColor.DarkGray => DocColor.FromRgb(128, 128, 128),
            HighlightColor.LightGray => DocColor.FromRgb(192, 192, 192),
            _ => null,
        };
        return color is null ? null : GetBrush(color.Value);
    }
}
