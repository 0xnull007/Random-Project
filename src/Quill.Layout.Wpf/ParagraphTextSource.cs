using System.Globalization;
using System.Windows.Media.TextFormatting;
using Quill.Core.Styles;

namespace Quill.Layout.Wpf;

/// <summary>Feeds a paragraph's expanded text to WPF's TextFormatter, one run at a time.</summary>
internal sealed class ParagraphTextSource : TextSource
{
    private readonly TextExpansion _expansion;
    private readonly Dictionary<ResolvedRunProperties, QuillTextRunProperties> _runProperties = new();
    private readonly double _pixelsPerDip;
    private readonly FontCatalog _fonts;

    public ParagraphTextSource(ParagraphLayoutInput input, TextExpansion expansion, FontCatalog fonts)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expansion);
        ArgumentNullException.ThrowIfNull(fonts);
        _expansion = expansion;
        _fonts = fonts;
        _pixelsPerDip = input.PixelsPerDip;
        MarkProperties = Get(input.MarkProperties);
        PixelsPerDip = input.PixelsPerDip;
    }

    public QuillTextRunProperties MarkProperties { get; }

    public string Text => _expansion.LayoutText;

    public override TextRun GetTextRun(int textSourceCharacterIndex)
    {
        int length = _expansion.LayoutLength;
        if (textSourceCharacterIndex >= length)
        {
            return new TextEndOfParagraph(1, MarkProperties);
        }

        (RunSpan run, int segmentEnd) = _expansion.RunAtLayout(textSourceCharacterIndex);
        int remaining = Math.Max(1, segmentEnd - textSourceCharacterIndex);
        QuillTextRunProperties properties = Get(run.Properties);
        return run.Kind switch
        {
            RunKind.LineBreak or RunKind.PageBreak or RunKind.ColumnBreak => new TextEndOfLine(1, properties),
            RunKind.Hidden => new TextHidden(remaining),
            RunKind.Image => new ImageEmbeddedObject(run, properties),
            _ => new TextCharacters(Text, textSourceCharacterIndex, remaining, properties),
        };
    }

    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
    {
        int limit = Math.Clamp(textSourceCharacterIndexLimit, 0, _expansion.LayoutLength);
        var range = new CharacterBufferRange(Text, 0, limit);
        return new TextSpan<CultureSpecificCharacterBufferRange>(limit, new CultureSpecificCharacterBufferRange(CultureInfo.CurrentCulture, range));
    }

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) => textSourceCharacterIndex;

    private QuillTextRunProperties Get(ResolvedRunProperties properties)
    {
        if (!_runProperties.TryGetValue(properties, out QuillTextRunProperties? result))
        {
            result = new QuillTextRunProperties(properties, _pixelsPerDip, _fonts);
            _runProperties[properties] = result;
        }

        return result;
    }
}
