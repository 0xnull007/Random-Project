using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Quill.Core.Model;

namespace Quill.Layout.Wpf;

/// <summary>Wraps a WPF <see cref="TextLine"/> and translates between flat (model) and layout (formatter) offsets.</summary>
public sealed class WpfFormattedLine : IFormattedLine
{
    private readonly TextLine _line;
    private readonly TextExpansion _expansion;
    private readonly int _layoutStart;
    private readonly int _layoutEnd; // exclusive, without the end-of-paragraph marker
    private bool _disposed;

    internal WpfFormattedLine(TextLine line, int layoutStart, TextExpansion expansion, RunSpan? breakRun)
    {
        _line = line;
        _expansion = expansion;
        _layoutStart = layoutStart;
        int rawEnd = layoutStart + line.Length;
        bool hasParagraphEnd = rawEnd > expansion.LayoutLength;
        _layoutEnd = hasParagraphEnd ? expansion.LayoutLength : rawEnd;

        Start = expansion.ToFlat(layoutStart);
        Length = expansion.ToFlat(_layoutEnd) - Start;
        NewlineLength = breakRun is null ? 0 : 1;
        ForcedBreakAfter = breakRun?.Kind switch
        {
            RunKind.PageBreak => BreakKind.Page,
            RunKind.ColumnBreak => BreakKind.Column,
            _ => null,
        };
    }

    public TextLine TextLine => _line;

    public int Start { get; }

    public int Length { get; }

    public int NewlineLength { get; }

    public BreakKind? ForcedBreakAfter { get; }

    public double Height => _line.Height;

    public double Baseline => _line.Baseline;

    public double Width => _line.Width;

    public double WidthIncludingTrailingWhitespace => _line.WidthIncludingTrailingWhitespace;

    public double GetCaretX(int offset)
    {
        int layout = ClampLayout(_expansion.ToLayout(offset));
        return _line.GetDistanceFromCharacterHit(new CharacterHit(layout, 0));
    }

    public int HitTest(double x)
    {
        CharacterHit hit = _line.GetCharacterHitFromDistance(x);
        int layout = ClampLayout(hit.FirstCharacterIndex + hit.TrailingLength);
        return _expansion.ToFlat(layout);
    }

    public IReadOnlyList<RectD> GetTextBounds(int start, int length)
    {
        int layoutStart = ClampLayout(_expansion.ToLayout(start));
        int layoutEnd = ClampLayout(_expansion.ToLayout(start + length));
        if (layoutEnd <= layoutStart)
        {
            return [];
        }

        IList<TextBounds> bounds = _line.GetTextBounds(layoutStart, layoutEnd - layoutStart);
        var result = new List<RectD>(bounds.Count);
        foreach (TextBounds b in bounds)
        {
            Rect r = b.Rectangle;
            result.Add(new RectD(r.X, r.Y, r.Width, r.Height));
        }

        return result;
    }

    public int NextCaretOffset(int offset)
    {
        int layout = ClampLayout(_expansion.ToLayout(offset));
        CharacterHit next = _line.GetNextCaretCharacterHit(new CharacterHit(layout, 0));
        int target = ClampLayout(next.FirstCharacterIndex + next.TrailingLength);
        int flat = _expansion.ToFlat(target);
        return flat > offset ? flat : Math.Min(offset + 1, Start + Length);
    }

    public int PreviousCaretOffset(int offset)
    {
        int layout = ClampLayout(_expansion.ToLayout(offset));
        CharacterHit previous = _line.GetPreviousCaretCharacterHit(new CharacterHit(layout, 0));
        int target = ClampLayout(previous.FirstCharacterIndex + previous.TrailingLength);
        int flat = _expansion.ToFlat(target);
        return flat < offset ? flat : Math.Max(offset - 1, Start);
    }

    public int BackspaceCaretOffset(int offset)
    {
        int layout = ClampLayout(_expansion.ToLayout(offset));
        CharacterHit previous = _line.GetBackspaceCaretCharacterHit(new CharacterHit(layout, 0));
        int target = ClampLayout(previous.FirstCharacterIndex + previous.TrailingLength);
        int flat = _expansion.ToFlat(target);
        return flat < offset ? flat : Math.Max(offset - 1, Start);
    }

    public void Draw(IRenderTarget target, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is WpfRenderTarget wpf)
        {
            _line.Draw(wpf.Context, new Point(origin.X, origin.Y), InvertAxes.None);
            return;
        }

        target.DrawTextSegments(GetSegments(), origin);
        foreach (ImagePlacement placement in GetImages())
        {
            if (placement.Data is { } data)
            {
                RectD b = placement.Bounds;
                target.DrawImage(data, new RectD(origin.X + b.X, origin.Y + b.Y, b.Width, b.Height));
            }
        }
    }

    /// <summary>Pictures on the line, positioned where WPF placed them (boxes stand on the baseline).</summary>
    public IEnumerable<ImagePlacement> GetImages()
    {
        int position = _layoutStart;
        foreach (TextSpan<TextRun> span in _line.GetTextRunSpans())
        {
            if (span.Value is ImageEmbeddedObject image && image.Run.Image is { } inline)
            {
                double x = _line.GetDistanceFromCharacterHit(new CharacterHit(position, 0));
                yield return new ImagePlacement(inline, image.Run.ImageData, new RectD(x, _line.Baseline - image.Height, image.Width, image.Height));
            }

            position += span.Length;
        }
    }


    /// <summary>
    /// Words with their exact x positions as WPF placed them, so a PDF target reproduces wrapping, justification
    /// and tab stops; only the advances inside a word come from the target's own font metrics.
    /// </summary>
    public IEnumerable<TextSegment> GetSegments()
    {
        string text = _expansion.LayoutText;
        int position = _layoutStart;
        foreach (TextSpan<TextRun> span in _line.GetTextRunSpans())
        {
            int length = span.Length;
            if (span.Value is TextCharacters characters && characters.Properties is QuillTextRunProperties props)
            {
                int end = Math.Min(position + length, _layoutEnd);
                int i = position;
                while (i < end)
                {
                    // A segment is a word plus the spaces after it. Tabs and other whitespace end the drawn text but
                    // still count towards the segment's width, so highlights form one continuous band like on screen.
                    int start = i;
                    while (i < end && !char.IsWhiteSpace(text[i]))
                    {
                        i++;
                    }

                    while (i < end && text[i] is ' ' or ' ')
                    {
                        i++;
                    }

                    int textEnd = i;
                    while (i < end && char.IsWhiteSpace(text[i]))
                    {
                        i++;
                    }

                    int reach = i;
                    if (reach == start)
                    {
                        i++;
                        continue;
                    }

                    double x = _line.GetDistanceFromCharacterHit(new CharacterHit(start, 0));
                    double right = _line.GetDistanceFromCharacterHit(new CharacterHit(reach, 0));
                    IList<TextBounds> bounds = _line.GetTextBounds(start, reach - start);
                    Rect box = bounds.Count > 0 ? bounds[0].Rectangle : new Rect(x, 0, Math.Max(0, right - x), _line.Height);
                    yield return new TextSegment(text.Substring(start, textEnd - start), props.Properties, x, _line.Baseline, Math.Max(0, right - x), box.Y, box.Height);
                }
            }

            position += length;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _line.Dispose();
        }
    }

    private int ClampLayout(int layoutOffset) => Math.Clamp(layoutOffset, _layoutStart, _layoutEnd);
}
