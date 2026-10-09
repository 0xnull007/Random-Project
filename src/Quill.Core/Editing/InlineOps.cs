using System.Collections.Immutable;
using Quill.Core.Model;

namespace Quill.Core.Editing;

/// <summary>Pure helpers for slicing and normalizing inline sequences.</summary>
internal static class InlineOps
{
    public static int Length(ImmutableArray<Inline> inlines)
    {
        int length = 0;
        foreach (Inline inline in inlines)
        {
            length += inline.Length;
        }

        return length;
    }

    /// <summary>The inlines covering flat-text offsets [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public static ImmutableArray<Inline> Slice(Paragraph paragraph, int start, int end)
    {
        if (start < 0 || end > paragraph.Length || start > end)
        {
            throw new ArgumentOutOfRangeException(nameof(start), $"Slice [{start},{end}) is outside paragraph of length {paragraph.Length}.");
        }

        if (start == end)
        {
            return ImmutableArray<Inline>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<Inline>();
        foreach (InlineSpan span in paragraph.Spans())
        {
            if (span.End <= start)
            {
                continue;
            }

            if (span.Start >= end)
            {
                break;
            }

            if (span.Inline is Run run)
            {
                int from = Math.Max(start, span.Start) - span.Start;
                int to = Math.Min(end, span.End) - span.Start;
                builder.Add(from == 0 && to == run.Text.Length ? run : run.WithText(run.Text.Substring(from, to - from)));
            }
            else
            {
                builder.Add(span.Inline);
            }
        }

        return builder.ToImmutable();
    }

    public static (ImmutableArray<Inline> Head, ImmutableArray<Inline> Tail) Split(Paragraph paragraph, int offset) =>
        (Slice(paragraph, 0, offset), Slice(paragraph, offset, paragraph.Length));

    /// <summary>Drops empty runs and merges adjacent runs with identical formatting.</summary>
    public static ImmutableArray<Inline> Normalize(IEnumerable<Inline> inlines)
    {
        var builder = ImmutableArray.CreateBuilder<Inline>();
        foreach (Inline inline in inlines)
        {
            if (inline is Run { Text.Length: 0 })
            {
                continue;
            }

            if (builder.Count > 0 && builder[^1] is Run previous && inline is Run current && previous.HasSameFormatting(current))
            {
                builder[^1] = previous.WithText(previous.Text + current.Text);
            }
            else
            {
                builder.Add(inline);
            }
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<Inline> Concat(params ReadOnlySpan<ImmutableArray<Inline>> parts)
    {
        var all = new List<Inline>();
        foreach (ImmutableArray<Inline> part in parts)
        {
            all.AddRange(part);
        }

        return Normalize(all);
    }

    /// <summary>Applies <paramref name="transform"/> to the inlines in [<paramref name="start"/>, <paramref name="end"/>), splitting runs at the boundaries.</summary>
    public static ImmutableArray<Inline> Transform(Paragraph paragraph, int start, int end, Func<Inline, Inline> transform)
    {
        if (start >= end)
        {
            return paragraph.Inlines;
        }

        var result = new List<Inline>();
        foreach (InlineSpan span in paragraph.Spans())
        {
            if (span.End <= start || span.Start >= end)
            {
                result.Add(span.Inline);
                continue;
            }

            if (span.Inline is Run run)
            {
                int from = Math.Max(start, span.Start) - span.Start;
                int to = Math.Min(end, span.End) - span.Start;
                if (from > 0)
                {
                    result.Add(run.WithText(run.Text[..from]));
                }

                result.Add(transform(run.WithText(run.Text[from..to])));
                if (to < run.Text.Length)
                {
                    result.Add(run.WithText(run.Text[to..]));
                }
            }
            else
            {
                result.Add(transform(span.Inline));
            }
        }

        return Normalize(result);
    }
}
