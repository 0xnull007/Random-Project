using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
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
    Image,
}

/// <summary>A span of the paragraph's flat text with uniform resolved formatting.</summary>
public readonly record struct RunSpan(int Start, int Length, ResolvedRunProperties Properties, RunKind Kind, string? FieldText = null, InlineImage? Image = null, ImageData? ImageData = null)
{
    public int End => Start + Length;

    public bool IsText => Kind == RunKind.Text;
}

/// <summary>Where an inline picture landed on a line, relative to the line origin.</summary>
public readonly record struct ImagePlacement(InlineImage Image, ImageData? Data, RectD Bounds);

/// <summary>
/// A rectangle text must flow around (a floating picture plus its distance from text), relative to the column's
/// left edge and the paragraph's content top. <paramref name="PushBelow"/> moves lines under it instead of beside it.
/// </summary>
public readonly record struct FlowObstacle(RectD Bounds, bool PushBelow)
{
    public string Signature => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Bounds.X:0.#},{Bounds.Y:0.#},{Bounds.Width:0.#},{Bounds.Height:0.#},{(PushBelow ? 1 : 0)}");
}

/// <summary>Where one line goes: its top (relative to the paragraph's content top) and its horizontal extent (relative to the column's left edge).</summary>
public readonly record struct LineSlot(double Top, double Start, double Width);

/// <summary>A floating picture placed on a page: its anchor character and its page rectangle.</summary>
/// <param name="Reference">The page point its anchor offsets are measured from, so a drag can be turned back into offsets.</param>
public sealed record FloatingImage(StoryId Story, BlockPath Path, int Offset, InlineImage Image, ImageData? Data, RectD Bounds, PointD Reference)
{
    public ImageAnchor Anchor => Image.Anchor ?? ImageAnchor.Default;

    public TextPosition Position => new(Story, Path, Offset);
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
        FieldKind.Seq => SequenceFields.Text(field, SequenceNumbers),
        FieldKind.PageRef => BookmarkSupport.Target(field) is { } target && PageRefs is not null && PageRefs.TryGetValue(target, out string? page) ? page : field.CachedResult,
        FieldKind.Date or FieldKind.Time => FieldSupport.FormatDate(field, Now ?? DateTime.Now),
        FieldKind.DocProperty => Metadata is { } metadata ? FieldSupport.DocumentPropertyText(field, metadata) ?? field.CachedResult : field.CachedResult,
        _ => field.CachedResult,
    };

    public string Signature => string.Concat(Page, "|", NumPages, "|", SectionPages);

    /// <summary>Displayed number per footnote id (reference order); null outside a document layout.</summary>
    public IReadOnlyDictionary<int, int>? FootnoteNumbers { get; init; }

    public IReadOnlyDictionary<int, int>? EndnoteNumbers { get; init; }

    /// <summary>Running numbers of SEQ (caption) fields by field instance; null outside a document layout.</summary>
    public IReadOnlyDictionary<Field, int>? SequenceNumbers { get; init; }

    /// <summary>Page number text per bookmark, for PAGEREF fields; filled from a previous layout pass.</summary>
    public IReadOnlyDictionary<string, string>? PageRefs { get; init; }

    /// <summary>The moment DATE and TIME fields show (one value per layout so every page agrees).</summary>
    public DateTime? Now { get; init; }

    /// <summary>Document properties for AUTHOR, TITLE... fields.</summary>
    public DocumentMetadata? Metadata { get; init; }

    public string EndnoteNumber(int noteId) =>
        EndnoteNumbers is not null && EndnoteNumbers.TryGetValue(noteId, out int number) ? NumberText.Format(number, NumberFormat.LowerRoman) : "?";

    public string NoteNumber(FootnoteReference note) => note.IsEndnote ? EndnoteNumber(note.NoteId) : FootnoteNumber(note.NoteId);

    public string FootnoteNumber(int noteId) =>
        FootnoteNumbers is not null && FootnoteNumbers.TryGetValue(noteId, out int number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
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

    /// <summary>Floating pictures the lines must flow around, relative to the column's left edge and the content top.</summary>
    public ImmutableArray<FlowObstacle> Obstacles { get; init; } = ImmutableArray<FlowObstacle>.Empty;

    /// <summary>Width available to a given line after indents; never below one DIP.</summary>
    public double LineWidth(bool firstLine) => Math.Max(1, ColumnWidth - LineStart(firstLine) - RightIndent);

    /// <summary>Horizontal start of a line relative to the column's left edge.</summary>
    public double LineStart(bool firstLine) =>
        firstLine && FirstLineStart is { } start ? start : LeftIndent + (firstLine ? FirstLineIndent : 0);

    /// <summary>Leftover room beside a picture narrower than this pushes the line below the picture instead.</summary>
    public const double MinimumRoomBesideObstacle = 36;

    /// <summary>
    /// Where a line whose top would be at <paramref name="top"/> actually goes: beside a square-wrapped picture
    /// (on the side with more room), or below a picture that leaves no room or wraps top-and-bottom. Both the
    /// formatter and <see cref="ParagraphLayout"/> call this, so a line's height is judged by the paragraph
    /// mark's font rather than by the line itself, which is not known until it has been formatted.
    /// </summary>
    public LineSlot PlaceLine(bool firstLine, double top)
    {
        double start = LineStart(firstLine);
        double right = ColumnWidth - RightIndent;
        if (Obstacles.IsDefaultOrEmpty)
        {
            return new LineSlot(top, start, Math.Max(1, right - start));
        }

        double probe = Math.Max(1, MarkProperties.FontSize.ToDips() * 1.2);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double lineLeft = start;
            double lineRight = right;
            bool pushed = false;
            foreach (FlowObstacle obstacle in Obstacles)
            {
                RectD box = obstacle.Bounds;
                if (box.Bottom <= top || box.Top >= top + probe || box.Right <= lineLeft || box.Left >= lineRight)
                {
                    continue;
                }

                double roomLeft = box.Left - lineLeft;
                double roomRight = lineRight - box.Right;
                if (!obstacle.PushBelow && Math.Max(roomLeft, roomRight) >= MinimumRoomBesideObstacle)
                {
                    if (roomLeft >= roomRight)
                    {
                        lineRight = box.Left;
                    }
                    else
                    {
                        lineLeft = box.Right;
                    }
                }
                else
                {
                    top = box.Bottom;
                    pushed = true;
                    break;
                }
            }

            if (!pushed)
            {
                return new LineSlot(top, lineLeft, Math.Max(1, lineRight - lineLeft));
            }
        }

        return new LineSlot(top, start, Math.Max(1, right - start));
    }

    /// <summary>Builds the input for a paragraph by resolving every inline against the style sheet.</summary>
    public static ParagraphLayoutInput Create(
        Paragraph paragraph,
        StyleResolver resolver,
        FieldValues fields,
        double columnWidth,
        Twips defaultTabStop,
        double pixelsPerDip,
        FlowDirection flowDirection = FlowDirection.LeftToRight,
        ImageStore? images = null,
        bool floatsInline = false)
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
                FootnoteReference => RunKind.Field,
                _ when properties.Hidden => RunKind.Hidden,
                InlineImage => RunKind.Image,
                _ => RunKind.Text,
            };
            string? fieldText = span.Inline switch
            {
                Field field => fields.Resolve(field),
                FootnoteReference note => fields.NoteNumber(note),
                _ => null,
            };
            InlineImage? image = span.Inline as InlineImage;
            if (floatsInline && image is { IsFloating: true })
            {
                image = image.WithAnchor(null); // table cells do not place floats; the picture shows in the line instead
            }

            runs.Add(new RunSpan(span.Start, span.Length, properties, kind, fieldText, image, image is null ? null : images?.Get(image.ImageId)));

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

    /// <summary>Inline pictures on this line with their boxes, for non-WPF render targets.</summary>
    IEnumerable<ImagePlacement> GetImages();
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

    /// <summary>Draws an encoded picture into <paramref name="bounds"/>.</summary>
    void DrawImage(ImageData image, RectD bounds);

    /// <summary>Diagonal translucent text across the page (a watermark); targets that cannot rotate text may ignore it.</summary>
    void DrawWatermark(string text, DocColor color, double opacity, SizeD page)
    {
    }

    /// <summary>A small right-aligned label whose right end sits at <paramref name="baselineRight"/> (line numbers).</summary>
    void DrawLabel(string text, PointD baselineRight, double fontSize, DocColor color)
    {
    }
}
