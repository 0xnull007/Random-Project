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

    public TableLayout(Table table, double left, ImmutableArray<double> columnWidths, ImmutableArray<TableRowLayout> rows)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        Left = left;
        ColumnWidths = columnWidths;
        Rows = rows;
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

/// <summary>Rows [FirstRow, LastRow] of a table placed on a page.</summary>
public sealed class TableFragment : BlockFragment
{
    public TableFragment(StoryId story, BlockPath path, TableLayout layout, int firstRow, int lastRow, double top, double columnLeft)
        : base(story, path, new RectD(columnLeft + layout.Left, top, layout.Width, layout.HeightOfRows(firstRow, lastRow)))
    {
        Layout = layout;
        FirstRow = firstRow;
        LastRow = lastRow;
    }

    public TableLayout Layout { get; }

    public int FirstRow { get; }

    public int LastRow { get; }

    /// <summary>Page y of the top of a row on this fragment.</summary>
    public double RowTop(int row) => Bounds.Top + Layout.RowTops[row] - Layout.RowTops[FirstRow];
}
