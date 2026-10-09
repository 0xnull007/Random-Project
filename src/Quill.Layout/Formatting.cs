using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Layout;

public enum FlowDirection
{
    LeftToRight,
    RightToLeft,
}

public enum RunKind
{
    Text,
    LineBreak,
    PageBreak,
    ColumnBreak,
    Field,
    Hidden,
}

/// <summary>A span of the paragraph's flat text with uniform resolved formatting.</summary>
public readonly record struct RunSpan(int Start, int Length, ResolvedRunProperties Properties, RunKind Kind, string? FieldText = null)
{
    public int End => Start + Length;

    public bool IsText => Kind == RunKind.Text;
}

/// <summary>Values substituted for fields while laying out one page.</summary>
public sealed record FieldValues(string Page, string NumPages, string SectionPages)
{
    public static readonly FieldValues Placeholder = new("1", "1", "1");

    public string Resolve(Field field) => field.Kind switch
    {
        FieldKind.Page => Page,
        FieldKind.NumPages => NumPages,
        FieldKind.SectionPages => SectionPages,
        _ => field.CachedResult,
    };

    public string Signature => string.Concat(Page, "|", NumPages, "|", SectionPages);
}

/// <summary>Everything a line formatter needs to break one paragraph into lines.</summary>
public sealed record ParagraphLayoutInput(
    Paragraph Paragraph,
    string Text,
    ImmutableArray<RunSpan> Runs,
    ResolvedRunProperties MarkProperties,
    ResolvedParagraphProperties Properties,
    double ColumnWidth,
    double DefaultTabStop,
    FlowDirection FlowDirection,
    double PixelsPerDip)
{
    public double LeftIndent => Properties.LeftIndent.ToDips();

    public double RightIndent => Properties.RightIndent.ToDips();

    public double FirstLineIndent => Properties.FirstLineIndent.ToDips();

    /// <summary>For list paragraphs: where the first line's text starts (after the marker), overriding the hanging indent.</summary>
    public double? FirstLineStart { get; init; }

    /// <summary>The list marker drawn in front of the paragraph, if any.</summary>
    public ListMarker? Marker { get; init; }

    /// <summary>Width available to a given line after indents; never below one DIP.</summary>
    public double LineWidth(bool firstLine) => Math.Max(1, ColumnWidth - LineStart(firstLine) - RightIndent);

    /// <summary>Horizontal start of a line relative to the column's left edge.</summary>
    public double LineStart(bool firstLine) =>
        firstLine && FirstLineStart is { } start ? start : LeftIndent + (firstLine ? FirstLineIndent : 0);

    /// <summary>Builds the input for a paragraph by resolving every inline against the style sheet.</summary>
    public static ParagraphLayoutInput Create(
        Paragraph paragraph,
        StyleResolver resolver,
        FieldValues fields,
        double columnWidth,
        Twips defaultTabStop,
        double pixelsPerDip,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(fields);

        var runs = ImmutableArray.CreateBuilder<RunSpan>(paragraph.Inlines.Length);
        foreach (InlineSpan span in paragraph.Spans())
        {
            ResolvedRunProperties properties = resolver.ResolveRun(paragraph, span.Inline);
            RunKind kind = span.Inline switch
            {
                Break { Kind: BreakKind.Line } => RunKind.LineBreak,
                Break { Kind: BreakKind.Page } => RunKind.PageBreak,
                Break => RunKind.ColumnBreak,
                Field => RunKind.Field,
                _ when properties.Hidden => RunKind.Hidden,
                _ => RunKind.Text,
            };
            string? fieldText = span.Inline is Field field ? fields.Resolve(field) : null;
            runs.Add(new RunSpan(span.Start, span.Length, properties, kind, fieldText));
        }

        return new ParagraphLayoutInput(
            paragraph,
            paragraph.FlatText,
            runs.ToImmutable(),
            resolver.ResolveParagraphMark(paragraph),
            resolver.ResolveParagraph(paragraph),
            columnWidth,
            defaultTabStop.ToDips(),
            flowDirection,
            pixelsPerDip);
    }

    public bool ContainsFields => Runs.Any(r => r.Kind == RunKind.Field);
}

/// <summary>A word-sized piece of a line with its position, for render targets that cannot draw glyph runs (PDF). Coordinates are relative to the line origin passed to <see cref="IFormattedLine.Draw"/>.</summary>
public readonly record struct TextSegment(string Text, ResolvedRunProperties Properties, double X, double Baseline, double Width, double Top, double Height);

/// <summary>Breaks paragraphs into lines. Implementations wrap a platform text engine (WPF TextFormatter) or a fake for tests.</summary>
public interface ILineFormatter
{
    IReadOnlyList<IFormattedLine> FormatParagraph(ParagraphLayoutInput input);
}

/// <summary>
/// One laid-out line. Offsets are into the paragraph's flat text; x coordinates are relative to the line origin
/// passed to <see cref="Draw"/> and already include alignment and indents.
/// </summary>
public interface IFormattedLine : IDisposable
{
    /// <summary>First flat-text offset on the line.</summary>
    int Start { get; }

    /// <summary>Number of flat-text characters on the line, including a trailing break character.</summary>
    int Length { get; }

    int End => Start + Length;

    /// <summary>Number of trailing characters that are a line/page/column break (0 or 1).</summary>
    int NewlineLength { get; }

    /// <summary>Set when the line ended because of an explicit page or column break.</summary>
    BreakKind? ForcedBreakAfter { get; }

    /// <summary>Natural height of the line from the font metrics, before paragraph line spacing is applied.</summary>
    double Height { get; }

    double Baseline { get; }

    /// <summary>Ink width excluding trailing whitespace.</summary>
    double Width { get; }

    double WidthIncludingTrailingWhitespace { get; }

    /// <summary>Caret x for a flat offset on this line.</summary>
    double GetCaretX(int offset);

    /// <summary>Nearest caret offset for an x coordinate.</summary>
    int HitTest(double x);

    IReadOnlyList<RectD> GetTextBounds(int start, int length);

    int NextCaretOffset(int offset);

    int PreviousCaretOffset(int offset);

    int BackspaceCaretOffset(int offset);

    /// <summary>Draws the line with its baseline-independent origin at <paramref name="origin"/> (top-left of the line box).</summary>
    void Draw(IRenderTarget target, PointD origin);

    /// <summary>The line's visible text split into words with layout positions, for non-WPF render targets.</summary>
    IEnumerable<TextSegment> GetSegments();
}

/// <summary>A minimal vector drawing surface. Text is drawn through <see cref="IFormattedLine.Draw"/>.</summary>
public interface IRenderTarget
{
    void FillRectangle(RectD rect, DocColor color, double opacity = 1);

    void DrawRectangle(RectD rect, DocColor color, double thickness);

    void DrawLine(PointD from, PointD to, DocColor color, double thickness);

    void PushClip(RectD rect);

    void Pop();

    /// <summary>Draws text segments produced by <see cref="IFormattedLine.GetSegments"/> at <paramref name="origin"/>.</summary>
    void DrawTextSegments(IEnumerable<TextSegment> segments, PointD origin);
}
