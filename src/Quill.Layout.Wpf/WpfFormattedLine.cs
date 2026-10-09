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
        if (target is not WpfRenderTarget wpf)
        {
            throw new ArgumentException("WPF lines can only be drawn to a WpfRenderTarget.", nameof(target));
        }

        _line.Draw(wpf.Context, new Point(origin.X, origin.Y), InvertAxes.None);
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
