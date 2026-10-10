using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Layout.Wpf;

/// <summary>Paints one laid-out page (body, header, footer, placeholders) onto a render target. Shared by the view, print and preview.</summary>
public static class PageRenderer
{
    private static readonly DocColor PlaceholderFill = DocColor.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly DocColor PlaceholderStroke = DocColor.FromRgb(0xA0, 0xA0, 0xA0);

    public static void DrawContent(PageLayout page, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        if (page.FootnoteSeparatorY is { } separator)
        {
            target.DrawLine(new PointD(page.BodyArea.Left, separator), new PointD(page.BodyArea.Left + Math.Min(144, page.BodyArea.Width), separator), DocColor.Black, 0.75);
        }

        foreach (BlockFragment fragment in page.AllFragments())
        {
            switch (fragment)
            {
                case ParagraphFragment paragraph:
                    DrawParagraph(paragraph, target);
                    break;
                case TableFragment table:
                    DrawTable(table, target);
                    break;
                case OpaqueFragment opaque:

                    target.FillRectangle(opaque.Bounds, PlaceholderFill);
                    target.DrawRectangle(opaque.Bounds, PlaceholderStroke, 1);
                    break;
            }
        }
    }

    public static void DrawTable(TableFragment fragment, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(target);
        for (int r = fragment.FirstRow; r <= fragment.LastRow; r++)
        {
            DrawRow(fragment.Layout, fragment.Layout.Rows[r], fragment.Bounds.Left, fragment.RowTop(r), target);
        }
    }

    private static void DrawRow(TableLayout layout, TableRowLayout row, double left, double top, IRenderTarget target)
    {
        foreach (TableCellLayout cell in row.Cells)
        {
            var rect = new RectD(left + cell.Left, top, cell.Width, row.Height);
            if (cell.Cell.Shading is { IsAuto: false } shading)
            {
                target.FillRectangle(rect, shading);
            }

            if (cell.Cell.VerticalMerge != VerticalMerge.Continue && !cell.Blocks.IsEmpty)
            {
                target.PushClip(rect);
                double contentLeft = rect.Left + TableLayout.CellPadding;
                foreach (CellBlockLayout block in cell.Blocks)
                {
                    if (block.Paragraph is { } paragraph)
                    {
                        DrawCellParagraph(paragraph, contentLeft, top + block.Top, target);
                    }
                    else if (block.Table is { } nested)
                    {
                        for (int r = 0; r < nested.Rows.Length; r++)
                        {
                            DrawRow(nested, nested.Rows[r], contentLeft + nested.Left, top + block.Top + nested.RowTops[r], target);
                        }
                    }
                }

                target.Pop();
            }

            if (layout.Table.HasBorders)
            {
                target.DrawRectangle(rect, DocColor.Black, TableLayout.BorderThickness);
            }
        }
    }

    private static void DrawCellParagraph(ParagraphLayout layout, double left, double top, IRenderTarget target)
    {
        double contentTop = top + layout.SpaceBefore;
        for (int i = 0; i < layout.LineCount; i++)
        {
            double naturalTop = contentTop + layout.LineTops[i] + (layout.LineBaselines[i] - layout.Lines[i].Baseline);
            if (i == 0 && layout.MarkerLine is { } marker)
            {
                marker.Draw(target, new PointD(left + layout.MarkerX, naturalTop + (layout.Lines[0].Baseline - marker.Baseline)));
            }

            layout.Lines[i].Draw(target, new PointD(left + layout.LineStarts[i], naturalTop));
        }
    }

    public static void DrawParagraph(ParagraphFragment fragment, IRenderTarget target)

    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(target);
        if (fragment.IsParagraphStart && fragment.Layout.MarkerLine is { } marker)
        {
            PointD first = fragment.LineOrigin(fragment.FirstLine);
            double y = first.Y + (fragment.Layout.Lines[fragment.FirstLine].Baseline - marker.Baseline);
            marker.Draw(target, new PointD(fragment.Bounds.Left + fragment.Layout.MarkerX, y));
        }

        for (int i = fragment.FirstLine; i <= fragment.LastLine; i++)
        {
            fragment.Layout.Lines[i].Draw(target, fragment.LineOrigin(i));
        }
    }
}
