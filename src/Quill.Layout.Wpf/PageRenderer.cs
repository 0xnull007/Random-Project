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
        if (page.Borders is { } borders && page.BorderRect() is { } borderRect)
        {
            DrawPageBorder(borderRect, borders, target);
        }


        foreach (double? separator in new[] { page.FootnoteSeparatorY, page.EndnoteSeparatorY })
        {
            if (separator is { } y)
            {
                target.DrawLine(new PointD(page.BodyArea.Left, y), new PointD(page.BodyArea.Left + Math.Min(144, page.BodyArea.Width), y), DocColor.Black, 0.75);
            }
        }

        foreach (BlockFragment fragment in page.AllFragments())
        {
            switch (fragment)
            {
                case ParagraphFragment paragraph:
                    if (paragraph.Path.IsTopLevel)
                    {
                        DrawParagraph(paragraph, target); // cell paragraphs are drawn by their table
                    }

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

    /// <summary>Word's page border: single, double (two lines a gap apart), dotted, dashed or thick, in the chosen colour.</summary>
    public static void DrawPageBorder(RectD rect, PageBorders borders, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(borders);
        ArgumentNullException.ThrowIfNull(target);
        double thickness = borders.ThicknessDips;
        switch (borders.Style)
        {
            case PageBorderStyle.Double:
                target.DrawRectangle(rect, borders.Color, thickness);
                double inset = thickness * 2.5;
                target.DrawRectangle(RectD.FromEdges(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset), borders.Color, thickness);
                break;
            case PageBorderStyle.Dotted:
                DrawDashedRectangle(rect, borders.Color, thickness, dash: thickness, gap: thickness * 1.5, target);
                break;
            case PageBorderStyle.Dashed:
                DrawDashedRectangle(rect, borders.Color, thickness, dash: thickness * 4, gap: thickness * 2, target);
                break;
            default:
                target.DrawRectangle(rect, borders.Color, thickness);
                break;
        }
    }

    private static void DrawDashedRectangle(RectD rect, DocColor color, double thickness, double dash, double gap, IRenderTarget target)
    {
        double half = thickness / 2;
        DrawDashedLine(new PointD(rect.Left, rect.Top + half), new PointD(rect.Right, rect.Top + half), color, thickness, dash, gap, target);
        DrawDashedLine(new PointD(rect.Left, rect.Bottom - half), new PointD(rect.Right, rect.Bottom - half), color, thickness, dash, gap, target);
        DrawDashedLine(new PointD(rect.Left + half, rect.Top), new PointD(rect.Left + half, rect.Bottom), color, thickness, dash, gap, target);
        DrawDashedLine(new PointD(rect.Right - half, rect.Top), new PointD(rect.Right - half, rect.Bottom), color, thickness, dash, gap, target);
    }

    private static void DrawDashedLine(PointD from, PointD to, DocColor color, double thickness, double dash, double gap, IRenderTarget target)
    {
        double length = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        if (length <= 0 || dash <= 0)
        {
            return;
        }

        double ux = (to.X - from.X) / length;
        double uy = (to.Y - from.Y) / length;
        for (double at = 0; at < length; at += dash + gap)
        {
            double end = Math.Min(length, at + dash);
            target.DrawLine(new PointD(from.X + ux * at, from.Y + uy * at), new PointD(from.X + ux * end, from.Y + uy * end), color, thickness);
        }
    }

    public static void DrawTable(TableFragment fragment, IRenderTarget target)

    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(target);
        for (int r = 0; r < fragment.RepeatedHeaderRows; r++)
        {
            DrawRow(fragment.Layout, r, fragment.Bounds.Left, fragment.RowTop(r), target);
        }

        for (int r = fragment.FirstRow; r <= fragment.LastRow; r++)
        {
            DrawRow(fragment.Layout, r, fragment.Bounds.Left, fragment.RowTop(r), target);
        }
    }

    private static void DrawRow(TableLayout layout, int rowIndex, double left, double top, IRenderTarget target)
    {
        TableRowLayout row = layout.Rows[rowIndex];
        foreach (TableCellLayout cell in row.Cells)
        {
            if (cell.Cell.VerticalMerge == VerticalMerge.Continue)
            {
                continue; // drawn by the cell that starts the merge
            }

            double height = cell.MergedRows > 1
                ? layout.RowTops[Math.Min(rowIndex + cell.MergedRows, layout.RowTops.Length - 1)] - layout.RowTops[rowIndex]
                : row.Height;
            var rect = new RectD(left + cell.Left, top, cell.Width, height);
            if (cell.Cell.Shading is { IsAuto: false } shading)
            {
                target.FillRectangle(rect, shading);
            }

            if (!cell.Blocks.IsEmpty)
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
                            DrawRow(nested, r, contentLeft + nested.Left, top + block.Top + nested.RowTops[r], target);
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
