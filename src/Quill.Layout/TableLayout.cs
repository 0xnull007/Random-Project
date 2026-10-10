using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Layout;

/// <summary>A table laid out at one width: column positions, row heights and the paragraphs inside each cell.</summary>
public sealed class TableLayout
{
    /// <summary>Word's default left/right cell margin (0.075 in).</summary>
    public const double CellPadding = 7.2;

    /// <summary>0.5 pt single borders.</summary>
    public const double BorderThickness = 2.0 / 3.0;

    public TableLayout(Table table, double left, ImmutableArray<double> columnWidths, ImmutableArray<TableRowLayout> rows, double scale = 1)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        Left = left;
        ColumnWidths = columnWidths;
        Rows = rows;
        Scale = scale;
        var tops = new double[rows.Length + 1];
        for (int i = 0; i < rows.Length; i++)
        {
            tops[i + 1] = tops[i] + rows[i].Height;
        }

        RowTops = ImmutableArray.Create(tops);
    }

    public Table Table { get; }

    /// <summary>Indent from the column's left edge.</summary>
    public double Left { get; }

    public ImmutableArray<double> ColumnWidths { get; }

    /// <summary>Ratio of laid-out widths to the model widths: below 1 when a grid wider than the column was shrunk to fit.</summary>
    public double Scale { get; }

    public double Width => ColumnWidths.Sum();

    public ImmutableArray<TableRowLayout> Rows { get; }

    /// <summary>Top of each row relative to the table's top; the last entry is the table height.</summary>
    public ImmutableArray<double> RowTops { get; }

    public double Height => RowTops[^1];

    public double HeightOfRows(int first, int last) => last < first ? 0 : RowTops[last + 1] - RowTops[first];
}

public sealed class TableRowLayout
{
    public TableRowLayout(TableRow row, double height, ImmutableArray<TableCellLayout> cells)
    {
        Row = row;
        Height = height;
        Cells = cells;
    }

    public TableRow Row { get; }

    public double Height { get; }

    public ImmutableArray<TableCellLayout> Cells { get; }
}

public sealed class TableCellLayout
{
    public TableCellLayout(TableCell cell, double left, double width, ImmutableArray<CellBlockLayout> blocks, double contentHeight)
    {
        Cell = cell;
        Left = left;
        Width = width;
        Blocks = blocks;
        ContentHeight = contentHeight;
    }

    public TableCell Cell { get; }

    /// <summary>Left edge relative to the table's left edge.</summary>
    public double Left { get; }

    public double Width { get; }

    public ImmutableArray<CellBlockLayout> Blocks { get; }

    public double ContentHeight { get; }

    /// <summary>Rows this cell spans when it starts a vertical merge (1 otherwise); set once the whole table is built.</summary>
    public int MergedRows { get; internal set; } = 1;
}

/// <summary>One block inside a cell: a paragraph layout or a nested table, with its top relative to the cell's top.</summary>
public sealed class CellBlockLayout
{
    public CellBlockLayout(ParagraphLayout? paragraph, TableLayout? table, double top, double height)
    {
        Paragraph = paragraph;
        Table = table;
        Top = top;
        Height = height;
    }

    public ParagraphLayout? Paragraph { get; }

    public TableLayout? Table { get; }

    public double Top { get; }

    public double Height { get; }
}

/// <summary>
/// Rows [FirstRow, LastRow] of a table placed on a page, with a fragment for every paragraph inside its cells.
/// When the table continues from an earlier page its leading header rows are repeated above the body rows.
/// </summary>
public sealed class TableFragment : BlockFragment
{
    public TableFragment(StoryId story, BlockPath path, TableLayout layout, int firstRow, int lastRow, double top, double columnLeft, int repeatedHeaderRows = 0)
        : base(story, path, new RectD(columnLeft + layout.Left, top, layout.Width, layout.RowTops[Math.Min(repeatedHeaderRows, firstRow)] + layout.HeightOfRows(firstRow, lastRow)))
    {
        Layout = layout;
        FirstRow = firstRow;
        LastRow = lastRow;
        RepeatedHeaderRows = Math.Min(repeatedHeaderRows, firstRow);

        var paragraphs = ImmutableArray.CreateBuilder<ParagraphFragment>();
        var tables = ImmutableArray.CreateBuilder<TableFragment>();
        for (int r = firstRow; r <= lastRow; r++)
        {
            TableRowLayout row = layout.Rows[r];
            double rowTop = RowTop(r);
            for (int c = 0; c < row.Cells.Length; c++)
            {
                TableCellLayout cell = row.Cells[c];
                if (cell.Cell.VerticalMerge == VerticalMerge.Continue)
                {
                    continue;
                }

                double contentLeft = Bounds.Left + cell.Left + TableLayout.CellPadding;
                for (int b = 0; b < cell.Blocks.Length; b++)
                {
                    CellBlockLayout block = cell.Blocks[b];
                    BlockPath childPath = path.Child(r, c, b);
                    if (block.Paragraph is { } paragraph)
                    {
                        paragraphs.Add(new ParagraphFragment(story, childPath, paragraph, 0, paragraph.LineCount - 1, rowTop + block.Top, contentLeft, paragraph.SpaceBefore, paragraph.SpaceAfter));
                    }
                    else if (block.Table is { } nested && nested.Rows.Length > 0)
                    {
                        tables.Add(new TableFragment(story, childPath, nested, 0, nested.Rows.Length - 1, rowTop + block.Top, contentLeft));
                    }
                }
            }
        }

        CellParagraphs = paragraphs.ToImmutable();
        NestedTables = tables.ToImmutable();
    }

    public TableLayout Layout { get; }

    public int FirstRow { get; }

    public int LastRow { get; }

    /// <summary>Leading header rows drawn again above <see cref="FirstRow"/> on a continuation page (0 on the first page).</summary>
    public int RepeatedHeaderRows { get; }

    /// <summary>Height of the repeated header rows at the top of this fragment.</summary>
    public double HeaderHeight => Layout.RowTops[RepeatedHeaderRows];


    /// <summary>Fragments of the paragraphs directly inside this fragment's cells (nested paths), for hit testing, caret and selection.</summary>
    public ImmutableArray<ParagraphFragment> CellParagraphs { get; }

    public ImmutableArray<TableFragment> NestedTables { get; }

    /// <summary>Every paragraph fragment inside the table, nested tables included.</summary>
    public IEnumerable<ParagraphFragment> AllParagraphs()
    {
        foreach (ParagraphFragment paragraph in CellParagraphs)
        {
            yield return paragraph;
        }

        foreach (TableFragment nested in NestedTables)
        {
            foreach (ParagraphFragment paragraph in nested.AllParagraphs())
            {
                yield return paragraph;
            }
        }
    }

    /// <summary>Page y of the top of a row on this fragment (repeated header rows sit above the body rows).</summary>
    public double RowTop(int row) => row < RepeatedHeaderRows
        ? Bounds.Top + Layout.RowTops[row]
        : Bounds.Top + HeaderHeight + Layout.RowTops[row] - Layout.RowTops[FirstRow];


    /// <summary>
    /// The innermost cell under a page point: the fragment holding it plus row and cell indices (merged cells
    /// resolve to the cell that starts the merge); null when the point is outside this fragment.
    /// </summary>
    public (TableFragment Table, int Row, int Cell)? CellAt(PointD point)
    {
        if (point.X < Bounds.Left || point.X > Bounds.Right || point.Y < Bounds.Top || point.Y > Bounds.Bottom)
        {
            return null;
        }

        int row = FirstRow;
        if (RepeatedHeaderRows > 0 && point.Y < RowTop(FirstRow))
        {
            row = 0;
            while (row < RepeatedHeaderRows - 1 && point.Y >= RowTop(row + 1))
            {
                row++;
            }
        }
        else
        {
            while (row < LastRow && point.Y >= RowTop(row + 1))
            {
                row++;
            }
        }


        TableRowLayout rowLayout = Layout.Rows[row];
        int cell = CellIndexAt(rowLayout, point.X - Bounds.Left);
        while (row > (row < RepeatedHeaderRows ? 0 : FirstRow) && rowLayout.Cells[cell].Cell.VerticalMerge == VerticalMerge.Continue)
        {
            row--;
            rowLayout = Layout.Rows[row];
            cell = CellIndexAt(rowLayout, point.X - Bounds.Left);
        }

        foreach (TableFragment nested in NestedTables)
        {
            if (nested.Path.CellIndices == (row, cell) && nested.CellAt(point) is { } inner)
            {
                return inner;
            }
        }

        return (this, row, cell);
    }

    private static int CellIndexAt(TableRowLayout row, double x)
    {
        int cell = 0;
        while (cell < row.Cells.Length - 1 && x >= row.Cells[cell + 1].Left)
        {
            cell++;
        }

        return cell;
    }

}
