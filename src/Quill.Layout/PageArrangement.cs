using System.Collections.Immutable;

namespace Quill.Layout;

/// <summary>
/// Where pages sit on the canvas: stacked in one column, or in rows of <c>columns</c> pages (Word's Multiple
/// Pages view). Grid cells are as wide as the widest page so rows line up; narrower pages are centred in their cell.
/// </summary>
public static class PageArrangement
{
    public readonly record struct Result(ImmutableArray<double> Tops, ImmutableArray<double> Lefts, double Width, double Height);

    public static Result Compute(IReadOnlyList<SizeD> pages, int columns, double padding, double gap)
    {
        ArgumentNullException.ThrowIfNull(pages);
        columns = Math.Max(1, columns);
        int count = pages.Count;
        double widest = 0;
        foreach (SizeD page in pages)
        {
            widest = Math.Max(widest, page.Width);
        }

        int across = Math.Max(1, Math.Min(columns, count));
        var tops = ImmutableArray.CreateBuilder<double>(count);
        var lefts = ImmutableArray.CreateBuilder<double>(count);
        double y = padding;
        for (int row = 0; row * columns < count; row++)
        {
            double rowHeight = 0;
            for (int column = 0; column < columns && row * columns + column < count; column++)
            {
                SizeD page = pages[row * columns + column];
                tops.Add(y);
                lefts.Add(padding + column * (widest + gap) + (widest - page.Width) / 2);
                rowHeight = Math.Max(rowHeight, page.Height);
            }

            y += rowHeight + gap;
        }

        double width = count == 0 ? 1 : 2 * padding + across * widest + (across - 1) * gap;
        double height = count == 0 ? 1 : y - gap + padding;
        return new Result(tops.ToImmutable(), lefts.ToImmutable(), Math.Max(1, width), Math.Max(1, height));
    }
}
