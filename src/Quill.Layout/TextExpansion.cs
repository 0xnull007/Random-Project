using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Quill.Layout;

/// <summary>
/// The text a platform formatter actually shapes, derived from a paragraph's flat text: fields are replaced by
/// their result text, all-caps runs are upper-cased and, when the paragraph hyphenates, soft hyphens (U+00AD)
/// mark the syllable breaks the formatter may use (it draws the hyphen only where it breaks). Keeps a two-way
/// map between flat offsets (the model) and layout offsets (the formatter): fields change lengths, soft hyphens
/// add zero-flat-length segments, every other run maps 1:1.
/// </summary>
public sealed class TextExpansion
{
    public const char SoftHyphen = '­';

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

            CultureInfo? upper = run.Kind == RunKind.Text && run.Properties.AllCaps ? SafeCulture(run.Properties.Language) : null;
            IReadOnlyList<int> breaks = run.Kind == RunKind.Text && input.Hyphenate && Hyphenation.Hyphenator.English.IsCultureSupported(SafeCulture(run.Properties.Language))
                ? Hyphenation.Hyphenator.English.TextBreaks(input.Text.AsSpan(run.Start, run.Length))
                : [];
            int from = 0;
            foreach (int at in breaks)
            {
                AppendPiece(builder, segments, input.Text, run, from, at, upper);
                segments.Add(new Segment(run.Start + at, builder.Length, 0, 1, run));
                builder.Append(SoftHyphen);
                from = at;
            }

            AppendPiece(builder, segments, input.Text, run, from, run.Length, upper);
        }

        return new TextExpansion(builder.ToString(), segments.ToImmutable(), input.Text.Length);
    }

    /// <summary>Appends run characters [start, end) (run-relative) as one 1:1 segment, upper-casing them for all-caps runs.</summary>
    private static void AppendPiece(StringBuilder builder, ImmutableArray<Segment>.Builder segments, string text, RunSpan run, int start, int end, CultureInfo? upper)
    {
        if (end <= start)
        {
            return;
        }

        int pieceStart = builder.Length;
        ReadOnlySpan<char> piece = text.AsSpan(run.Start + start, end - start);
        if (upper is { } culture)
        {
            foreach (char c in piece)
            {
                builder.Append(char.ToUpper(c, culture));
            }
        }
        else
        {
            builder.Append(piece);
        }

        segments.Add(new Segment(run.Start + start, pieceStart, end - start, end - start, run));
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
