using Quill.Core.Model;
using Quill.Layout;

namespace Quill.Layout.Tests;

/// <summary>
/// A deterministic monospace formatter: every character is 10 DIPs wide, lines are 20 DIPs tall with the
/// baseline at 16. Lines wrap after the last space that fits; break characters end lines; fields measure
/// as their substituted text; hidden text has no width. Tabs are ignored.
/// </summary>
internal sealed class FakeLineFormatter : ILineFormatter
{
    public const double CharWidth = 10;
    public const double LineHeight = 20;
    public const double Baseline = 16;

    public int Calls { get; private set; }

    public IReadOnlyList<IFormattedLine> FormatParagraph(ParagraphLayoutInput input)
    {
        Calls++;
        string text = input.Text;
        var widths = new double[text.Length];
        var breaks = new BreakKind?[text.Length];
        var isBreak = new bool[text.Length];
        foreach (RunSpan run in input.Runs)
        {
            for (int i = run.Start; i < run.End; i++)
            {
                switch (run.Kind)
                {
                    case RunKind.Text:
                        widths[i] = CharWidth;
                        break;
                    case RunKind.Field:
                        widths[i] = CharWidth * (run.FieldText?.Length ?? 0);
                        break;
                    case RunKind.Hidden:
                        widths[i] = 0;
                        break;
                    default:
                        isBreak[i] = true;
                        breaks[i] = run.Kind switch { RunKind.PageBreak => BreakKind.Page, RunKind.ColumnBreak => BreakKind.Column, _ => null };
                        break;
                }
            }
        }

        var lines = new List<IFormattedLine>();
        int pos = 0;
        bool first = true;
        while (true)
        {
            double maxWidth = input.LineWidth(first);
            int lineStart = pos;
            double width = 0;
            int lastSpace = -1;
            int end = -1;
            int newline = 0;
            BreakKind? forced = null;
            for (int i = pos; i < text.Length; i++)
            {
                if (isBreak[i])
                {
                    end = i + 1;
                    newline = 1;
                    forced = breaks[i];
                    break;
                }

                if (width + widths[i] > maxWidth + 1e-6 && i > lineStart)
                {
                    end = lastSpace >= 0 ? lastSpace + 1 : i;
                    break;
                }

                width += widths[i];
                if (text[i] == ' ')
                {
                    lastSpace = i;
                }
            }

            if (end < 0)
            {
                end = text.Length;
            }

            lines.Add(new FakeLine(lineStart, end - lineStart, newline, forced, widths));
            pos = end;
            first = false;
            if (end == text.Length)
            {
                if (newline == 1)
                {
                    lines.Add(new FakeLine(end, 0, 0, null, widths));
                }

                break;
            }
        }

        return lines;
    }

    internal sealed class FakeLine(int start, int length, int newlineLength, BreakKind? forcedBreakAfter, double[] widths) : IFormattedLine
    {
        public bool IsDisposed { get; private set; }

        public int Start { get; } = start;

        public int Length { get; } = length;

        public int NewlineLength { get; } = newlineLength;

        public BreakKind? ForcedBreakAfter { get; } = forcedBreakAfter;

        public double Height => LineHeight;

        public double Baseline => FakeLineFormatter.Baseline;

        public double Width => GetCaretX(Start + Length - NewlineLength);

        public double WidthIncludingTrailingWhitespace => Width;

        public double GetCaretX(int offset)
        {
            double x = 0;
            for (int i = Start; i < Math.Min(offset, Start + Length); i++)
            {
                x += widths[i];
            }

            return x;
        }

        public int HitTest(double x)
        {
            double acc = 0;
            int end = Start + Length - NewlineLength;
            for (int i = Start; i < end; i++)
            {
                if (x < acc + widths[i] / 2)
                {
                    return i;
                }

                acc += widths[i];
            }

            return end;
        }

        public IReadOnlyList<RectD> GetTextBounds(int rangeStart, int rangeLength)
        {
            double left = GetCaretX(rangeStart);
            double right = GetCaretX(rangeStart + rangeLength);
            return [new RectD(left, 0, right - left, LineHeight)];
        }

        public int NextCaretOffset(int offset) => Math.Min(offset + 1, Start + Length);

        public int PreviousCaretOffset(int offset) => Math.Max(offset - 1, Start);

        public int BackspaceCaretOffset(int offset) => PreviousCaretOffset(offset);

        public void Draw(IRenderTarget target, PointD origin)
        {
        }

        public IEnumerable<TextSegment> GetSegments() => [];

        public void Dispose() => IsDisposed = true;
    }
}
