using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Quill.Layout;

/// <summary>
/// The text a platform formatter actually shapes, derived from a paragraph's flat text: fields are replaced by
/// their result text and all-caps runs are upper-cased. Keeps a two-way map between flat offsets (the model)
/// and layout offsets (the formatter). Only fields change lengths; every other run maps 1:1.
/// </summary>
public sealed class TextExpansion
{
    private readonly ImmutableArray<Segment> _segments;

    private TextExpansion(string layoutText, ImmutableArray<Segment> segments, int flatLength)
    {
        LayoutText = layoutText;
        _segments = segments;
        FlatLength = flatLength;
    }

    public string LayoutText { get; }

    public int LayoutLength => LayoutText.Length;

    public int FlatLength { get; }

    public static TextExpansion Build(ParagraphLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var builder = new StringBuilder(input.Text.Length);
        var segments = ImmutableArray.CreateBuilder<Segment>(input.Runs.Length);
        foreach (RunSpan run in input.Runs)
        {
            int layoutStart = builder.Length;
            if (run.Kind == RunKind.Field)
            {
                string text = run.FieldText ?? string.Empty;
                builder.Append(text);
                segments.Add(new Segment(run.Start, layoutStart, run.Length, text.Length, run));
                continue;
            }

            ReadOnlySpan<char> slice = input.Text.AsSpan(run.Start, run.Length);
            if (run.Kind == RunKind.Text && run.Properties.AllCaps)
            {
                CultureInfo culture = SafeCulture(run.Properties.Language);
                foreach (char c in slice)
                {
                    builder.Append(char.ToUpper(c, culture));
                }
            }
            else
            {
                builder.Append(slice);
            }

            segments.Add(new Segment(run.Start, layoutStart, run.Length, run.Length, run));
        }

        return new TextExpansion(builder.ToString(), segments.ToImmutable(), input.Text.Length);
    }

    /// <summary>The run covering a layout offset, and the layout offset where that run ends.</summary>
    public (RunSpan Run, int SegmentEnd) RunAtLayout(int layoutOffset)
    {
        int index = FindByLayout(layoutOffset);
        Segment segment = _segments[index];
        return (segment.Run, segment.LayoutStart + segment.LayoutLength);
    }

    public int ToLayout(int flatOffset)
    {
        if (_segments.IsEmpty || flatOffset <= 0)
        {
            return 0;
        }

        if (flatOffset >= FlatLength)
        {
            return LayoutLength;
        }

        int index = FindByFlat(flatOffset);
        Segment segment = _segments[index];
        int delta = flatOffset - segment.FlatStart;
        if (segment.IsOneToOne)
        {
            return segment.LayoutStart + delta;
        }

        return delta == 0 ? segment.LayoutStart : segment.LayoutStart + segment.LayoutLength;
    }

    public int ToFlat(int layoutOffset)
    {
        if (_segments.IsEmpty || layoutOffset <= 0)
        {
            return 0;
        }

        if (layoutOffset >= LayoutLength)
        {
            return FlatLength;
        }

        int index = FindByLayout(layoutOffset);
        Segment segment = _segments[index];
        int delta = layoutOffset - segment.LayoutStart;
        if (segment.IsOneToOne)
        {
            return segment.FlatStart + delta;
        }

        // Inside a field's result text: snap to the nearer side of the field.
        return delta * 2 > segment.LayoutLength ? segment.FlatStart + segment.FlatLength : segment.FlatStart;
    }

    private int FindByFlat(int flatOffset)
    {
        int lo = 0;
        int hi = _segments.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_segments[mid].FlatStart <= flatOffset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private int FindByLayout(int layoutOffset)
    {
        int lo = 0;
        int hi = _segments.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_segments[mid].LayoutStart <= layoutOffset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Skip zero-length layout segments (empty field results) so a text run that starts here wins.
        while (lo + 1 < _segments.Length && _segments[lo].LayoutLength == 0 && _segments[lo + 1].LayoutStart <= layoutOffset)
        {
            lo++;
        }

        return lo;
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

    private readonly record struct Segment(int FlatStart, int LayoutStart, int FlatLength, int LayoutLength, RunSpan Run)
    {
        public bool IsOneToOne => FlatLength == LayoutLength && Run.Kind != RunKind.Field;
    }
}
