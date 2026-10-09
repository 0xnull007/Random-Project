using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Quill.Layout.Wpf;

/// <summary>Breaks paragraphs into lines with WPF's <see cref="TextFormatter"/> in ideal (resolution-independent) mode.</summary>
public sealed class WpfLineFormatter : ILineFormatter
{
    [ThreadStatic]
    private static TextFormatter? s_formatter;

    private readonly FontCatalog _fonts;

    public WpfLineFormatter(FontCatalog? fonts = null)
    {
        _fonts = fonts ?? FontCatalog.Shared;
    }

    private static TextFormatter Formatter => s_formatter ??= TextFormatter.Create(TextFormattingMode.Ideal);

    public IReadOnlyList<IFormattedLine> FormatParagraph(ParagraphLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        TextExpansion expansion = TextExpansion.Build(input);
        var source = new ParagraphTextSource(input, expansion, _fonts);
        var firstLineProps = new QuillTextParagraphProperties(input, firstLine: true, source.MarkProperties);
        var otherLineProps = new QuillTextParagraphProperties(input, firstLine: false, source.MarkProperties);

        var lines = new List<IFormattedLine>();
        int position = 0;
        bool first = true;
        TextLineBreak? previousBreak = null;
        TextFormatter formatter = Formatter;
        while (true)
        {
            double width = input.LineWidth(first);
            TextLine line = formatter.FormatLine(source, position, width, first ? firstLineProps : otherLineProps, previousBreak);
            int end = position + line.Length;
            RunSpan? breakRun = null;
            if (end <= expansion.LayoutLength && line.Length > 0)
            {
                (RunSpan run, _) = expansion.RunAtLayout(end - 1);
                if (run.Kind is RunKind.LineBreak or RunKind.PageBreak or RunKind.ColumnBreak)
                {
                    breakRun = run;
                }
            }

            lines.Add(new WpfFormattedLine(line, position, expansion, breakRun));
            position = end;
            first = false;
            if (position > expansion.LayoutLength || line.Length == 0)
            {
                break; // the end-of-paragraph marker has been consumed
            }

            previousBreak = line.GetTextLineBreak();
        }

        return lines;
    }
}
