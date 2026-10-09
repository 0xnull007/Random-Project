using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Layout;

/// <summary>
/// The lines of one paragraph at one width, with line-spacing rules applied. Cached per
/// (paragraph identity, width, field values) and shared by every page fragment of the paragraph.
/// </summary>
public sealed class ParagraphLayout : IDisposable
{
    private bool _disposed;

    internal ParagraphLayout(ParagraphLayoutInput input, IReadOnlyList<IFormattedLine> lines, string fieldSignature)
    {
        Input = input;
        Lines = lines;
        FieldSignature = fieldSignature;

        ResolvedParagraphProperties props = input.Properties;
        SpaceBefore = Math.Max(0, props.SpaceBefore.ToDips());
        SpaceAfter = Math.Max(0, props.SpaceAfter.ToDips());

        var tops = new double[lines.Count];
        var heights = new double[lines.Count];
        var baselines = new double[lines.Count];
        var starts = new double[lines.Count];
        double y = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            IFormattedLine line = lines[i];
            (double slot, double baselineOffset) = ApplyLineSpacing(props.LineSpacing, line.Height, line.Baseline);
            tops[i] = y;
            heights[i] = slot;
            baselines[i] = baselineOffset;
            starts[i] = input.LineStart(i == 0);
            y += slot;
        }

        LineTops = tops;
        LineHeights = heights;
        LineBaselines = baselines;
        LineStarts = starts;
        ContentHeight = y;
    }

    public ParagraphLayoutInput Input { get; }

    public Paragraph Paragraph => Input.Paragraph;

    public ResolvedParagraphProperties Properties => Input.Properties;

    public double Width => Input.ColumnWidth;

    public string FieldSignature { get; }

    public IReadOnlyList<IFormattedLine> Lines { get; }

    public int LineCount => Lines.Count;

    /// <summary>Top of each line box relative to the first line's top (space-before excluded).</summary>
    public IReadOnlyList<double> LineTops { get; }

    /// <summary>Height of each line box after line-spacing rules.</summary>
    public IReadOnlyList<double> LineHeights { get; }

    /// <summary>Where the natural line box sits inside its (possibly taller) slot.</summary>
    public IReadOnlyList<double> LineBaselines { get; }

    /// <summary>x of each line's origin relative to the column's left edge (indents applied).</summary>
    public IReadOnlyList<double> LineStarts { get; }

    public double SpaceBefore { get; }

    public double SpaceAfter { get; }

    /// <summary>Sum of all line slots.</summary>
    public double ContentHeight { get; }

    /// <summary>Height of lines [first, last].</summary>
    public double HeightOfLines(int first, int last)
    {
        if (last < first)
        {
            return 0;
        }

        return LineTops[last] + LineHeights[last] - LineTops[first];
    }

    /// <summary>Index of the line containing the flat offset (end-of-paragraph maps to the last line).</summary>
    public int LineIndexOf(int offset, bool upstream = false)
    {
        int last = Lines.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            IFormattedLine line = Lines[i];
            if (offset < line.End)
            {
                return i;
            }

            if (offset == line.End)
            {
                if (line.NewlineLength > 0)
                {
                    continue; // the caret after a break belongs to the next line
                }

                if (upstream || i == last)
                {
                    return i;
                }
            }
        }

        return last;
    }

    /// <summary>Converts the natural line height into a slot height under the paragraph's line spacing rule.</summary>
    internal static (double Slot, double BaselineOffset) ApplyLineSpacing(LineSpacing spacing, double naturalHeight, double naturalBaseline)
    {
        switch (spacing.Rule)
        {
            case LineSpacingRule.Exact:
            {
                double exact = Math.Max(1, spacing.Height.ToDips());
                // Word keeps the baseline near the bottom of an exact slot; text taller than the slot is clipped.
                return (exact, exact - (naturalHeight - naturalBaseline));
            }

            case LineSpacingRule.AtLeast:
            {
                double slot = Math.Max(naturalHeight, spacing.Height.ToDips());
                return (slot, naturalBaseline + (slot - naturalHeight));
            }

            default:
            {
                double factor = double.IsNaN(spacing.Factor) ? 1 : Math.Max(0.05, spacing.Factor);
                double slot = naturalHeight * factor;
                // Extra leading goes above the text, as in Word.
                return (slot, naturalBaseline + (slot - naturalHeight));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IFormattedLine line in Lines)
        {
            line.Dispose();
        }
    }
}
